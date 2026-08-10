// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

[TestFixture]
public class ExperimentalCoroutineTests
{
    [Test]
    public void ReadIsReplayedAndStepsMutateOnlyWhenTheirEdgesAreSelected()
    {
        var firstCalls = 0;
        var secondCalls = 0;

        async ModelTask Workflow(ModelContext<CounterState> context)
        {
            var observed = await context.Read("initial-count", state => state.Count);
            await context.Step("first", state =>
            {
                firstCalls++;
                state.Count = observed + 1;
            });
            await context.Step("second", state =>
            {
                secondCalls++;
                state.Count++;
            });
        }

        var root = CoroutineModel.Explore("replay", new CounterState { Count = 5 }, Workflow);
        var first = root.Edges.Single();
        var second = first.Target.Edges.Single();
        var terminal = second.Target;

        Assert.That(((CounterState)terminal.State).Count, Is.EqualTo(7));
        Assert.That(firstCalls, Is.EqualTo(1));
        Assert.That(secondCalls, Is.EqualTo(1));
        Assert.That(terminal.StepFunctions, Is.Empty);
        Assert.That(terminal.Edges, Is.Empty);
    }

    [Test]
    public void ChooseCreatesFiniteVisibleBranchesAndReadDoesNot()
    {
        var root = CoroutineModel.Explore("branch", new CounterState { Count = 3 }, BranchingWorkflow);

        Assert.That(root.Edges, Has.Count.EqualTo(2));
        Assert.That(
            root.Edges.Select(edge => ((CoroutineTransition)edge.Metadata).ToString()),
            Is.EquivalentTo(new[] { "choose:delta=1", "choose:delta=2" }));

        var terminalCounts = root.Edges
            .Select(edge => edge.Target.Edges.Single().Target)
            .Select(node => ((CounterState)node.State).Count);
        Assert.That(terminalCounts, Is.EquivalentTo(new[] { 4, 5 }));
    }

    [Test]
    public void VisibleCheckpointMetadataAndActionsExposeTypedReplayPrefixes()
    {
        var root = CoroutineModel.Explore("branch", new CounterState(), BranchingWorkflow);
        var choose = (CoroutineTransition)root.Edges.First().Metadata;
        var afterChoose = root.Edges.First().Target;
        var claimAction = (ICoroutineCheckpointStep)afterChoose.StepFunctions.Single();
        var step = (CoroutineTransition)afterChoose.Edges.Single().Metadata;

        Assert.That(
            choose.ReplayPrefix.Select(entry => (entry.Kind, entry.Name, entry.Value)),
            Is.EqualTo(new[] { (ModelCheckpointKind.Read, "count", (object)0) }));
        Assert.That(choose.Kind, Is.EqualTo(ModelCheckpointKind.Choose));
        Assert.That(claimAction.CheckpointKind, Is.EqualTo(ModelCheckpointKind.Step));
        Assert.That(claimAction.CheckpointName, Is.EqualTo("apply-delta"));
        Assert.That(
            claimAction.ReplayPrefix.Select(entry => (entry.Kind, entry.Name, entry.Value)),
            Is.EqualTo(new[] { (ModelCheckpointKind.Read, "count", (object)0), (ModelCheckpointKind.Choose, "delta", (object)1) }));
        Assert.That(
            step.ReplayPrefix.Select(entry => (entry.Kind, entry.Name, entry.Value)),
            Is.EqualTo(claimAction.ReplayPrefix.Select(entry => (entry.Kind, entry.Name, entry.Value))));
    }

