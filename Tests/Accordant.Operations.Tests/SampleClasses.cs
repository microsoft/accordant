// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Operations.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Accordant;

#region SimpleStatefulClass Spec and Operations

public class SimpleStatefulClassSpec : Spec<CounterState>
{
    public SimpleStatefulClassAddOperation AddOp { get; } = new();
    public SimpleStatefulClassCountOperation Count { get; } = new();
    public SimpleStatefulClassFaultWrappingAddOperation FaultWrappingAdd { get; } = new();
    public SimpleStatefulClassFaultWrappingCountOperation FaultWrappingCount { get; } = new();

    public SimpleStatefulClassSpec()
    {
        this["Add"] = AddOp;
        this["Count"] = Count;
        this["Fault Wrapping Add"] = FaultWrappingAdd;
        this["Fault Wrapping Count"] = FaultWrappingCount;
    }
}

public class SimpleStatefulClassAddOperation : Operation<int, int, CounterState>
{
    public SimpleStatefulClassAddOperation() : base("Add") { }

    public override ExpectedOutcomes Apply(int request, CounterState state)
    {
        var expectedValue = request + state.Value;
        return Expect.That(r => r == expectedValue, $"should equal {expectedValue}")
                     .WithNextState(new CounterState(state.Value + request));
    }

    public override int Execute(TestingContext context, int request)
    {
        return context.Get<SimpleStatefulClass>().Add(request);
    }
}

public class SimpleStatefulClassCountOperation : Operation<Unit, int, CounterState>
{
    public SimpleStatefulClassCountOperation() : base("Count") { }

    public override ExpectedOutcomes Apply(Unit request, CounterState state)
    {
        return Expect.That(r => r == state.Value, $"should equal {state.Value}")
                     .SameState();
    }

    public override int Execute(TestingContext context, Unit request)
    {
        return context.Get<SimpleStatefulClass>().GetCount();
    }
}

public class SimpleStatefulClassFaultWrappingAddOperation : Operation<int, int?, CounterState>
{
    public SimpleStatefulClassFaultWrappingAddOperation() : base("Fault Wrapping Add") { }

    public override ExpectedOutcomes Apply(int request, CounterState state)
    {
        var expectedValue = request + state.Value;

        // Non-deterministic: either success or fault
        return Expect.OneOf(
            // Normal success outcome
            Expect.That(r => r == expectedValue, $"success: should equal {expectedValue}")
                  .WithNextState(new CounterState(state.Value + request)),
            // Exception/fault outcome - returns null, state unchanged
            Expect.That(r => r == null, "fault: should be null")
                  .SameState());
    }

