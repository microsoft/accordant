namespace DurableJobs;

using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>The worker-host lifecycle mirrored by the implementation.</summary>
public enum WorkerPhase
{
    Idle,
    Running,
    Crashed,
    Stopped
}

/// <summary>
/// A finite stand-in for the worker's response. It is volatile: a crash loses
/// it, while the durable row and lease survive until expiry.
/// </summary>
public enum AttemptOutcome
{
    None,
    Succeeded,
    Failed,
    Abandoned
}

/// <summary>The visible and internal actions of the process design.</summary>
public enum DesignAction
{
    Submit,
    Get,
    Cancel,
    EnqueueDuplicate,
    LoseDispatch,
    RebuildDispatch,
    ClaimNext,
    RecordSuccess,
    RecordFailure,
    CompleteSuccess,
    FailAttempt,
    AbandonAttempt,
    ExpireLease,
    CrashWorker,
    RestartWorker,
    LoseAcceptedJob
}

/// <summary>Two deliberately broken design switches used by counterexample tests.</summary>
public sealed class DesignOptions
{
    public static DesignOptions Correct { get; } = new();

    public bool AllowCompletionAfterCancellation { get; init; }

    public bool LoseAcceptedJob { get; init; }
}

/// <summary>The durable row owned by the service.</summary>
[State]
public partial class DurableJobRowState
{
    public string JobId { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public JobStatus Status { get; set; }

    public string? Result { get; set; }

    public string? Error { get; set; }

    public int Attempts { get; set; }
}

/// <summary>The durable claim token carried by one worker attempt.</summary>
[State]
public partial class DurableJobLeaseState
{
    public string JobId { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public int Attempt { get; set; }

    public int Token { get; set; }
}

/// <summary>
/// The process model's shared state. Coroutine continuations and failure-domain
/// ownership are scheduler configuration, not fields in this domain state.
/// </summary>
[State]
public partial class DurableJobDesignState
{
    public DurableJobRowState? Row { get; set; }

    public int QueueDepth { get; set; }

    public DurableJobLeaseState? ActiveLease { get; set; }

    public AttemptOutcome AttemptOutcome { get; set; }

    public WorkerPhase Worker { get; set; }

    public int Crashes { get; set; }

    public JobStatus Status => Row?.Status ?? JobStatus.Missing;

    public string? JobId => Row?.JobId;

    public string? Payload => Row?.Payload;

    public string? Result => Row?.Result;

    public string? Error => Row?.Error;

    public int Attempts => Row?.Attempts ?? 0;

    public int LeaseToken => ActiveLease?.Token ?? 0;

    public bool CanClaim =>
        Row is
        {
            Status: JobStatus.Pending,
            Attempts: < DurableJobFixture.MaxAttempts
        } &&
        Worker == WorkerPhase.Idle &&
        ActiveLease is null &&
        QueueDepth > 0;

    public bool CanRecordOutcome =>
        Row?.Status == JobStatus.Pending &&
        Worker == WorkerPhase.Running &&
        ActiveLease is not null &&
        AttemptOutcome == AttemptOutcome.None;

    public bool OwnsActiveLease(int token)
        => ActiveLease?.Token == token && Worker == WorkerPhase.Running;

    public void Submit()
    {
        if (Row is not null)
        {
            return;
        }

        Row = new DurableJobRowState
        {
            JobId = DurableJobFixture.JobId,
            Payload = DurableJobFixture.Payload,
            Status = JobStatus.Pending
        };
        QueueDepth = 1;
        ActiveLease = null;
        AttemptOutcome = AttemptOutcome.None;
        Worker = WorkerPhase.Idle;
        Crashes = 0;
    }

    public void Cancel(bool preserveLiveLease)
    {
        if (Row?.Status != JobStatus.Pending)
        {
            return;
        }

        Row.Status = JobStatus.Cancelled;
        Row.Result = null;
        Row.Error = null;
        QueueDepth = 0;

        if (!preserveLiveLease || Worker != WorkerPhase.Running)
        {
            ActiveLease = null;
            AttemptOutcome = AttemptOutcome.None;
            Worker = WorkerPhase.Idle;
        }
    }

    public void EnqueueDuplicate()
    {
        if (Row?.Status == JobStatus.Pending && QueueDepth < 2)
        {
            QueueDepth++;
        }
    }

