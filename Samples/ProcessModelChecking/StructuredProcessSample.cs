// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace ProcessModelChecking;

using System;
using System.Collections.Generic;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>The finite lifecycle of the sample's reusable work slot.</summary>
public enum WorkPhase
{
    /// <summary>No work is present.</summary>
    Idle,

    /// <summary>A submit action published work.</summary>
    Pending,

    /// <summary>The worker claimed the work.</summary>
    Claimed,

    /// <summary>The external outcome has been recorded.</summary>
    Decided,

    /// <summary>The work completed and awaits acknowledgement.</summary>
    Completed
}

/// <summary>The finite outcomes of one external execution attempt.</summary>
public enum WorkOutcome
{
    /// <summary>No outcome has been selected.</summary>
    None,

    /// <summary>The attempt asks the worker to retry.</summary>
    Retry,

    /// <summary>The attempt succeeds.</summary>
    Success
}

/// <summary>Typed semantic actions carried by <see cref="ProcessTransition"/>.</summary>
public enum WorkAction
{
    /// <summary>Publish work into the reusable slot.</summary>
    Submit,

    /// <summary>Atomically claim pending work.</summary>
    Claim,

    /// <summary>Select and record an external outcome.</summary>
    Execute,

    /// <summary>Apply the selected outcome.</summary>
    Settle,

    /// <summary>Acknowledge completed work and free the slot.</summary>
    Acknowledge
}

/// <summary>The sample's complete domain state.</summary>
public sealed class WorkState : State
{
    /// <summary>The reusable slot's current phase.</summary>
    public WorkPhase Phase { get; set; }

    /// <summary>The most recently selected attempt outcome.</summary>
    public WorkOutcome Outcome { get; set; }

    protected override void CloneInternal(Dictionary<object, object> clonedMap)
        => clonedMap[this] = new WorkState
        {
            Phase = Phase,
            Outcome = Outcome
        };

    protected override string StringRepresentationInternal(
        Dictionary<object, string> objectPaths,
        string path,
        bool forceRecompute)
        => $"Phase={Phase},Outcome={Outcome}";

    protected override void FreezeComponents(HashSet<object> visited)
    {
    }
}

/// <summary>
/// A compact structured-process model demonstrating recurring stateless
/// actions, structured iteration, a checkpoint-bearing call, and atomic
/// nondeterministic execution.
/// </summary>
public static class StructuredProcessSample
{
    /// <summary>The stable role of the sequential worker.</summary>
    public const string WorkerRole = "worker";

    /// <summary>The stable role of the recurring submit action.</summary>
    public const string SubmitRole = "submitter";

    /// <summary>The stable role of the recurring acknowledgement action.</summary>
    public const string AcknowledgeRole = "acknowledger";

    /// <summary>Builds the reusable work-slot process system.</summary>
    public static ProcessSystemModel<WorkState> Build()
        => new ProcessSystemModel<WorkState>(new WorkState())
            .RepeatedAction(
                SubmitRole,
                state => state.Phase == WorkPhase.Idle,
                WorkAction.Submit,
                state => state.Phase = WorkPhase.Pending,
                subject: "job")
            .Process(
                WorkerRole,
                context => context.Forever(
                    "attempt-loop",
                    WorkerIteration))
            .RepeatedAction(
                AcknowledgeRole,
                state => state.Phase == WorkPhase.Completed,
                WorkAction.Acknowledge,
                state =>
                {
                    state.Phase = WorkPhase.Idle;
                    state.Outcome = WorkOutcome.None;
                },
                subject: "job");

    /// <summary>Explores the complete finite graph.</summary>
    public static StateGraphNode Explore() => Build().Explore();

    private static async ModelTask WorkerIteration(ModelContext<WorkState> context)
    {
        await context.StepWhen(
            WorkAction.Claim,
            state => state.Phase == WorkPhase.Pending,
            state =>
            {
                state.Phase = WorkPhase.Claimed;
                state.Outcome = WorkOutcome.None;
            },
            subject: "job");

        var outcome = await context.Call("execute-attempt", ExecuteAttempt);

        await context.Step(
            WorkAction.Settle,
            state =>
            {
                if (outcome == WorkOutcome.Success)
                {
                    state.Phase = WorkPhase.Completed;
                }
                else
                {
                    state.Phase = WorkPhase.Pending;
                    state.Outcome = WorkOutcome.None;
                }
            },
            subject: "job");
    }

    private static async ModelTask<WorkOutcome> ExecuteAttempt(
        ModelContext<WorkState> context)
    {
        var outcome = await context.ChooseStep(
            "execution-outcome",
            WorkAction.Execute,
            new[] { WorkOutcome.Retry, WorkOutcome.Success },
            (state, selected) =>
            {
                state.Phase = WorkPhase.Decided;
                state.Outcome = selected;
            },
            subject: _ => "job");
        return outcome;
    }
}