    public override async Task<int?> ExecuteAsync(TestingContext context, int request)
    {
        try
        {
            var instance = context.Get<SimpleStatefulClass>();
            return await Task.FromResult(instance.Add(request));
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public class SimpleStatefulClassFaultWrappingCountOperation : Operation<Unit, int?, CounterState>
{
    public SimpleStatefulClassFaultWrappingCountOperation() : base("Fault Wrapping Count") { }

    public override ExpectedOutcomes Apply(Unit request, CounterState state)
    {
        // Non-deterministic: either success or fault
        return Expect.OneOf(
            // Normal success outcome
            Expect.That(r => r == state.Value, $"success: should equal {state.Value}")
                  .SameState(),
            // Exception/fault outcome - returns null, state unchanged
            Expect.That(r => r == null, "fault: should be null")
                  .SameState());
    }

    public override async Task<int?> ExecuteAsync(TestingContext context, Unit request)
    {
        try
        {
            var instance = context.Get<SimpleStatefulClass>();
            return await Task.FromResult(instance.GetCount());
        }
        catch (Exception)
        {
            return null;
        }
    }
}

#endregion

#region SimpleAsyncClass Spec and Operations

public class SimpleAsyncClassSpec : Spec<CounterState>
{
    public TriggerAsyncFirstStageOperation TriggerAsyncFirstStage { get; } = new();
    public TriggerSyncSecondStageOperation TriggerSyncSecondStage { get; } = new();
    public TriggerSyncThirdStageOperation TriggerSyncThirdStage { get; } = new();
    public GetStageOperation GetStage { get; } = new();

    public SimpleAsyncClassSpec()
    {
        this["TriggerAsyncFirstStage"] = TriggerAsyncFirstStage;
        this["TriggerSyncSecondStage"] = TriggerSyncSecondStage;
        this["TriggerSyncThirdStage"] = TriggerSyncThirdStage;
        this["GetStage"] = GetStage;
    }
}

#endregion

//
// Simple Stateful Class
//

public class SimpleStatefulClass
{
    private bool simulateBuggyBehavior;
    private bool throwExceptionsOnAdd;
    private bool throwExceptionsOnGet;

    private int maxAddExceptions;
    private int maxGetExceptions;

    private int addExceptionCount = 0;
    private int getExceptionCount = 0;

    private int Count { get; set; } = 0;

    public SimpleStatefulClass(
        bool simulateBuggyBehavior = false,
        bool throwExceptionsOnAdd = false,
        bool throwExceptionsOnGet = false,
        int maxAddExceptions = -1,
        int maxGetExceptions = -1)
    {
        this.simulateBuggyBehavior = simulateBuggyBehavior;
        this.throwExceptionsOnAdd = throwExceptionsOnAdd;
        this.throwExceptionsOnGet = throwExceptionsOnGet;
        this.maxAddExceptions = maxAddExceptions;
        this.maxGetExceptions = maxGetExceptions;
    }

    public int Add(int value)
    {
        if (throwExceptionsOnAdd)
        {
            if (maxAddExceptions == -1 ||
                addExceptionCount < maxAddExceptions)
            {
                addExceptionCount++;

                throw new System.Exception();
            }
        }

        lock (this)
        {
            if (simulateBuggyBehavior)
            {
                Count += (value - 1);
            }
            else
            {
                Count += value;
            }

            return Count;
        }
    }

    public int GetCount()
    {
        if (throwExceptionsOnGet)
        {
            if (maxGetExceptions == -1 ||
                getExceptionCount < maxGetExceptions)
            {
                getExceptionCount++;

                throw new System.Exception();
            }
        }

        lock (this)
        {
            return Count;
        }
    }
}

//
// Simple Async Class
//

public class SimpleAsyncClass
{
    private int stage = 0;

    public void TriggerAsyncFirstStage()
    {
        if (stage != 0)
        {
            return;
        }

        stage = 1;

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);

            stage = 2;
        });
    }

    public bool TriggerSyncSecondStage()
    {
        if (stage < 2)
        {
            return false;
        }

        stage = 3;

        return true;
    }

    public bool TriggerSyncThirdStage()
    {
        if (stage < 3)
        {
            return false;
        }

        stage = 4;

        return true;
    }

    public int GetStage()
    {
        return stage;
    }
}

public class TriggerAsyncFirstStageOperation : Operation<Unit, Unit, CounterState>
{
    public TriggerAsyncFirstStageOperation() : base("TriggerAsyncFirstStage") { }

    public override ExpectedOutcomes Apply(Unit request, CounterState state)
    {
        if (state.Value != 0)
        {
            return Expect.Unit("already triggered")
                         .SameState();
        }

        return Expect.Unit("trigger async work")
                     .WithNextState(new CounterState(1))
                     .Triggers(new SimpleAsyncClassStepFunction());
    }

    public override Unit Execute(TestingContext context, Unit request)
    {
        context.Get<SimpleAsyncClass>().TriggerAsyncFirstStage();
        return Unit.Value;
    }
}

public class TriggerSyncSecondStageOperation : Operation<Unit, bool, CounterState>
{
    public TriggerSyncSecondStageOperation() : base("TriggerSyncSecondStage") { }

    public override ExpectedOutcomes Apply(Unit request, CounterState state)
    {
        if (state.Value != 2)
        {
            return Expect.That(b => b == false, "not ready for second stage")
                         .SameState();
        }

        return Expect.That(b => b == true, "second stage triggered")
                     .WithNextState(new CounterState(3));
    }

    public override bool Execute(TestingContext context, Unit request)
    {
        return context.Get<SimpleAsyncClass>().TriggerSyncSecondStage();
    }
}

public class TriggerSyncThirdStageOperation : Operation<Unit, bool, CounterState>
{
    public TriggerSyncThirdStageOperation() : base("TriggerSyncThirdStage") { }

    public override ExpectedOutcomes Apply(Unit request, CounterState state)
    {
        if (state.Value != 3)
        {
            return Expect.That(b => b == false, "not ready for third stage")
                         .SameState();
        }

        return Expect.That(b => b == true, "third stage triggered")
                     .WithNextState(new CounterState(4));
    }