    public void LoseDispatch()
    {
        if (Row is
            {
                Status: JobStatus.Pending,
                Attempts: < DurableJobFixture.MaxAttempts
            } &&
            Worker == WorkerPhase.Idle &&
            ActiveLease is null)
        {
            QueueDepth = 0;
        }
    }

    public void RebuildDispatch()
    {
        if (Row is
            {
                Status: JobStatus.Pending,
                Attempts: < DurableJobFixture.MaxAttempts
            } &&
            Worker == WorkerPhase.Idle &&
            ActiveLease is null &&
            QueueDepth == 0)
        {
            QueueDepth = 1;
        }
    }

    public void ClaimNext()
    {
        if (!CanClaim || Row is null)
        {
            return;
        }

        QueueDepth--;
        Row.Attempts++;
        ActiveLease = new DurableJobLeaseState
        {
            JobId = Row.JobId,
            Payload = Row.Payload,
            Attempt = Row.Attempts,
            Token = Row.Attempts
        };
        AttemptOutcome = AttemptOutcome.None;
        Worker = WorkerPhase.Running;
    }

    public void CompleteSuccess(int token, bool allowAfterCancellation)
    {
        if (!OwnsActiveLease(token) ||
            Row is null ||
            (Row.Status != JobStatus.Pending &&
                !(allowAfterCancellation && Row.Status == JobStatus.Cancelled)))
        {
            return;
        }

        Row.Status = JobStatus.Succeeded;
        Row.Result = DurableJobFixture.Result;
        Row.Error = null;
        ClearWork(WorkerPhase.Idle);
    }

    public void FailAttempt(int token)
    {
        if (!OwnsActiveLease(token) ||
            Row?.Status != JobStatus.Pending)
        {
            return;
        }

        if (Row.Attempts < DurableJobFixture.MaxAttempts)
        {
            ActiveLease = null;
            AttemptOutcome = AttemptOutcome.None;
            Worker = WorkerPhase.Idle;
            EnsureDispatch();
            return;
        }

        Row.Status = JobStatus.Failed;
        Row.Result = null;
        Row.Error = DurableJobFixture.Error;
        ClearWork(WorkerPhase.Idle);
    }

    public void CrashWorker()
    {
        Crashes++;
        AttemptOutcome = AttemptOutcome.None;
        Worker = WorkerPhase.Crashed;
    }

    public void ExpireLease()
    {
        if (Row?.Status != JobStatus.Pending ||
            Worker != WorkerPhase.Crashed ||
            ActiveLease is null)
        {
            return;
        }

        ActiveLease = null;
        AttemptOutcome = AttemptOutcome.None;
        Worker = WorkerPhase.Stopped;

        if (Row.Attempts == DurableJobFixture.MaxAttempts)
        {
            Row.Status = JobStatus.Failed;
            Row.Result = null;
            Row.Error = DurableJobFixture.Error;
            QueueDepth = 0;
        }
        else
        {
            EnsureDispatch();
        }
    }

    public void RestartWorker()
    {
        if (Worker == WorkerPhase.Stopped)
        {
            Worker = WorkerPhase.Idle;
        }
    }

    public void LoseAcceptedJob()
    {
        Row = null;
        QueueDepth = 0;
        ActiveLease = null;
        AttemptOutcome = AttemptOutcome.None;
        Worker = WorkerPhase.Idle;
        Crashes = 0;
    }

    private void EnsureDispatch()
    {
        if (Row?.Status == JobStatus.Pending && QueueDepth == 0)
        {
            QueueDepth = 1;
        }
    }

    private void ClearWork(WorkerPhase phase)
    {
        QueueDepth = 0;
        ActiveLease = null;
        AttemptOutcome = AttemptOutcome.None;
        Worker = phase;
    }
}

/// <summary>Stable process roles in the detailed design.</summary>
public static class DurableJobRoles
{
    public const string SubmitApi = "submit-api";
    public const string GetApi = "get-api";
    public const string CancelApi = "cancel-api";
    public const string DuplicateDispatcher = "duplicate-dispatcher";
    public const string DispatchLoss = "dispatch-loss";
    public const string DispatchRebuilder = "dispatch-rebuilder";
    public const string LeaseReaper = "lease-reaper";
    public const string Worker = "worker";
    public const string SuccessSource = "success-source";
    public const string FailureSource = "failure-source";
    public const string RowLossDefect = "row-loss-defect";
    public const string WorkerHost = "worker-host";