    [Test]
    public void ContextDoesNotExposeStateAndMutableReplayValuesAreRejected()
    {
        Assert.That(typeof(ModelContext<CounterState>).GetProperty("State"), Is.Null);

        async ModelTask BadValue(ModelContext<CounterState> context)
        {
            await context.Read("bad", _ => new List<int> { 1 });
        }

        var error = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore("bad-value", new CounterState(), BadValue));
        Assert.That(error.Message, Does.Contain("unsupported value type"));
    }

    [Test]
    public void GraphIdentityIsStableAndExistingPropertyCheckingWorks()
    {
        var first = CoroutineModel.Explore("branch", new CounterState { Count = 0 }, BranchingWorkflow);
        var second = CoroutineModel.Explore("branch", new CounterState { Count = 0 }, BranchingWorkflow);

        Assert.That(first.GetNodeFingerprint(), Is.EqualTo(second.GetNodeFingerprint()));
        Assert.That(
            first.Edges.Select(edge => edge.StepFunction.StepFunctionId),
            Is.EqualTo(second.Edges.Select(edge => edge.StepFunction.StepFunctionId)));

        var formula = Formula.For<CounterState>();
        var completed = formula.Observe(state => state.Count > 0, "Completed");
        Assert.That(first.Check(formula.Eventually(completed)).Valid, Is.True);
    }

    [Test]
    public void ExistingTemporalRefinementAcceptsCoroutineGraphs()
    {
        var concrete = CoroutineModel.Explore("branch", new CounterState(), BranchingWorkflow);
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AbstractLoopStep() },
            new AbstractState());

        var result = Microsoft.Accordant.ModelChecking.Refinement
            .Between<CounterState, AbstractState>(concrete, abstraction)
            .Map(_ => new AbstractState())
            .MapTransition(_ => AbstractResponse.Step<AbstractLoopStep>())
            .CheckTemporal();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void RepeatedCheckpointNamesGrowTheNaiveTapeUntilTheGraphDepthBound()
    {
        async ModelTask Repeated(ModelContext<CounterState> context)
        {
            while (true)
            {
                await context.Step("toggle", state => state.Count = 1 - state.Count);
            }
        }

        var root = CoroutineModel.Explore(
            "repeated",
            new CounterState(),
            Repeated,
            maxDepth: 3);
        var nodes = Reachable(root).ToArray();
        var activeTapes = nodes
            .Where(node => node.StepFunctions.Count != 0)
            .Select(node => ((ICoroutineCheckpointStep)node.StepFunctions.Single()).ReplayPrefix.Count)
            .ToArray();
        var formula = Formula.For<CounterState>();
        var neverReached = formula.Observe(state => state.Count == 99, "never-reached");

        Assert.That(nodes, Has.Length.EqualTo(3));
        Assert.That(root.GetNodeFingerprint(), Is.Not.EqualTo(nodes[2].GetNodeFingerprint()),
            "the domain state and Step location repeat, but the full replay tape changes identity");
        Assert.That(activeTapes, Is.EquivalentTo(new[] { 0, 1, 2 }));
        Assert.That(nodes.Last().IsDepthFrontier, Is.True);
        Assert.That(
            root.Check(formula.Eventually(neverReached)).Status,
            Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
    }

    [Test]
    public void LoopRebasesTheTapeAndCreatesAnOrdinaryGraphCycle()
    {
        var root = CoroutineModel.Explore("loop", new CounterState(), LoopingWorkflow);
        var nodes = Reachable(root).ToArray();
        var formula = Formula.For<CounterState>().AllowStutterSensitiveFormulas();
        var toggleEnabled = formula.Enabled(step =>
            step is ICoroutineCheckpointStep checkpoint &&
            checkpoint.CheckpointKind == ModelCheckpointKind.Step &&
            checkpoint.CheckpointName == "toggle");

        Assert.That(nodes, Has.Length.EqualTo(2));
        Assert.That(nodes.All(node => node.Edges.Single().Target != null), Is.True);
        Assert.That(nodes.SelectMany(node => node.Edges).Select(edge =>
            ((CoroutineTransition)edge.Metadata).Kind), Is.All.EqualTo(ModelCheckpointKind.Step));
        Assert.That(
            nodes.SelectMany(node => node.Edges)
                .Select(edge => ((CoroutineTransition)edge.Metadata).CheckpointName),
            Is.All.EqualTo("toggle"));
        Assert.That(
            root.Check(formula.Always(toggleEnabled), fairness: Fairness.Weak(
                step => step is ICoroutineCheckpointStep)).Status,
            Is.EqualTo(PropertyCheckingStatus.Holds));
        Assert.That(
            root.Check(formula.Always(toggleEnabled), fairness: Fairness.Strong(
                step => step is ICoroutineCheckpointStep)).Status,
            Is.EqualTo(PropertyCheckingStatus.Holds));
    }

    [Test]
    public void LoopPersistentValuePreventsAMergeUntilTheValueRepeats()
    {
        var root = CoroutineModel.Explore("persistent-loop", new CounterState(), PersistentLoopWorkflow);
        var first = root;
        var second = first.Edges.Single().Target;
        var third = second.Edges.Single().Target;

        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(third, Is.Not.SameAs(first));
        Assert.That(third.Edges.Single().Target, Is.SameAs(first));
        Assert.That(
            new[] { first, second, third }
                .Select(node => ((ICoroutineCheckpointStep)node.StepFunctions.Single())
                .ReplayPrefix.Single(entry => entry.Kind == ModelCheckpointKind.Loop)
                .Value),
            Is.EqualTo(new object[] { 0, 1, 2 }));
    }

    [Test]
    public void LoopPersistentValuesUseTheSameImmutableScalarRules()
    {
        async ModelTask MutablePersistentValue(ModelContext<CounterState> context)
        {
            await context.LoopState("iteration", new List<int> { 1 });
        }

        var error = Assert.Throws<ModelDefinitionException>(() =>
            CoroutineModel.Explore(
                "mutable-loop-value",
                new CounterState(),
                MutablePersistentValue));

        Assert.That(error.Message, Does.Contain("unsupported value type"));
    }

    [Test]
    public void MultipleLoopBoundariesAreRejected()
    {
        async ModelTask Ambiguous(ModelContext<CounterState> context)
        {
            await context.Loop("iteration");
            await context.Step("tick", _ => { });
            await context.Loop("iteration");
        }

        var error = Assert.Throws<StepFunctionApplicationException>(() =>
            CoroutineModel.Explore("ambiguous-loop", new CounterState(), Ambiguous));

        Assert.That(error.InnerException, Is.TypeOf<ModelDefinitionException>());
        Assert.That(error.InnerException.Message, Does.Contain("support one Loop boundary"));
    }

    [Test]
    public void LoopMustBeTheFirstAccordantCheckpoint()
    {
        async ModelTask Misplaced(ModelContext<CounterState> context)
        {
            await context.Read("count", state => state.Count);
            await context.Loop("iteration");
            await context.Step("tick", _ => { });
        }

        var error = Assert.Throws<ModelDefinitionException>(() =>
            CoroutineModel.Explore("misplaced-loop", new CounterState(), Misplaced));

        Assert.That(error.Message, Does.Contain("first Accordant checkpoint"));
    }

    [Test]
    public void LoopSupportsStringPersistentValues()
    {
        async ModelTask StringPhase(ModelContext<CounterState> context)
        {
            var phase = "zero";
            while (true)
            {
                phase = await context.LoopState("iteration", phase);
                await context.Step("tick", _ => { });
                phase = phase == "zero" ? "one" : "zero";
            }
        }

        var root = CoroutineModel.Explore("string-loop", new CounterState(), StringPhase);
        var first = (ICoroutineCheckpointStep)root.StepFunctions.Single();
        var second = (ICoroutineCheckpointStep)root.Edges.Single().Target.StepFunctions.Single();

        Assert.That(first.ReplayPrefix.Single().Value, Is.EqualTo("zero"));
        Assert.That(second.ReplayPrefix.Single().Value, Is.EqualTo("one"));
    }

    [Test]
    public void LoopIsInternalAndStepTransitionsRefineAnOrdinaryToggleGraph()
    {
        var concrete = CoroutineModel.Explore("loop-refinement", new CounterState(), LoopingWorkflow);
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new ToggleStep() },
            new CounterState());

        var result = Microsoft.Accordant.ModelChecking.Refinement
            .Between<CounterState, CounterState>(concrete, abstraction)
            .Map(state => new CounterState { Count = state.Count })
            .MapTransition(transition =>
            {
                var checkpoint = (CoroutineTransition)transition.Metadata;
                return checkpoint.Kind == ModelCheckpointKind.Step &&
                    checkpoint.CheckpointName == "toggle"
                    ? AbstractResponse.Step(step => step.StepFunctionId == "toggle")
                    : throw new AssertionException("Loop must not create a visible transition.");
            })
            .CheckTemporal(
                concreteFairness: Fairness.Weak(step => step is ICoroutineCheckpointStep),
                abstractFairness: Fairness.Weak(step => step.StepFunctionId == "toggle"));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void DefinitionErrorsAreClearForExternalAwaits()
    {
        async ModelTask ExternalAwait(ModelContext<CounterState> context)
        {
            await Task.Yield();
            await context.Step("unreachable", _ => { });
        }

        var external = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore("external", new CounterState(), ExternalAwait));
        Assert.That(external.Message, Does.Contain("only supports incomplete awaits"));
        Assert.That(external.Message, Does.Contain("YieldAwaitable"));
    }

    [Test]
    public void ForeignAwaitsThatCompleteSynchronouslyAreNotRejectedByTheRuntime()
    {
        async ModelTask CompletedForeignAwait(ModelContext<CounterState> context)
        {
            await Task.CompletedTask;
            await context.Step("tick", state => state.Count++);
        }

        // Documents a runtime limitation, not an endorsement: the compiler resumes a
        // synchronously completed awaiter inline, so ModelTaskMethodBuilder never sees
        // it and cannot reject it. Only its observable effects can be detected.
        var root = CoroutineModel.Explore("completed-foreign-await", new CounterState(), CompletedForeignAwait);

        Assert.That(((CounterState)root.Edges.Single().Target.State).Count, Is.EqualTo(1));
    }

    [Test]
    public void ForeignSynchronousAwaitsAreCaughtOnlyWhenTheyChangeObservableBehaviour()
    {
        foreignAwaitResults = 0;

        async ModelTask VaryingForeignAwait(ModelContext<CounterState> context)
        {
            var even = await Task.FromResult(foreignAwaitResults++ % 2 == 0);
            if (even)
            {
                await context.Step("even", state => state.Count++);
            }
            else
            {
                await context.Step("odd", state => state.Count--);
            }
        }

        var error = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore(
                "varying-foreign-await",
                new CounterState(),
                VaryingForeignAwait,
                verifyDeterminism: true));

        Assert.That(error.Message, Does.Contain("is not deterministic under replay"));
        Assert.That(error.Message, Does.Contain("Step checkpoint 'even'"));
        Assert.That(error.Message, Does.Contain("Step checkpoint 'odd'"));
    }

    [Test]
    public void ImpureSelectorsAreRejectedWithTheOffendingCheckpointNamed()
    {
        var readings = 0;

        async ModelTask ImpureRead(ModelContext<CounterState> context)
        {
            var observed = await context.Read("clock-like", state => state.Count + readings++);
            await context.Step("tick", state => state.Count = observed);
        }

        var error = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore(
                "impure-read",
                new CounterState(),
                ImpureRead,
                verifyDeterminism: true));

        Assert.That(error.Message, Does.Contain("Read checkpoint 'clock-like'"));
        Assert.That(error.Message, Does.Contain("is not a function of the frozen model state"));
    }

    [Test]
    public void MutatingACapturedVariableInTheReplayedBodyIsRejected()
    {
        var invocations = 0;

        async ModelTask CountingBody(ModelContext<CounterState> context)
        {
            invocations++;
            await context.Step("tick", state => state.Count++);
        }

        var error = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore(
                "captured-counter",
                new CounterState(),
                CountingBody,
                verifyDeterminism: true));

        Assert.That(error.Message, Does.Contain("mutated the captured external variable"));
        Assert.That(error.Message, Does.Contain("invocations"));
    }

    [Test]
    public void CapturedInputsAreReportedWithTheirMonitoringStrength()
    {
        var limit = 3;
        var log = new List<string>();

        async ModelTask CapturingWorkflow(ModelContext<CounterState> context)
        {
            await context.Step("tick", state => state.Count = limit + log.Count);
        }

        var captured = CoroutineModel
            .DescribeCapturedInputs<CounterState>(CapturingWorkflow)
            .ToDictionary(input => input.Name, input => input.Monitoring);

        Assert.That(captured["limit"], Is.EqualTo(CoroutineCapturedInputMonitoring.ImmutableScalar));
        Assert.That(captured["log"], Is.EqualTo(CoroutineCapturedInputMonitoring.ReferenceIdentityOnly));
    }

    [Test]
    public void StaticWorkflowCapturedInputsAreReportedAsNotAnalyzable()
    {
        var captured = CoroutineModel
            .DescribeCapturedInputs<CounterState>(CountedWorkflow)
            .Single();

        Assert.That(captured.Monitoring, Is.EqualTo(CoroutineCapturedInputMonitoring.NotAnalyzable));
    }

    [Test]
    public void ReplayThatCompletesWithoutConsumingItsTapeIsRejected()
    {
        truncatingInvocations = 0;

        async ModelTask Truncating(ModelContext<CounterState> context)
        {
            await context.Read("a", state => state.Count);
            if (truncatingInvocations++ < 1)
            {
                await context.Read("b", state => state.Count);
            }
        }

        var error = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore(
                "truncating-replay",
                new CounterState(),
                Truncating,
                verifyDeterminism: true));

        Assert.That(error.Message, Does.Contain("completed after replaying only 1 of 2 recorded checkpoints"));
    }

    [Test]
    public void ReplayedCheckpointValueTypesMustStayStable()
    {
        shiftingInvocations = 0;

        async ModelTask Shifting(ModelContext<CounterState> context)
        {
            if (shiftingInvocations++ == 0)
            {
                await context.Read("value", state => state.Count);
            }
            else
            {
                await context.Read("value", state => (long)state.Count);
            }

            await context.Step("tick", _ => { });
        }

        var error = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore(
                "shifting-value-type",
                new CounterState(),
                Shifting,
                verifyDeterminism: true));

        Assert.That(error.Message, Does.Contain("Read checkpoint 'value'"));
        Assert.That(error.Message, Does.Contain("value type must be stable across replays"));
    }

    [Test]
    public void RecordedReplayValuesAreAlwaysImmutableScalars()
    {
        var root = CoroutineModel.Explore("scalar-tape", new CounterState(), PersistentLoopWorkflow);

        var recorded = Reachable(root)
            .Where(node => node.StepFunctions.Count != 0)
            .SelectMany(node => ((ICoroutineCheckpointStep)node.StepFunctions.Single()).ReplayPrefix)
            .Select(entry => entry.Value)
            .ToArray();

        Assert.That(recorded, Is.Not.Empty);
        Assert.That(recorded.All(IsImmutableScalar), Is.True, "every recorded replay value must be an immutable scalar");
    }

    [Test]
    public void StepIdentityDistinguishesDifferentChoiceSetsAtTheSameCheckpoint()
    {
        async ModelTask StateDependentChoices(ModelContext<CounterState> context)
        {
            var delta = await context.Choose(
                "delta",
                state => state.Count == 0 ? new[] { 1 } : new[] { 1, 2 });
            await context.Step("apply", state => state.Count += delta);
        }

        string RootStepId(int count) => CoroutineModel
            .Explore("choice-identity", new CounterState { Count = count }, StateDependentChoices)
            .StepFunctions.Single().StepFunctionId;

        Assert.That(RootStepId(0), Is.Not.EqualTo(RootStepId(1)));
        Assert.That(RootStepId(1), Is.EqualTo(RootStepId(2)));
    }

    [Test]
    public void StepIdentityIgnoresChoiceEnumerationOrder()
    {
        async ModelTask Choices(ModelContext<CounterState> context)
        {
            await context.Choose(
                "delta",
                state => state.Count == 0 ? new[] { 1, 2 } : new[] { 2, 1 });
        }

        string RootStepId(int count) => CoroutineModel
            .Explore("choice-order", new CounterState { Count = count }, Choices)
            .StepFunctions.Single().StepFunctionId;

        Assert.That(RootStepId(0), Is.EqualTo(RootStepId(1)));
    }

    [Test]
    public void StepIdentityEncodingSeparatesPunctuatedWorkflowAndCheckpointNames()
    {
        async ModelTask Tick(ModelContext<CounterState> context)
        {
            await context.Step("b", state => state.Count++);
        }

        async ModelTask PunctuatedTick(ModelContext<CounterState> context)
        {
            await context.Step("a:step:b", state => state.Count++);
        }

        var nested = CoroutineModel.Explore("w:step:a", new CounterState(), Tick)
            .StepFunctions.Single().StepFunctionId;
        var punctuated = CoroutineModel.Explore("w", new CounterState(), PunctuatedTick)
            .StepFunctions.Single().StepFunctionId;

        Assert.That(nested, Is.Not.EqualTo(punctuated));
    }

    [Test]
    public void StepIdentityIncludesTheActionMethod()
    {
        async ModelTask VaryingAction(ModelContext<CounterState> context)
        {
            if (alternateAction)
            {
                await context.Step("same-name", state => state.Count++);
            }
            else
            {
                await context.Step("same-name", state => state.Count--);
            }
        }

        alternateAction = false;
        var decrement = CoroutineModel
            .Explore("action-identity", new CounterState(), VaryingAction)
            .StepFunctions.Single().StepFunctionId;
        alternateAction = true;
        var increment = CoroutineModel
            .Explore("action-identity", new CounterState(), VaryingAction)
            .StepFunctions.Single().StepFunctionId;

        Assert.That(increment, Is.Not.EqualTo(decrement));
    }

    [Test]
    public void TheDeterminismAuditRunsTheBodyTwiceAndCanBeDisabled()
    {
        auditedInvocations = 0;
        CoroutineModel.Explore("audit-off", new CounterState(), CountedWorkflow);
        var withoutAudit = auditedInvocations;

        auditedInvocations = 0;
        CoroutineModel.Explore(
            "audit-on",
            new CounterState(),
            CountedWorkflow,
            verifyDeterminism: true);
        var withAudit = auditedInvocations;

        Assert.That(withoutAudit, Is.GreaterThan(0));
        Assert.That(withAudit, Is.EqualTo(withoutAudit * 2));
    }

    [Test]
    public void ReplayOnlyInfiniteLoopsStopAtTheInternalCheckpointBound()
    {
        async ModelTask ReadForever(ModelContext<CounterState> context)
        {
            while (true)
            {
                await context.Read("tick", state => state.Count);
            }
        }

        var error = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore(
                "read-forever",
                new CounterState(),
                ReadForever,
                maxInternalCheckpoints: 25));

        Assert.That(
            error.Message,
            Does.Contain("did not reach Choose, Step, or completion after 25 internal Read or Loop checkpoints"));
    }

    [Test]
    [Timeout(30000)]
    public void ASynchronousLoopThatTakesNoCheckpointCannotBeInterruptedByTheRuntime()
    {
        // A literal `while (true) { }` would hang this test process forever, which is the
        // limitation itself. The loop below is identical from the runtime's point of view
        // (it reaches no checkpoint and never yields), but the test can release it, so the
        // limitation is observed without hanging.
        spinning = true;
        StateGraphNode root = null;
        Exception failure = null;
        var entered = new ManualResetEventSlim(false);

        async ModelTask SpinThenStep(ModelContext<CounterState> context)
        {
            entered.Set();
            while (Volatile.Read(ref spinning))
            {
            }

            await context.Step("after-spin", state => state.Count++);
        }

        var worker = new Thread(() =>
        {
            try
            {
                root = CoroutineModel.Explore("spin", new CounterState(), SpinThenStep, maxDepth: 2);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "coroutine-spin-limitation"
        };

        worker.Start();
        Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True, "the workflow body must start");
        Assert.That(
            worker.Join(TimeSpan.FromMilliseconds(250)),
            Is.False,
            "no depth bound, checkpoint bound, or timeout can interrupt a loop that reaches no checkpoint");

        Volatile.Write(ref spinning, false);

        Assert.That(worker.Join(TimeSpan.FromSeconds(10)), Is.True, "the workflow must finish once the loop exits");
        Assert.That(failure, Is.Null);
        Assert.That(((CounterState)root.Edges.Single().Target.State).Count, Is.EqualTo(1));
    }

    private static async ModelTask CountedWorkflow(ModelContext<CounterState> context)
    {
        auditedInvocations++;
        await context.Step("tick", state => state.Count++);
    }

    private static bool IsImmutableScalar(object value)
        => value == null ||
            value is string ||
            value is bool ||
            value is char ||
            value.GetType().IsPrimitive ||
            value.GetType().IsEnum ||
            value is decimal ||
            value is DateTime ||
            value is DateTimeOffset ||
            value is TimeSpan ||
            value is Guid ||
            value.GetType().Name == "ModelUnit";

    private static bool spinning;
    private static int auditedInvocations;
    private static int foreignAwaitResults;
    private static int truncatingInvocations;
    private static int shiftingInvocations;
    private static bool alternateAction;

    [Test]
    public void CapturedStateCannotBeMutatedOutsideAStep()
    {
        var initial = new CounterState();

        async ModelTask MutatingWorkflow(ModelContext<CounterState> context)
        {
            initial.Count = 42;
            await context.Step("visible", _ => { });
        }

        var previous = State.EnableFreezeValidation;
        State.EnableFreezeValidation = false;
        try
        {
            var error = Assert.Throws<ModelDefinitionException>(
                () => CoroutineModel.Explore(
                    "captured-mutation",
                    initial,
                    MutatingWorkflow));
            Assert.That(
                error.Message,
                Does.Contain("outside ModelContext.Step"));
        }
        finally
        {
            State.EnableFreezeValidation = previous;
        }
    }

    [Test]
    public void ScalarIdentityPreservesDateTimeKind()
    {
        var ticks = new DateTime(2024, 1, 1).Ticks;

        async ModelTask Dates(ModelContext<CounterState> context)
        {
            var selected = await context.Choose(
                "when",
                new[]
                {
                    new DateTime(ticks, DateTimeKind.Utc),
                    new DateTime(ticks, DateTimeKind.Local)
                });
            await context.Step(
                "record-kind",
                state => state.Count = (int)selected.Kind);
        }

        var root = CoroutineModel.Explore(
            "date-kinds",
            new CounterState(),
            Dates);

        Assert.That(root.Edges, Has.Count.EqualTo(2));
        Assert.That(
            root.Edges
                .Select(edge => edge.Target.Edges.Single().Target)
                .Select(node => ((CounterState)node.State).Count),
            Is.EquivalentTo(new[]
            {
                (int)DateTimeKind.Utc,
                (int)DateTimeKind.Local
            }));
    }

    private static async ModelTask BranchingWorkflow(ModelContext<CounterState> context)
    {
        var count = await context.Read("count", state => state.Count);
        var delta = await context.Choose("delta", state => new[] { 1, 2 });
        await context.Step("apply-delta", state => state.Count = count + delta);
    }

    private static async ModelTask LoopingWorkflow(ModelContext<CounterState> context)
    {
        while (true)
        {
            await context.Loop("iteration");
            await context.Step("toggle", state => state.Count = 1 - state.Count);
        }
    }

    private static async ModelTask PersistentLoopWorkflow(ModelContext<CounterState> context)
    {
        var phase = 0;
        while (true)
        {
            phase = await context.LoopState("iteration", phase);
            await context.Step("tick", _ => { });
            phase = (phase + 1) % 3;
        }
    }

    private static IEnumerable<StateGraphNode> Reachable(StateGraphNode root)
    {
        var seen = new HashSet<string>();
        var pending = new Stack<StateGraphNode>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node.GetNodeFingerprint()))
            {
                continue;
            }

            yield return node;
            foreach (var edge in node.Edges)
            {
                pending.Push(edge.Target);
            }
        }
    }

    private sealed class CounterState : State
    {
        public int Count { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new CounterState { Count = Count };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute)
            => $"Count={Count}";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AbstractState : State
    {
        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new AbstractState();

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute)
            => "abstract";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AbstractLoopStep : BaseStepFunction
    {
        public override string StepFunctionId => "abstract-loop";

        protected override IList<StepResult> ApplyInternal(IState state)
            => new[]
            {
                new StepResult
                {
                    State = state,
                    StepFunctions = new IStepFunction[] { this }
                }
            };
    }

    private sealed class ToggleStep : BaseStepFunction
    {
        public override string StepFunctionId => "toggle";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var next = (CounterState)state.Clone();
            next.Count = 1 - next.Count;
            return new[]
            {
                new StepResult
                {
                    State = next,
                    StepFunctions = new IStepFunction[] { this }
                }
            };
        }
    }
}