    public override bool Execute(TestingContext context, Unit request)
    {
        return context.Get<SimpleAsyncClass>().TriggerSyncThirdStage();
    }
}

public class GetStageOperation : Operation<Unit, int, CounterState>
{
    public GetStageOperation() : base("GetStage") { }

    public override ExpectedOutcomes Apply(Unit request, CounterState state)
    {
        return Expect.That(r => r == state.Value, $"should equal current stage {state.Value}")
                     .SameState();
    }

    public override int Execute(TestingContext context, Unit request)
    {
        return context.Get<SimpleAsyncClass>().GetStage();
    }
}

public class SimpleAsyncClassStepFunction : TerminatingStepFunction
{
    /// <summary>
    /// Terminal when state value is 2 (async work completed).
    /// </summary>
    public override Func<IState, bool> IsTerminalState => state =>
    {
        var counterState = (CounterState)state;
        return counterState.Value == 2;
    };

    /// <summary>
    /// Transition from value 1 to value 2.
    /// Only called when IsTerminalState is false.
    /// </summary>
    protected override IList<StepResult> GetStepResults(IState state)
    {
        var nextState = (CounterState)state.Clone();
        if (nextState.Value == 1)
        {
            nextState.Value = 2;
        }
        return new[] { new StepResult { State = nextState } };
    }
}

#region TerminalStepFunctionPolling Bug Repro

/// <summary>
/// State for the async operation scenario.
/// Models a resource with a Status that transitions from "pending" to "success".
/// </summary>
[State]
public partial class AsyncOperationState : State
{
    public string Status { get; set; } = "none";
}

/// <summary>
/// Spec for reproducing the bug where a terminated step function
/// causes polling to be attempted on subsequent operations that
/// don't have polling configured.
/// 
/// Models: StartAsync -> GetStatus (polls until success) -> Unrelated operation (no polling)
/// </summary>
public class AsyncOperationSpec : Spec<AsyncOperationState>
{
    public StartAsyncOperation StartAsync { get; } = new();
    public GetStatusOperation GetStatus { get; } = new();
    public UnrelatedOperation Unrelated { get; } = new();

    public AsyncOperationSpec()
    {
        this["StartAsync"] = StartAsync;
        this["GetStatus"] = GetStatus;
        this["Unrelated"] = Unrelated;
    }
}

/// <summary>
/// StartAsync operation - triggers async work, returns while work is still pending.
/// Has polling configured to poll via GetStatus.
/// </summary>
public class StartAsyncOperation : Operation<Unit, string, AsyncOperationState>
{
    public StartAsyncOperation() : base("StartAsync") { }

    public override PollingSetup Polling => new PollingSetup
    {
        Operation = "GetStatus",
        WaitTimeInMs = 10,
        MaxRetryCount = 100
    };

    public override ExpectedOutcomes Apply(Unit request, AsyncOperationState state)
    {
        if (state.Status != "none")
        {
            return Expect.That(r => r == "already started", "already started").SameState();
        }

        // Work starts in "pending" state and triggers a step function
        // that will eventually transition to "success"
        return Expect.That(r => r == "pending", "work started, status is pending")
                     .WithNextState(new AsyncOperationState { Status = "pending" })
                     .Triggers(new AsyncWorkStepFunction());
    }

    public override string Execute(TestingContext context, Unit request)
    {
        var target = context.Get<AsyncWorkTarget>();
        return target.StartWork();
    }
}

/// <summary>
/// GetStatus - observes the current status from the server.
/// Uses state-aware validation: asserts response matches model state.
/// The step function is responsible for transitioning state to "success".
/// </summary>
public class GetStatusOperation : Operation<Unit, string, AsyncOperationState>
{
    public GetStatusOperation() : base("GetStatus") { }

    /// <summary>
    /// Derivation for polling: GetStatus derives from StartAsync.
    /// </summary>
    public override IReadOnlyList<RequestDerivation> DerivedFrom => new[]
    {
        Derive.From<Unit, string, Unit>("StartAsync")
              .As((req, resp) => Unit.Value)
    };

    public override ExpectedOutcomes Apply(Unit request, AsyncOperationState state)
    {
        // State-aware validation: assert response matches our model state.
        // The step function is responsible for transitioning state to "success".
        return Expect.That(r => r == state.Status, $"should return '{state.Status}'")
                     .SameState();
    }