    public static IReadOnlyList<string> Correct { get; } =
    [
        SubmitApi,
        GetApi,
        CancelApi,
        DuplicateDispatcher,
        DispatchLoss,
        DispatchRebuilder,
        LeaseReaper,
        Worker,
        SuccessSource,
        FailureSource
    ];
}

/// <summary>
/// Builds the one canonical detailed design with real process/coroutine APIs.
/// </summary>
public static class DurableJobDesign
{
    public static DurableJobDesignState InitialState()
        => new()
        {
            Worker = WorkerPhase.Idle,
            AttemptOutcome = AttemptOutcome.None
        };

    public static ProcessSystemModel<DurableJobDesignState> Build(
        DesignOptions? options = null)
    {
        options ??= DesignOptions.Correct;
        var allowCompletionAfterCancellation =
            options.AllowCompletionAfterCancellation;

        var model = new ProcessSystemModel<DurableJobDesignState>(InitialState())
            .Process(
                DurableJobRoles.SubmitApi,
                SubmitApi)
            .Process(
                DurableJobRoles.GetApi,
                GetApi)
            .Process(
                DurableJobRoles.CancelApi,
                context => CancelApi(
                    context,
                    allowCompletionAfterCancellation))
            .Process(
                DurableJobRoles.DuplicateDispatcher,
                DuplicateDispatcher)
            .Process(
                DurableJobRoles.DispatchLoss,
                DispatchLoss)
            .Process(
                DurableJobRoles.DispatchRebuilder,
                DispatchRebuilder)
            .Process(
                DurableJobRoles.LeaseReaper,
                LeaseReaper);

        if (options.LoseAcceptedJob)
        {
            model.Process(DurableJobRoles.RowLossDefect, RowLossDefect);
        }

        var workerHost = model.FailureDomain(
            DurableJobRoles.WorkerHost,
            crashEnabled: state =>
                state.Row?.Status == JobStatus.Pending &&
                state.Worker == WorkerPhase.Running &&
                state.ActiveLease is not null &&
                state.Crashes < DurableJobFixture.MaxCrashes,
            onCrash: state => state.CrashWorker(),
            restartEnabled: state => state.Worker == WorkerPhase.Stopped,
            onRestart: state => state.RestartWorker());

        workerHost.Process(
            DurableJobRoles.Worker,
            context => Worker(
                context,
                allowCompletionAfterCancellation));
        workerHost.Process(
            DurableJobRoles.SuccessSource,
            SuccessSource);
        workerHost.Process(
            DurableJobRoles.FailureSource,
            FailureSource);

        return model;
    }

    public static StateGraphNode Explore(
        DesignOptions? options = null,
        bool lazy = false,
        int maxDepth = -1)
        => Build(options).Explore(maxDepth, lazy);

    public static ProcessTransition TransitionOf(StateGraphEdge edge)
        => (ProcessTransition)edge.Metadata;

    public static DesignAction? ActionOf(ProcessTransition transition)
    {
        if (transition.SemanticAction is DesignAction action)
        {
            return action;
        }

        return transition.Control switch
        {
            ProcessControlKind.Crash => DesignAction.CrashWorker,
            ProcessControlKind.Restart => DesignAction.RestartWorker,
            _ => null
        };
    }

    private static async ModelTask SubmitApi(
        ModelContext<DurableJobDesignState> context)
    {
        while (true)
        {
            await context.Loop("submit-loop");
            await context.Step(DesignAction.Submit, state => state.Submit());
        }
    }

    private static async ModelTask GetApi(
        ModelContext<DurableJobDesignState> context)
    {
        while (true)
        {
            await context.Loop("get-loop");
            await context.Step(DesignAction.Get, _ => { });
        }
    }

    private static async ModelTask CancelApi(
        ModelContext<DurableJobDesignState> context,
        bool preserveLiveLease)
    {
        while (true)
        {
            await context.Loop("cancel-loop");
            await context.Step(
                DesignAction.Cancel,
                state => state.Cancel(preserveLiveLease));
        }
    }

    private static async ModelTask DuplicateDispatcher(
        ModelContext<DurableJobDesignState> context)
    {
        while (true)
        {
            await context.Loop("duplicate-dispatch-loop");
            await context.StepWhen(
                DesignAction.EnqueueDuplicate,
                state =>
                    state.Row?.Status == JobStatus.Pending &&
                    state.QueueDepth < 2,
                state => state.EnqueueDuplicate());
        }
    }

