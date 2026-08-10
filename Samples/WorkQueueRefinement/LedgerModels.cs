namespace WorkQueueRefinement;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;

/// <summary>The lifecycle of one ledger entry as a client sees it.</summary>
public enum LedgerPhase
{
    /// <summary>Accepted by the queue, not yet assigned to anybody.</summary>
    Enqueued,

    /// <summary>Assigned. The ledger has committed to an owner and a result.</summary>
    Assigned,

    /// <summary>Closed with the committed result.</summary>
    Settled,

    /// <summary>Removed from the ledger without a result.</summary>
    Purged
}

/// <summary>The result the ledger commits to when an entry is assigned.</summary>
public enum LedgerResult
{
    Unknown,
    Completed,
    Dropped,
    Cancelled
}

/// <summary>
/// The specification: a client-visible ledger. It commits at assignment time
/// to the worker that first accepted the entry and to the final result, long
/// before the implementation can know either.
/// </summary>
[State]
public partial class LedgerState
{
    /// <summary>The phase of each entry.</summary>
    public LedgerPhase[] Phases { get; set; }

    /// <summary>
    /// The worker that first accepted each entry, or
    /// <see cref="Ledger.NoOwner"/>.
    /// </summary>
    public int[] Owner { get; set; }

    /// <summary>The committed result of each entry.</summary>
    public LedgerResult[] Results { get; set; }
}

/// <summary>
/// Knobs used to build deliberately different ledgers: a ledger that is
/// missing an outcome, and the two ledgers that make the missing
/// action-mapping slice visible.
/// </summary>
public sealed class LedgerOptions
{
    /// <summary>The faithful ledger the implementation is supposed to refine.</summary>
    public static LedgerOptions Default { get; } = new LedgerOptions();

    /// <summary>The results the ledger is willing to commit to.</summary>
    public IReadOnlyList<LedgerResult> Results { get; set; } = new[]
    {
        LedgerResult.Completed,
        LedgerResult.Dropped,
        LedgerResult.Cancelled
    };

    /// <summary>
    /// Adds a second ledger action that performs the same state change as
    /// <see cref="LedgerSettleStep"/> for a cancelled entry.
    /// </summary>
    public bool IncludeCloseCancelled { get; set; }

    /// <summary>
    /// Adds a ledger action with no state footprint at all: it records that
    /// an attempt happened without changing the ledger.
    /// </summary>
    public bool IncludeRecordAttempt { get; set; }
}

/// <summary>Builds the specification model.</summary>
public static class Ledger
{
    /// <summary>An entry nobody has accepted yet.</summary>
    public const int NoOwner = -1;

    /// <summary>Creates the initial ledger: every entry enqueued and uncommitted.</summary>
    public static LedgerState InitialState(WorkQueueConfig config)
    {
        var owner = new int[config.Tasks];
        for (var task = 0; task < config.Tasks; task++)
        {
            owner[task] = NoOwner;
        }

        return new LedgerState
        {
            Phases = new LedgerPhase[config.Tasks],
            Owner = owner,
            Results = new LedgerResult[config.Tasks]
        };
    }

    /// <summary>Creates every step function of the specification model.</summary>
    public static IList<IStepFunction> Steps(
        WorkQueueConfig config,
        LedgerOptions options = null)
    {
        options ??= LedgerOptions.Default;
        var steps = new List<IStepFunction>();
        for (var task = 0; task < config.Tasks; task++)
        {
            steps.Add(new LedgerAcceptStep(task, config.Workers, options.Results));
            steps.Add(new LedgerSettleStep(task));
            steps.Add(new LedgerCancelUnassignedStep(task));
            steps.Add(new LedgerPurgeStep(task));

            if (options.IncludeCloseCancelled)
            {
                steps.Add(new LedgerCloseCancelledStep(task));
            }

            if (options.IncludeRecordAttempt)
            {
                steps.Add(new LedgerRecordAttemptStep(task));
            }
        }

        return steps;
    }

    /// <summary>Explores the specification graph on demand.</summary>
    public static StateGraphNode Explore(
        WorkQueueConfig config,
        LedgerOptions options = null)
        => StateGraph.ExploreStateGraph(
            Steps(config, options),
            InitialState(config),
            lazy: true);
}

/// <summary>Shared scaffolding for the single-successor ledger actions.</summary>
public abstract class LedgerStep : BaseStepFunction
{
    protected abstract bool IsEnabled(LedgerState ledger);

    protected abstract void Advance(LedgerState next);

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var ledger = (LedgerState)state;
        if (!IsEnabled(ledger))
        {
            return null;
        }