    public override string Execute(TestingContext context, Unit request)
    {
        return context.Get<AsyncWorkTarget>().GetStatus();
    }
}

/// <summary>
/// Unrelated operation - does NOT have polling configured.
/// </summary>
public class UnrelatedOperation : Operation<Unit, string, AsyncOperationState>
{
    public UnrelatedOperation() : base("Unrelated") { }

    // NOTE: No Polling property - this operation has nothing to do with async work.

    public override ExpectedOutcomes Apply(Unit request, AsyncOperationState state)
    {
        return Expect.That(r => r == "ok", "unrelated operation completed")
                     .SameState();
    }

    public override string Execute(TestingContext context, Unit request)
    {
        return context.Get<AsyncWorkTarget>().DoUnrelatedWork();
    }
}

/// <summary>
/// Step function that models the async work completing.
/// Terminal when Status is no longer "pending" (i.e., "success" or "failed").
/// </summary>
public class AsyncWorkStepFunction : TerminatingStepFunction
{
    public override Func<IState, bool> IsTerminalState => state =>
    {
        var asyncState = (AsyncOperationState)state;
        // Terminal when work is no longer pending
        return asyncState.Status != "pending";
    };

    protected override IList<StepResult> GetStepResults(IState state)
    {
        // Transition from pending to success
        var nextState = (AsyncOperationState)state.Clone();
        nextState.Status = "success";
        return new[] { new StepResult { State = nextState } };
    }
}

/// <summary>
/// Target class that simulates async work.
/// </summary>
public class AsyncWorkTarget
{
    private string status = "none";

    public string StartWork()
    {
        if (status == "none")
        {
            status = "pending";
            // Simulate async work completing quickly
            _ = Task.Run(async () =>
            {
                await Task.Delay(5);
                status = "success";
            });
        }
        return status;
    }

    public string GetStatus() => status;

    public string DoUnrelatedWork() => "ok";
}

#endregion

#region TriggersWhen Demo - CopyBlob-like Scenario

/// <summary>
/// State for the copy operation scenario.
/// Models a resource that can complete immediately ("success") or async ("pending").
/// </summary>
[State]
public partial class CopyState : State
{
    public string CopyStatus { get; set; } = "none";
}

/// <summary>
/// Spec demonstrating TriggersWhen for operations that may or may not trigger
/// async work depending on the response.
/// 
/// Like Azure Blob Copy: operation can return "success" (copy completed immediately)
/// or "pending" (copy is async, need to poll).
/// </summary>
public class CopyOperationSpec : Spec<CopyState>
{
    public StartCopyOperation StartCopy { get; } = new();
    public GetCopyStatusOperation GetCopyStatus { get; } = new();
    public UnrelatedCopyOperation UnrelatedCopy { get; } = new();

    public CopyOperationSpec()
    {
        this["StartCopy"] = StartCopy;
        this["GetCopyStatus"] = GetCopyStatus;
        this["UnrelatedCopy"] = UnrelatedCopy;
    }
}

/// <summary>
/// StartCopy - models an operation that can complete immediately or async.
/// Uses TriggersWhen to only trigger the step function when response is "pending".
/// </summary>
public class StartCopyOperation : Operation<Unit, string, CopyState>
{
    public StartCopyOperation() : base("StartCopy") { }

    public override PollingSetup Polling => new PollingSetup
    {
        Operation = "GetCopyStatus",
        WaitTimeInMs = 10,
        MaxRetryCount = 100
    };

    public override ExpectedOutcomes Apply(Unit request, CopyState state)
    {
        if (state.CopyStatus != "none")
        {
            return Expect.That(r => r == "already started", "already started").SameState();
        }

        // Response can be either "pending" (async) or "success" (immediate completion)
        // Only trigger step function when pending!
        return Expect.That(r => r == "pending" || r == "success", "copy started")
                     .ThenState(
                         (string response, CopyState nextState) =>
                         {
                             nextState.CopyStatus = response;
                         },
                         mock: () => "success")  // Mock returns success for exploration
                     .TriggersWhen(
                         response => response == "pending",
                         new CopyStepFunction());
    }

    public override string Execute(TestingContext context, Unit request)
    {
        return context.Get<CopyTarget>().StartCopy();
    }
}