    private static async ModelTask DispatchLoss(
        ModelContext<DurableJobDesignState> context)
    {
        while (true)
        {
            await context.Loop("dispatch-loss-loop");
            await context.StepWhen(
                DesignAction.LoseDispatch,
                state =>
                    state.Row is
                    {
                        Status: JobStatus.Pending,
                        Attempts: < DurableJobFixture.MaxAttempts
                    } &&
                    state.Worker == WorkerPhase.Idle &&
                    state.ActiveLease is null &&
                    state.QueueDepth > 0,
                state => state.LoseDispatch());
        }
    }

    private static async ModelTask DispatchRebuilder(
        ModelContext<DurableJobDesignState> context)
    {
        while (true)
        {
            await context.Loop("dispatch-rebuild-loop");
            await context.StepWhen(
                DesignAction.RebuildDispatch,
                state =>
                    state.Row is
                    {
                        Status: JobStatus.Pending,
                        Attempts: < DurableJobFixture.MaxAttempts
                    } &&
                    state.Worker == WorkerPhase.Idle &&
                    state.ActiveLease is null &&
                    state.QueueDepth == 0,
                state => state.RebuildDispatch());
        }
    }

    private static async ModelTask LeaseReaper(
        ModelContext<DurableJobDesignState> context)
    {
        while (true)
        {
            await context.Loop("lease-reaper-loop");
            await context.StepWhen(
                DesignAction.ExpireLease,
                state =>
                    state.Row?.Status == JobStatus.Pending &&
                    state.Worker == WorkerPhase.Crashed &&
                    state.ActiveLease is not null,
                state => state.ExpireLease());
        }
    }

    private static async ModelTask SuccessSource(
        ModelContext<DurableJobDesignState> context)
    {
        while (true)
        {
            await context.Loop("success-source-loop");
            await context.StepWhen(
                DesignAction.RecordSuccess,
                state => state.CanRecordOutcome,
                state => state.AttemptOutcome = AttemptOutcome.Succeeded);
        }
    }

    private static async ModelTask FailureSource(
        ModelContext<DurableJobDesignState> context)
    {
        while (true)
        {
            await context.Loop("failure-source-loop");
            await context.StepWhen(
                DesignAction.RecordFailure,
                state => state.CanRecordOutcome,
                state => state.AttemptOutcome = AttemptOutcome.Failed);
        }
    }

    private static async ModelTask Worker(
        ModelContext<DurableJobDesignState> context,
        bool allowCompletionAfterCancellation)
    {
        while (true)
        {
            await context.Loop("worker-loop");

            await context.StepWhen(
                DesignAction.ClaimNext,
                state => state.CanClaim,
                state => state.ClaimNext());

            var leaseToken = await context.Read(
                "lease-token",
                state => state.LeaseToken);

            var outcome = await context.WaitUntil(
                "attempt-finished",
                state =>
                    !state.OwnsActiveLease(leaseToken) ||
                    state.AttemptOutcome != AttemptOutcome.None,
                state => state.OwnsActiveLease(leaseToken)
                    ? state.AttemptOutcome
                    : AttemptOutcome.Abandoned);

            switch (outcome)
            {
                case AttemptOutcome.Succeeded:
                    await context.Step(
                        DesignAction.CompleteSuccess,
                        state => state.CompleteSuccess(
                            leaseToken,
                            allowCompletionAfterCancellation));
                    break;

                case AttemptOutcome.Failed:
                    await context.Step(
                        DesignAction.FailAttempt,
                        state => state.FailAttempt(leaseToken));
                    break;

                case AttemptOutcome.Abandoned:
                    await context.Step(
                        DesignAction.AbandonAttempt,
                        _ => { });
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unexpected attempt outcome '{outcome}'.");
            }
        }
    }

    private static async ModelTask RowLossDefect(
        ModelContext<DurableJobDesignState> context)
    {
        while (true)
        {
            await context.Loop("row-loss-loop");
            await context.StepWhen(
                DesignAction.LoseAcceptedJob,
                state =>
                    state.Row is
                    {
                        Status: JobStatus.Pending,
                        Attempts: 0
                    } &&
                    state.Worker == WorkerPhase.Idle,
                state => state.LoseAcceptedJob());
        }
    }
}
