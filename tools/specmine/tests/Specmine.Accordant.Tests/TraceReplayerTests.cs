// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant.Tests;

using System.Text.Json;
using Microsoft.Accordant;
using NUnit.Framework;

[TestFixture]
public sealed class TraceReplayerTests
{
    [Test]
    public void Replay_CompleteConformingTraceAcrossMultipleOperations_ReturnsConformingWithFinalState()
    {
        var spec = TaskWorkflowSpec.Create();

        // CompleteTask's request reuses the server-generated TaskId from CreateTask's own
        // response, exercising response-derived state exactly as TaskWorkflowSpec models it.
        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")),
            TraceBuilder.Call(2, "CompleteTask", new CompleteTaskRequest("t-1"), new CompleteTaskResponse("t-1", "Completed")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.TraceId, Is.EqualTo(trace.TraceId));
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Conforming));
            Assert.That(result.Steps, Has.Count.EqualTo(2));
            Assert.That(result.Steps[0].Outcome, Is.EqualTo(ReplayStepOutcome.Conforming));
            Assert.That(result.Steps[1].Outcome, Is.EqualTo(ReplayStepOutcome.Conforming));
            Assert.That(result.FinalStateProfile, Is.Not.Null);
        });

        var finalState = (TaskWorkflowState)result.FinalStateProfile!.SingleState();
        Assert.That(finalState.Tasks["t-1"].Status, Is.EqualTo("Completed"));
        Assert.That(finalState.Tasks["t-1"].Title, Is.EqualTo("Buy milk"));
    }

    [Test]
    public void Replay_EmptyTrace_IsTriviallyConformingWithInitialState()
    {
        var spec = TaskWorkflowSpec.Create();
        var trace = TraceBuilder.Trace(TraceStatus.Completed);

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Conforming));
            Assert.That(result.Steps, Is.Empty);
            Assert.That(result.FinalStateProfile, Is.Not.Null);
        });
    }

    [Test]
    public void Replay_ResponseRejectedByModel_ReportsModelViolationWithExplanation()
    {
        var spec = TaskWorkflowSpec.Create();

        // No CreateTask ever ran for "missing-task", so the model expects NotFound - the
        // trace instead claims the (nonexistent) task was completed.
        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CompleteTask", new CompleteTaskRequest("missing-task"), new CompleteTaskResponse("missing-task", "Completed")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps[0].Outcome, Is.EqualTo(ReplayStepOutcome.ModelViolation));
            Assert.That(result.Steps[0].Message, Does.Contain("NotFound"));
        });
    }

    [Test]
    public void Replay_UnknownOperation_ReportsOperationNotModeledRatherThanViolationOrPass()
    {
        var spec = TaskWorkflowSpec.Create();

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "ArchiveTask", new { taskId = "t-1" }, new { taskId = "t-1", status = "Archived" }));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps[0].Outcome, Is.EqualTo(ReplayStepOutcome.OperationNotModeled));
            Assert.That(result.Steps[0].Message, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Replay_ProvisionalExpectationMatches_ReportsProvisionalAndAdvancesState()
    {
        var spec = Spec.For<TaskWorkflowState>();
        spec.Operation<CreateTaskRequest, CreateTaskResponse>("CreateTask", (request, state) =>
            Understanding.Provisional(
                "create-outcome",
                "What selects creation success?",
                Expect.That<CreateTaskResponse>(
                        response => response.Status == "Open",
                        "Expected an open task.")
                    .ThenState<TaskWorkflowState>((response, next) =>
                    {
                        next.Tasks[response.TaskId] = new TaskRecordState
                        {
                            Title = response.Title,
                            Status = response.Status,
                        };
                    }, mock: () => new CreateTaskResponse("mock", request.Title, "Open"))));

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Provisional));
            Assert.That(result.Steps.Single().Outcome, Is.EqualTo(ReplayStepOutcome.ProvisionalMatch));
            Assert.That(result.Steps.Single().Message, Does.Contain("create-outcome"));
            Assert.That(result.Steps.Single().MarkerId, Is.EqualTo("create-outcome"));
            Assert.That(result.Steps.Single().MarkerKind, Is.EqualTo(UnderstandingKind.Provisional));
            Assert.That(result.AcceptedMatchCount, Is.Zero);
            Assert.That(result.ProvisionalMatchCount, Is.EqualTo(1));
            Assert.That(result.UnknownCount, Is.Zero);
            Assert.That(result.ViolationCount, Is.Zero);
            Assert.That(
                ((TaskWorkflowState)result.FinalStateProfile!.SingleState()).Tasks,
                Does.ContainKey("t-1"));
        });
    }

    [Test]
    public void Replay_ProvisionalExpectationRejects_ReportsModelViolation()
    {
        var spec = Spec.For<TaskWorkflowState>();
        spec.Operation<CreateTaskRequest, CreateTaskResponse>("CreateTask", (_, _) =>
            Understanding.Provisional(
                "create-outcome",
                "What selects creation success?",
                Expect.That<CreateTaskResponse>(
                        response => response.Status == "Open",
                        "Expected an open task.")
                    .SameState()));

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Rejected")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(result.Steps.Single().Outcome, Is.EqualTo(ReplayStepOutcome.ModelViolation));
            Assert.That(result.ViolationCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Replay_UnknownExpectation_ReportsUnknownAndStopsWithoutChangingState()
    {
        var spec = Spec.For<TaskWorkflowState>();
        spec.Operation<CreateTaskRequest, CreateTaskResponse>("CreateTask", (_, _) =>
            Understanding.Unknown<CreateTaskResponse>(
                "create-outcome",
                "Creation behavior has not been investigated."));

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")),
            TraceBuilder.Call(2, "CreateTask", new CreateTaskRequest("Wash car"), new CreateTaskResponse("t-2", "Wash car", "Open")));

        var initialState = TaskWorkflowSpec.InitialState();
        var result = TraceReplayer.Replay(spec, initialState, trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps.Single().Outcome, Is.EqualTo(ReplayStepOutcome.Unknown));
            Assert.That(result.Steps.Single().Message, Does.Contain("create-outcome"));
            Assert.That(result.Steps.Single().MarkerId, Is.EqualTo("create-outcome"));
            Assert.That(result.Steps.Single().MarkerKind, Is.EqualTo(UnderstandingKind.Unknown));
            Assert.That(result.UnknownCount, Is.EqualTo(1));
            Assert.That(result.FinalStateProfile!.SingleState(), Is.SameAs(initialState));
        });
    }

    [Test]
    public void Provisional_CanWrapAnotherProvisional_LastWrapRecordsTheEncounter()
    {
        // Nesting is no longer disallowed: Provisional wraps the validator, not the outcome
        // type, so wrapping twice just double-wraps - harmless, if redundant. The outermost
        // wrap's id/question is what LastEncounter reflects.
        var inner = Understanding.Provisional(
            "inner-question",
            "Is this the inner rule?",
            Expect.That<CreateTaskResponse>(response => response.Status == "Open", "Expected open.").SameState());

        var outer = Understanding.Provisional("outer-question", "Is this the outer rule?", inner);

        Understanding.ClearLastEncounter();
        var (isValid, _) = outer.Matches(new CreateTaskResponse("t-1", "Buy milk", "Open"), TaskWorkflowSpec.InitialState());

        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.True);
            Assert.That(Understanding.LastEncounter, Is.Not.Null);
            Assert.That(Understanding.LastEncounter!.Id, Is.EqualTo("outer-question"));
            Assert.That(Understanding.LastEncounter!.Kind, Is.EqualTo(UnderstandingKind.Provisional));
        });
    }

    [Test]
    public void Replay_RecomposedUnknownOutcome_PreservesUnknownMarker()
    {
        var spec = Spec.For<TaskWorkflowState>();
        spec.Operation<CreateTaskRequest, CreateTaskResponse>("CreateTask", (_, _) =>
        {
            var unknown = Understanding.Unknown<CreateTaskResponse>(
                "create-outcome",
                "Creation behavior has not been investigated.");
            return new ExpectedOutcomes(unknown.PossibleOutcomes.ToArray());
        });

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.That(result.Steps.Single().Outcome, Is.EqualTo(ReplayStepOutcome.Unknown));
    }

    [Test]
    public void Unknown_DefaultStrictness_FailsValidationWithExplanationRatherThanThrowing()
    {
        Understanding.ClearLastEncounter();
        var outcome = Understanding.Unknown<CreateTaskResponse>("create-outcome", "Not yet characterized.");

        var (isValid, _) = outcome.Matches(new CreateTaskResponse("t-1", "Buy milk", "Open"), TaskWorkflowSpec.InitialState());

        Assert.Multiple(() =>
        {
            Assert.That(Understanding.CurrentStrictness, Is.EqualTo(UnderstandingStrictness.Reject));
            Assert.That(isValid, Is.False);
            Assert.That(Understanding.LastEncounter, Is.Not.Null);
            Assert.That(Understanding.LastEncounter!.Kind, Is.EqualTo(UnderstandingKind.Unknown));
            Assert.That(Understanding.LastEncounter!.Id, Is.EqualTo("create-outcome"));
        });
    }

    [Test]
    public void Unknown_AcceptStrictness_PassesValidationAndRecordsEncounter()
    {
        using var scope = Understanding.UseStrictness(UnderstandingStrictness.Accept);
        Understanding.ClearLastEncounter();
        var outcome = Understanding.Unknown<CreateTaskResponse>("create-outcome", "Not yet characterized.");

        var (isValid, stateProfile) = outcome.Matches(
            new CreateTaskResponse("t-1", "Buy milk", "Open"),
            TaskWorkflowSpec.InitialState());

        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.True);
            Assert.That(stateProfile, Is.Not.Null);
            Assert.That(Understanding.LastEncounter, Is.Not.Null);
            Assert.That(Understanding.LastEncounter!.Kind, Is.EqualTo(UnderstandingKind.Unknown));
        });
    }

    [Test]
    public void Unknown_StrictStrictness_ThrowsUnconditionallyOnEvaluation()
    {
        using var scope = Understanding.UseStrictness(UnderstandingStrictness.Strict);
        var outcome = Understanding.Unknown<CreateTaskResponse>("create-outcome", "Not yet characterized.");

        var exception = Assert.Throws<UnknownRegionEncounteredException>(
            () => outcome.Matches(new CreateTaskResponse("t-1", "Buy milk", "Open"), TaskWorkflowSpec.InitialState()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Id, Is.EqualTo("create-outcome"));
            Assert.That(exception, Is.InstanceOf<UnderstandingException>());
        });
    }

    [Test]
    public void Provisional_NonMatchingResponse_StaysOrdinaryRejectionEvenUnderStrict()
    {
        using var scope = Understanding.UseStrictness(UnderstandingStrictness.Strict);
        Understanding.ClearLastEncounter();
        var outcome = Understanding.Provisional(
            "create-outcome",
            "Still needs refinement?",
            Expect.That<CreateTaskResponse>(response => response.Status == "Open", "Expected open.").SameState());

        var (isValid, _) = outcome.Matches(
            new CreateTaskResponse("t-1", "Buy milk", "Rejected"),
            TaskWorkflowSpec.InitialState());

        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.False, "A non-matching response must not be treated as a Understanding encounter.");
            Assert.That(Understanding.LastEncounter, Is.Null);
        });
    }

    [Test]
    public void Provisional_MatchingResponse_StrictStrictnessThrowsInsteadOfPassing()
    {
        using var scope = Understanding.UseStrictness(UnderstandingStrictness.Strict);
        var outcome = Understanding.Provisional(
            "create-outcome",
            "Still needs refinement?",
            Expect.That<CreateTaskResponse>(response => response.Status == "Open", "Expected open.").SameState());

        var exception = Assert.Throws<ProvisionalMatchEncounteredException>(
            () => outcome.Matches(new CreateTaskResponse("t-1", "Buy milk", "Open"), TaskWorkflowSpec.InitialState()));

        Assert.That(exception!.Id, Is.EqualTo("create-outcome"));
    }

    [Test]
    public void Replay_UnderStrictStrictness_PropagatesUnknownExceptionInsteadOfReportingStep()
    {
        var spec = Spec.For<TaskWorkflowState>();
        spec.Operation<CreateTaskRequest, CreateTaskResponse>("CreateTask", (_, _) =>
            Understanding.Unknown<CreateTaskResponse>("create-outcome", "Not yet characterized."));

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")));

        using var scope = Understanding.UseStrictness(UnderstandingStrictness.Strict);

        Assert.Throws<UnknownRegionEncounteredException>(
            () => TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace));
    }

    [Test]
    public void Assume_ConditionSatisfied_DoesNotThrow()
    {
        Assert.That(() => Understanding.Assume(true, "create-scope", "Only short titles are modeled."), Throws.Nothing);
    }

    [Test]
    public void Assume_ConditionViolated_ThrowsAssumptionViolatedExceptionWithIdAndReason()
    {
        var exception = Assert.Throws<AssumptionViolatedException>(
            () => Understanding.Assume(false, "create-scope", "Only short titles are modeled."));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Id, Is.EqualTo("create-scope"));
            Assert.That(exception.Reason, Is.EqualTo("Only short titles are modeled."));
            Assert.That(exception, Is.InstanceOf<UnderstandingException>());
            Assert.That(exception.Message, Does.Contain("create-scope").And.Contain("Only short titles are modeled."));
        });
    }

    [Test]
    public void Replay_AssumptionViolated_ReportsOutOfScopeAndStopsWithoutChangingState()
    {
        var spec = Spec.For<TaskWorkflowState>();
        spec.Operation<CreateTaskRequest, CreateTaskResponse>("CreateTask", (request, _) =>
        {
            Understanding.Assume(
                request.Title.Length <= 10,
                "create-scope",
                "Only short titles (<= 10 chars) are modeled so far.");

            return Expect.That<CreateTaskResponse>(response => response.Status == "Open", "Expected an open task.")
                .SameState();
        });

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(
                1,
                "CreateTask",
                new CreateTaskRequest("A very long task title indeed"),
                new CreateTaskResponse("t-1", "A very long task title indeed", "Open")));

        var initialState = TaskWorkflowSpec.InitialState();
        var result = TraceReplayer.Replay(spec, initialState, trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps.Single().Outcome, Is.EqualTo(ReplayStepOutcome.OutOfScope));
            Assert.That(result.Steps.Single().Message, Does.Contain("create-scope"));
            Assert.That(result.Steps.Single().MarkerId, Is.EqualTo("create-scope"));
            Assert.That(result.Steps.Single().MarkerKind, Is.Null);
            Assert.That(result.OutOfScopeCount, Is.EqualTo(1));
            Assert.That(result.UnknownCount, Is.Zero);
            Assert.That(result.ViolationCount, Is.Zero);
            Assert.That(result.FinalStateProfile!.SingleState(), Is.SameAs(initialState));
        });
    }

    [Test]
    public void Replay_AssumptionSatisfied_ReplaysNormallyAsConforming()
    {
        var spec = Spec.For<TaskWorkflowState>();
        spec.Operation<CreateTaskRequest, CreateTaskResponse>("CreateTask", (request, _) =>
        {
            Understanding.Assume(
                request.Title.Length <= 10,
                "create-scope",
                "Only short titles (<= 10 chars) are modeled so far.");

            return Expect.That<CreateTaskResponse>(response => response.Status == "Open", "Expected an open task.")
                .SameState();
        });

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Conforming));
            Assert.That(result.Steps.Single().Outcome, Is.EqualTo(ReplayStepOutcome.Conforming));
            Assert.That(result.OutOfScopeCount, Is.Zero);
        });
    }

    [Test]
    public void Replay_PartialModelTrace_StopsConservativelyAtUnmodeledOperationRatherThanPassing()
    {
        var spec = TaskWorkflowSpec.Create();

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")),
            TraceBuilder.Call(2, "CompleteTask", new CompleteTaskRequest("t-1"), new CompleteTaskResponse("t-1", "Completed")),
            TraceBuilder.Call(3, "ArchiveTask", new { taskId = "t-1" }, new { taskId = "t-1", status = "Archived" }),
            // Well-formed and would itself be conforming - included to prove replay never
            // reaches it once an unmodeled operation has been hit.
            TraceBuilder.Call(4, "CreateTask", new CreateTaskRequest("Wash car"), new CreateTaskResponse("t-2", "Wash car", "Open")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(result.Steps, Has.Count.EqualTo(3), "Replay must stop at the unmodeled call, not continue to call 4.");
            Assert.That(result.Steps[0].Outcome, Is.EqualTo(ReplayStepOutcome.Conforming));
            Assert.That(result.Steps[1].Outcome, Is.EqualTo(ReplayStepOutcome.Conforming));
            Assert.That(result.Steps[2].Outcome, Is.EqualTo(ReplayStepOutcome.OperationNotModeled));
        });
    }

    [Test]
    public void Replay_ExecutionErrorCall_ReportsExecutionErrorRatherThanViolationOrPass()
    {
        var spec = TaskWorkflowSpec.Create();

        var trace = TraceBuilder.Trace(
            TraceStatus.Interrupted,
            TraceBuilder.ErrorCall(
                1, "CreateTask", new CreateTaskRequest("Buy milk"), "System.TimeoutException", "The operation timed out."));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps[0].Outcome, Is.EqualTo(ReplayStepOutcome.ExecutionError));
            Assert.That(result.Steps[0].Message, Does.Contain("System.TimeoutException").And.Contains("timed out"));
        });
    }

    [Test]
    public void Replay_RequestDeserializationFailure_ReportsRequestDeserializationFailed()
    {
        var spec = TaskWorkflowSpec.Create();

        // "title" is a JSON number, but CreateTaskRequest.Title is a string.
        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.RawCall(1, "CreateTask", "{\"title\":123}", "{\"taskId\":\"t-1\",\"title\":\"x\",\"status\":\"Open\"}"));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps[0].Outcome, Is.EqualTo(ReplayStepOutcome.RequestDeserializationFailed));
            Assert.That(result.Steps[0].Message, Does.Contain("CreateTaskRequest"));
        });
    }

    [Test]
    public void Replay_ResponseDeserializationFailure_ReportsResponseDeserializationFailed()
    {
        var spec = TaskWorkflowSpec.Create();

        // "taskId" is a JSON boolean, but CreateTaskResponse.TaskId is a string.
        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.RawCall(1, "CreateTask", "{\"title\":\"Buy milk\"}", "{\"taskId\":true,\"title\":\"Buy milk\",\"status\":\"Open\"}"));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(result.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Steps[0].Outcome, Is.EqualTo(ReplayStepOutcome.ResponseDeserializationFailed));
            Assert.That(result.Steps[0].Message, Does.Contain("CreateTaskResponse"));
        });
    }

    [Test]
    public void Replay_UnsupportedSchemaVersion_ReportsUnsupportedSchemaVersionWithNoSteps()
    {
        var spec = TaskWorkflowSpec.Create();

        var trace = TraceBuilder.Trace(
            schemaVersion: 999,
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.UnsupportedSchemaVersion));
            Assert.That(result.Steps, Is.Empty);
            Assert.That(result.FinalStateProfile, Is.Null);
            Assert.That(result.Message, Does.Contain("999"));
        });
    }

    [Test]
    public void Replay_NonPositiveCallId_ReportsInvalidTraceStructure()
    {
        var spec = TaskWorkflowSpec.Create();

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(0, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.InvalidTraceStructure));
            Assert.That(result.Steps, Is.Empty);
            Assert.That(result.FinalStateProfile, Is.Null);
            Assert.That(result.Message, Does.Contain("not positive"));
        });
    }

    [Test]
    public void Replay_OutOfOrderCallIds_ReportsInvalidTraceStructure()
    {
        var spec = TaskWorkflowSpec.Create();

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(2, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")),
            TraceBuilder.Call(1, "CompleteTask", new CompleteTaskRequest("t-1"), new CompleteTaskResponse("t-1", "Completed")));

        var result = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(TraceReplayStatus.InvalidTraceStructure));
            Assert.That(result.Steps, Is.Empty);
            Assert.That(result.FinalStateProfile, Is.Null);
            Assert.That(result.Message, Does.Contain("strictly"));
        });
    }

    [Test]
    public async Task ReplayAsync_TracePathOverload_MatchesInMemoryReplay()
    {
        using var tracesDirectory = new TestTracesDirectory();
        var spec = TaskWorkflowSpec.Create();

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")),
            TraceBuilder.Call(2, "CompleteTask", new CompleteTaskRequest("t-1"), new CompleteTaskResponse("t-1", "Completed")));

        var path = await TraceStore.SaveAsync(tracesDirectory.Path, trace);

        var inMemoryResult = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);
        var pathResult = await TraceReplayer.ReplayAsync(spec, TaskWorkflowSpec.InitialState(), path);

        Assert.Multiple(() =>
        {
            Assert.That(pathResult.TraceId, Is.EqualTo(inMemoryResult.TraceId));
            Assert.That(pathResult.Status, Is.EqualTo(inMemoryResult.Status));
            Assert.That(pathResult.Steps, Is.EqualTo(inMemoryResult.Steps));
        });
    }

    [Test]
    public void Replay_CustomSerializerOptions_ChangesOutcomeVersusDefaultOptions()
    {
        var spec = TaskWorkflowSpec.Create();

        // "task_id" only binds to CreateTaskResponse.TaskId under a snake_case naming
        // policy. Under the default (camelCase, case-insensitive) options it deserializes
        // with a null TaskId, which the model then rejects (TaskId must be non-empty) -
        // a silent-looking model violation that is really a serializer mismatch.
        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.RawCall(
                1,
                "CreateTask",
                "{\"title\":\"Buy milk\"}",
                "{\"task_id\":\"t-custom\",\"title\":\"Buy milk\",\"status\":\"Open\"}"));

        var defaultResult = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace);

        Assert.Multiple(() =>
        {
            Assert.That(defaultResult.Status, Is.EqualTo(TraceReplayStatus.Stopped));
            Assert.That(defaultResult.Steps[0].Outcome, Is.EqualTo(ReplayStepOutcome.ModelViolation));
        });

        var snakeCaseOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var customResult = TraceReplayer.Replay(spec, TaskWorkflowSpec.InitialState(), trace, snakeCaseOptions);

        Assert.Multiple(() =>
        {
            Assert.That(customResult.Status, Is.EqualTo(TraceReplayStatus.Conforming));
            Assert.That(customResult.Steps[0].Outcome, Is.EqualTo(ReplayStepOutcome.Conforming));

            var finalState = (TaskWorkflowState)customResult.FinalStateProfile!.SingleState();
            Assert.That(finalState.Tasks["t-custom"].Title, Is.EqualTo("Buy milk"));
        });
    }

    [Test]
    public void Replay_DoesNotMutateInitialStateAndIsRepeatable()
    {
        var spec = TaskWorkflowSpec.Create();

        var trace = TraceBuilder.Trace(
            TraceStatus.Completed,
            TraceBuilder.Call(1, "CreateTask", new CreateTaskRequest("Buy milk"), new CreateTaskResponse("t-1", "Buy milk", "Open")),
            TraceBuilder.Call(2, "CompleteTask", new CompleteTaskRequest("t-1"), new CompleteTaskResponse("t-1", "Completed")));

        var initialState = TaskWorkflowSpec.InitialState();

        var firstResult = TraceReplayer.Replay(spec, initialState, trace);

        Assert.That(initialState.Tasks, Is.Empty, "Replay must not mutate the caller's initial state.");

        var secondResult = TraceReplayer.Replay(spec, initialState, trace);

        Assert.Multiple(() =>
        {
            Assert.That(secondResult.Status, Is.EqualTo(firstResult.Status));
            Assert.That(secondResult.Steps, Is.EqualTo(firstResult.Steps));
            Assert.That(initialState.Tasks, Is.Empty, "Replay must not mutate the caller's initial state, even when repeated.");
        });
    }
}