/// <summary>
/// GetCopyStatus - polls for copy completion using response-dependent state.
/// </summary>
public class GetCopyStatusOperation : Operation<Unit, string, CopyState>
{
    public GetCopyStatusOperation() : base("GetCopyStatus") { }

    public override IReadOnlyList<RequestDerivation> DerivedFrom => new[]
    {
        Derive.From<Unit, string, Unit>("StartCopy")
              .As((req, resp) => Unit.Value)
    };

    public override ExpectedOutcomes Apply(Unit request, CopyState state)
    {
        // Response-dependent state: accept pending or success, update model
        return Expect.That(r => r == "pending" || r == "success", "should return copy status")
                     .ThenState(
                         (string response, CopyState nextState) =>
                         {
                             nextState.CopyStatus = response;
                         },
                         mock: () => "success");
    }

    public override string Execute(TestingContext context, Unit request)
    {
        return context.Get<CopyTarget>().GetCopyStatus();
    }
}

/// <summary>
/// Unrelated operation - no polling configured.
/// </summary>
public class UnrelatedCopyOperation : Operation<Unit, string, CopyState>
{
    public UnrelatedCopyOperation() : base("UnrelatedCopy") { }

    public override ExpectedOutcomes Apply(Unit request, CopyState state)
    {
        return Expect.That(r => r == "ok", "unrelated completed").SameState();
    }

    public override string Execute(TestingContext context, Unit request)
    {
        return context.Get<CopyTarget>().DoUnrelated();
    }
}

/// <summary>
/// Step function for copy completion - terminal when CopyStatus != "pending".
/// </summary>
public class CopyStepFunction : TerminatingStepFunction
{
    public override Func<IState, bool> IsTerminalState => state =>
    {
        var copyState = (CopyState)state;
        return copyState.CopyStatus != "pending";
    };

    protected override IList<StepResult> GetStepResults(IState state)
    {
        var nextState = (CopyState)state.Clone();
        nextState.CopyStatus = "success";
        return new[] { new StepResult { State = nextState } };
    }
}

/// <summary>
/// Target that simulates copy operation behavior.
/// </summary>
public class CopyTarget
{
    private string copyStatus = "none";
    private readonly bool immediateSuccess;

    /// <summary>
    /// Creates a copy target.
    /// </summary>
    /// <param name="immediateSuccess">If true, StartCopy returns "success" immediately.
    /// If false, returns "pending" and completes asynchronously.</param>
    public CopyTarget(bool immediateSuccess = true)
    {
        this.immediateSuccess = immediateSuccess;
    }

    public string StartCopy()
    {
        if (copyStatus == "none")
        {
            if (immediateSuccess)
            {
                copyStatus = "success";
            }
            else
            {
                copyStatus = "pending";
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5);
                    copyStatus = "success";
                });
            }
        }
        return copyStatus;
    }

    public string GetCopyStatus() => copyStatus;

    public string DoUnrelated() => "ok";
}

#endregion

#region DualAsync Spec - Two Independent Async Operations

/// <summary>
/// State for two independent async jobs (A and B).
/// </summary>
[State]
public partial class DualAsyncState : State
{
    public string JobAStatus { get; set; } = "none";
    public string JobBStatus { get; set; } = "none";
}

/// <summary>
/// Spec with two independent async operations (StartJobA, StartJobB),
/// each with its own polling operation (PollJobA, PollJobB).
/// Designed to test concurrent polling after a concurrent segment.
/// </summary>
public class DualAsyncSpec : Spec<DualAsyncState>
{
    public StartJobAOperation StartJobA { get; } = new();
    public PollJobAOperation PollJobA { get; } = new();
    public StartJobBOperation StartJobB { get; } = new();
    public PollJobBOperation PollJobB { get; } = new();

    public DualAsyncSpec()
    {
        this["StartJobA"] = StartJobA;
        this["PollJobA"] = PollJobA;
        this["StartJobB"] = StartJobB;
        this["PollJobB"] = PollJobB;
    }
}

public class JobAStepFunction : TerminatingStepFunction
{
    public override Func<IState, bool> IsTerminalState => state =>
    {
        var s = (DualAsyncState)state;
        return s.JobAStatus != "pending";
    };

    protected override IList<StepResult> GetStepResults(IState state)
    {
        var next = (DualAsyncState)state.Clone();
        next.JobAStatus = "done";
        return new[] { new StepResult { State = next } };
    }
}

