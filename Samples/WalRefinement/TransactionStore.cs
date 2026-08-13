namespace WalRefinement;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>The client-visible lifecycle of the one in-flight transaction.</summary>
public enum TxnPhase
{
    /// <summary>No transaction is in flight.</summary>
    Idle,

    /// <summary>Submitted, and its outcome is not decided yet.</summary>
    Pending,

    /// <summary>Committed. The client has not been told yet.</summary>
    Committed,

    /// <summary>Aborted. The client has not been told yet.</summary>
    Aborted
}

/// <summary>The outcome the client was told for the last transaction.</summary>
public enum Outcome
{
    /// <summary>Nothing has been reported since the last submission.</summary>
    None,

    /// <summary>The client was told the transaction committed.</summary>
    Committed,

    /// <summary>The client was told the transaction aborted.</summary>
    Aborted
}

/// <summary>The size of one model instance.</summary>
public sealed class WalConfig
{
    /// <summary>Two keys and the two values <c>0</c> and <c>1</c>.</summary>
    public static WalConfig Default { get; } = new WalConfig(2, new[] { 0, 1 });

    public WalConfig(int keys, IReadOnlyList<int> values)
    {
        Keys = keys;
        Values = values;
    }

    /// <summary>The number of keys one transaction writes.</summary>
    public int Keys { get; }

    /// <summary>The values a transaction may write.</summary>
    public IReadOnlyList<int> Values { get; }
}

/// <summary>
/// The specification: an atomic key-value transaction store. A transaction
/// writes the same value to <em>every</em> key in one indivisible step, so a
/// state where the keys disagree does not exist in this model at all.
/// </summary>
[State]
public partial class StoreState
{
    /// <summary>The committed value of each key.</summary>
    public int[] Values { get; set; }

    /// <summary>The phase of the in-flight transaction.</summary>
    public TxnPhase Phase { get; set; }

    /// <summary>
    /// The value the in-flight transaction writes, or
    /// <see cref="TransactionStore.NoValue"/> when no transaction is in flight.
    /// </summary>
    public int Request { get; set; }

    /// <summary>The outcome the client was last told.</summary>
    public Outcome LastOutcome { get; set; }
}

/// <summary>Builds the specification model.</summary>
public static class TransactionStore
{
    /// <summary>The absent value: no request, no logged payload.</summary>
    public const int NoValue = -1;

    /// <summary>The value every key starts at.</summary>
    public const int InitialValue = 0;

    /// <summary>Creates the initial store: every key initial, nothing in flight.</summary>
    public static StoreState InitialState(WalConfig config)
    {
        var values = new int[config.Keys];
        for (var key = 0; key < config.Keys; key++)
        {
            values[key] = InitialValue;
        }

        return new StoreState
        {
            Values = values,
            Phase = TxnPhase.Idle,
            Request = NoValue,
            LastOutcome = Outcome.None
        };
    }

    /// <summary>Creates every step function of the specification model.</summary>
    public static IList<IStepFunction> Steps(WalConfig config)
    {
        var steps = new List<IStepFunction>();
        foreach (var value in config.Values)
        {
            steps.Add(new SubmitTransactionStep(value));
        }

        steps.Add(new CommitTransactionStep());
        steps.Add(new AbortTransactionStep());
        steps.Add(new ReportCommitStep());
        steps.Add(new ReportAbortStep());
        return steps;
    }

    /// <summary>Explores the specification graph.</summary>
    public static StateGraphNode Explore(WalConfig config = null, bool lazy = true)
    {
        config ??= WalConfig.Default;
        return StateGraph.ExploreStateGraph(
            Steps(config),
            InitialState(config),
            lazy: lazy);
    }
}

/// <summary>
/// Shared scaffolding: guard the source state, clone it, mutate the clone,
/// and re-emit this step so the step-function set — and therefore the graph
/// node identity — stays stable.
/// </summary>
public abstract class StoreStep : BaseStepFunction
{
    protected abstract bool IsEnabled(StoreState store);

    protected abstract void Advance(StoreState next);

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var store = (StoreState)state;
        if (!IsEnabled(store))
        {
            return null;
        }

        var next = (StoreState)store.Clone();
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

/// <summary>The client submits a transaction that writes one value to every key.</summary>
public sealed class SubmitTransactionStep : StoreStep
{
    public SubmitTransactionStep(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public override string StepFunctionId => $"spec-submit-v{Value}";

    protected override bool IsEnabled(StoreState store)
        => store.Phase == TxnPhase.Idle;

    protected override void Advance(StoreState next)
    {
        next.Phase = TxnPhase.Pending;
        next.Request = Value;
        next.LastOutcome = Outcome.None;
    }
}

/// <summary>
/// The transaction commits. Every key takes the requested value in the same
/// indivisible step: this is the atomicity the implementation has to provide.
/// </summary>
public sealed class CommitTransactionStep : StoreStep
{
    public override string StepFunctionId => "spec-commit";

    protected override bool IsEnabled(StoreState store)
        => store.Phase == TxnPhase.Pending;

    protected override void Advance(StoreState next)
    {
        for (var key = 0; key < next.Values.Length; key++)
        {
            next.Values[key] = next.Request;
        }

        next.Phase = TxnPhase.Committed;
    }
}

/// <summary>The transaction aborts, leaving every key untouched.</summary>
public sealed class AbortTransactionStep : StoreStep
{
    public override string StepFunctionId => "spec-abort";

    protected override bool IsEnabled(StoreState store)
        => store.Phase == TxnPhase.Pending;

    protected override void Advance(StoreState next)
        => next.Phase = TxnPhase.Aborted;
}

/// <summary>The client is told the transaction committed.</summary>
public sealed class ReportCommitStep : StoreStep
{
    public override string StepFunctionId => "spec-report-commit";

    protected override bool IsEnabled(StoreState store)
        => store.Phase == TxnPhase.Committed;

    protected override void Advance(StoreState next)
    {
        next.Phase = TxnPhase.Idle;
        next.Request = TransactionStore.NoValue;
        next.LastOutcome = Outcome.Committed;
    }
}

/// <summary>The client is told the transaction aborted.</summary>
public sealed class ReportAbortStep : StoreStep
{
    public override string StepFunctionId => "spec-report-abort";

    protected override bool IsEnabled(StoreState store)
        => store.Phase == TxnPhase.Aborted;

    protected override void Advance(StoreState next)
    {
        next.Phase = TxnPhase.Idle;
        next.Request = TransactionStore.NoValue;
        next.LastOutcome = Outcome.Aborted;
    }
}