        var next = (LedgerState)ledger.Clone();
        Advance(next);
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

/// <summary>
/// The ledger assigns an entry, committing to an owner and a result in the
/// same instant. The choice is modeled as several results of one step
/// function, so a fairness constraint names the action "assign entry t"
/// rather than one specific committed outcome.
/// </summary>
public sealed class LedgerAcceptStep : BaseStepFunction
{
    private readonly int workers;
    private readonly IReadOnlyList<LedgerResult> results;

    public LedgerAcceptStep(
        int task,
        int workers,
        IReadOnlyList<LedgerResult> results)
    {
        Task = task;
        this.workers = workers;
        this.results = results;
    }

    public int Task { get; }

    public override string StepFunctionId => $"ledger-accept-t{Task}";

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var ledger = (LedgerState)state;
        if (ledger.Phases[Task] != LedgerPhase.Enqueued)
        {
            return null;
        }

        return (from owner in Enumerable.Range(0, workers)
                from result in results
                select Assign(ledger, owner, result))
            .ToArray();
    }

    private StepResult Assign(LedgerState ledger, int owner, LedgerResult result)
    {
        var next = (LedgerState)ledger.Clone();
        next.Phases[Task] = LedgerPhase.Assigned;
        next.Owner[Task] = owner;
        next.Results[Task] = result;
        return new StepResult
        {
            State = next,
            StepFunctions = new IStepFunction[] { this }
        };
    }
}

/// <summary>The ledger closes an assigned entry with its committed result.</summary>
public sealed class LedgerSettleStep : LedgerStep
{
    public LedgerSettleStep(int task)
    {
        Task = task;
    }

    public int Task { get; }

    public override string StepFunctionId => $"ledger-settle-t{Task}";

    protected override bool IsEnabled(LedgerState ledger)
        => ledger.Phases[Task] == LedgerPhase.Assigned;

    protected override void Advance(LedgerState next)
        => next.Phases[Task] = LedgerPhase.Settled;
}

/// <summary>The ledger cancels an entry nobody ever accepted.</summary>
public sealed class LedgerCancelUnassignedStep : LedgerStep
{
    public LedgerCancelUnassignedStep(int task)
    {
        Task = task;
    }

    public int Task { get; }

    public override string StepFunctionId => $"ledger-cancel-unassigned-t{Task}";

    protected override bool IsEnabled(LedgerState ledger)
        => ledger.Phases[Task] == LedgerPhase.Enqueued;

    protected override void Advance(LedgerState next)
    {
        next.Phases[Task] = LedgerPhase.Settled;
        next.Owner[Task] = Ledger.NoOwner;
        next.Results[Task] = LedgerResult.Cancelled;
    }
}

/// <summary>The ledger drops an assigned entry without a result.</summary>
public sealed class LedgerPurgeStep : LedgerStep
{
    public LedgerPurgeStep(int task)
    {
        Task = task;
    }

    public int Task { get; }

    public override string StepFunctionId => $"ledger-purge-t{Task}";

    protected override bool IsEnabled(LedgerState ledger)
        => ledger.Phases[Task] == LedgerPhase.Assigned;

    protected override void Advance(LedgerState next)
    {
        next.Phases[Task] = LedgerPhase.Purged;
        next.Results[Task] = LedgerResult.Unknown;
    }
}

/// <summary>
/// A second way to close a cancelled entry. It performs exactly the state
/// change <see cref="LedgerSettleStep"/> performs, so the two actions are
/// indistinguishable to a state-valued refinement mapping.
/// </summary>
public sealed class LedgerCloseCancelledStep : LedgerStep
{
    public LedgerCloseCancelledStep(int task)
    {
        Task = task;
    }

    public int Task { get; }

    public override string StepFunctionId => $"ledger-close-cancelled-t{Task}";

    protected override bool IsEnabled(LedgerState ledger)
        => ledger.Phases[Task] == LedgerPhase.Assigned &&
            ledger.Results[Task] == LedgerResult.Cancelled;

    protected override void Advance(LedgerState next)
        => next.Phases[Task] = LedgerPhase.Settled;
}

/// <summary>
/// A ledger action with no state footprint: the ledger records that an
/// attempt occurred but exposes no attempt counter, so the action is
/// indistinguishable from abstract stutter.
/// </summary>
public sealed class LedgerRecordAttemptStep : LedgerStep
{
    public LedgerRecordAttemptStep(int task)
    {
        Task = task;
    }

    public int Task { get; }

    public override string StepFunctionId => $"ledger-record-attempt-t{Task}";

    protected override bool IsEnabled(LedgerState ledger)
        => ledger.Phases[Task] == LedgerPhase.Assigned;

    protected override void Advance(LedgerState next)
    {
    }
}