public class JobBStepFunction : TerminatingStepFunction
{
    public override Func<IState, bool> IsTerminalState => state =>
    {
        var s = (DualAsyncState)state;
        return s.JobBStatus != "pending";
    };

    protected override IList<StepResult> GetStepResults(IState state)
    {
        var next = (DualAsyncState)state.Clone();
        next.JobBStatus = "done";
        return new[] { new StepResult { State = next } };
    }
}

public class StartJobAOperation : Operation<Unit, string, DualAsyncState>
{
    public StartJobAOperation() : base("StartJobA") { }

    public override PollingSetup Polling => new PollingSetup
    {
        Operation = "PollJobA",
        WaitTimeInMs = 10,
        MaxRetryCount = 100
    };

    public override ExpectedOutcomes Apply(Unit request, DualAsyncState state)
    {
        if (state.JobAStatus != "none")
            return Expect.That(r => r == "already started", "already started").SameState();

        return Expect.That(r => r == "pending", "job A started")
                     .WithNextState(new DualAsyncState { JobAStatus = "pending", JobBStatus = state.JobBStatus })
                     .Triggers(new JobAStepFunction());
    }

    public override string Execute(TestingContext context, Unit request)
    {
        return context.Get<DualAsyncTarget>().StartJobA();
    }
}

public class PollJobAOperation : Operation<Unit, string, DualAsyncState>
{
    public PollJobAOperation() : base("PollJobA") { }

    public override IReadOnlyList<RequestDerivation> DerivedFrom => new[]
    {
        Derive.From<Unit, string, Unit>("StartJobA")
              .As((req, resp) => Unit.Value)
    };

    public override ExpectedOutcomes Apply(Unit request, DualAsyncState state)
    {
        return Expect.That(r => r == state.JobAStatus, $"should return '{state.JobAStatus}'")
                     .SameState();
    }

    public override string Execute(TestingContext context, Unit request)
    {
        return context.Get<DualAsyncTarget>().GetJobAStatus();
    }
}

public class StartJobBOperation : Operation<Unit, string, DualAsyncState>
{
    public StartJobBOperation() : base("StartJobB") { }

    public override PollingSetup Polling => new PollingSetup
    {
        Operation = "PollJobB",
        WaitTimeInMs = 10,
        MaxRetryCount = 100
    };

    public override ExpectedOutcomes Apply(Unit request, DualAsyncState state)
    {
        if (state.JobBStatus != "none")
            return Expect.That(r => r == "already started", "already started").SameState();

        return Expect.That(r => r == "pending", "job B started")
                     .WithNextState(new DualAsyncState { JobAStatus = state.JobAStatus, JobBStatus = "pending" })
                     .Triggers(new JobBStepFunction());
    }

    public override string Execute(TestingContext context, Unit request)
    {
        return context.Get<DualAsyncTarget>().StartJobB();
    }
}

public class PollJobBOperation : Operation<Unit, string, DualAsyncState>
{
    public PollJobBOperation() : base("PollJobB") { }

    public override IReadOnlyList<RequestDerivation> DerivedFrom => new[]
    {
        Derive.From<Unit, string, Unit>("StartJobB")
              .As((req, resp) => Unit.Value)
    };

    public override ExpectedOutcomes Apply(Unit request, DualAsyncState state)
    {
        return Expect.That(r => r == state.JobBStatus, $"should return '{state.JobBStatus}'")
                     .SameState();
    }

    public override string Execute(TestingContext context, Unit request)
    {
        return context.Get<DualAsyncTarget>().GetJobBStatus();
    }
}

/// <summary>
/// Target with two independent async jobs.
/// </summary>
public class DualAsyncTarget
{
    private string jobAStatus = "none";
    private string jobBStatus = "none";

    public string StartJobA()
    {
        if (jobAStatus == "none")
        {
            jobAStatus = "pending";
            _ = Task.Run(async () =>
            {
                await Task.Delay(5);
                jobAStatus = "done";
            });
        }
        return jobAStatus;
    }

    public string StartJobB()
    {
        if (jobBStatus == "none")
        {
            jobBStatus = "pending";
            _ = Task.Run(async () =>
            {
                await Task.Delay(5);
                jobBStatus = "done";
            });
        }
        return jobBStatus;
    }

    public string GetJobAStatus() => jobAStatus;
    public string GetJobBStatus() => jobBStatus;
}

#endregion
