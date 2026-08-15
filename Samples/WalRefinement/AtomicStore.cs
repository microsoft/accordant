namespace WalRefinement;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;

/// <summary>
/// The lifecycle of the one in-flight transaction. <see cref="Committed"/>
/// and <see cref="Aborted"/> mean decided but not yet reported.
/// </summary>
public enum TxnPhase { Idle, Pending, Committed, Aborted }

/// <summary>The outcome the client was told for the last transaction.</summary>
public enum Outcome { None, Committed, Aborted }

/// <summary>The five actions of the specification.</summary>
public enum StoreAction { Submit, Commit, Abort, ReportCommit, ReportAbort }

// ---------------------------------------------------------------------
// The payload both models share.
// ---------------------------------------------------------------------

/// <summary>
/// The payload of one transaction: a named <em>write set</em> with one target
/// value per key, for example <c>topup = [1, 2]</c>.
///
/// <para>The values are <b>absolute</b> targets, never deltas. That is what
/// keeps the model finite although transactions repeat forever: the committed
/// store is always one of the snapshots in <see cref="WalConfig.Snapshots"/>,
/// however long the run is.</para>
///
/// <para>A nested <c>[State]</c> class is deep-cloned, frozen and hashed with
/// the state that holds it. Treat instances as immutable: build with
/// <see cref="Of"/>, hand over with <see cref="Copy"/>, never modify.</para>
/// </summary>
[State]
public partial class WriteSet
{
    /// <summary>The name of the transaction that writes this set.</summary>
    public string Name { get; set; }

    /// <summary>The target value of each key.</summary>
    public int[] Values { get; set; }

    /// <summary>Creates a named write set with one target value per key.</summary>
    public static WriteSet Of(string name, params int[] values)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A write set needs a name.", nameof(name));
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        return new WriteSet { Name = name, Values = (int[])values.Clone() };
    }

    /// <summary>The target value of <paramref name="key"/>.</summary>
    public int this[int key] => Values[key];

    /// <summary>The number of keys this write set covers.</summary>
    public int Keys => Values.Length;

    /// <summary>A private copy of the target values.</summary>
    public int[] ToValues() => (int[])Values.Clone();

    /// <summary>An unaliased copy, for storing into another state.</summary>
    public WriteSet Copy() => (WriteSet)Clone();

    /// <summary>Whether <paramref name="values"/> is exactly this write set.</summary>
    public bool Matches(IReadOnlyList<int> values)
        => values != null && values.Count == Values.Length && Values.SequenceEqual(values);

    public override string ToString() => $"{Name}=[{string.Join(", ", Values)}]";
}

/// <summary>The size of one model instance.</summary>
public sealed class WalConfig
{
    /// <summary>The value every key starts at.</summary>
    public const int InitialValue = 0;

    /// <summary>
    /// Two keys starting at <c>[0, 0]</c>, written by the two transactions
    /// <c>topup = [1, 2]</c> and <c>swap = [2, 1]</c>. Each writes something
    /// different to each key, so "half of it is installed" — <c>[1, 0]</c> —
    /// is a state the model can name.
    /// </summary>
    public static WalConfig Default { get; } = new WalConfig(
        2,
        new[] { WriteSet.Of("topup", 1, 2), WriteSet.Of("swap", 2, 1) });

    public WalConfig(int keys, IReadOnlyList<WriteSet> transactions)
    {
        if (keys <= 0) throw new ArgumentOutOfRangeException(nameof(keys));
        if (transactions == null) throw new ArgumentNullException(nameof(transactions));

        var owned = transactions.Select(transaction =>
        {
            if (transaction == null)
                throw new ArgumentException("Write sets cannot be null.", nameof(transactions));
            if (transaction.Keys != keys)
            {
                throw new ArgumentException(
                    $"the write set {transaction} does not cover {keys} keys",
                    nameof(transactions));
            }
            var copy = transaction.Copy();
            copy.Freeze();
            return copy;
        }).ToArray();

        var duplicate = owned
            .GroupBy(transaction => transaction.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException(
                $"write-set name '{duplicate.Key}' is not unique",
                nameof(transactions));

        Keys = keys;
        Transactions = owned;
        Initial = WriteSet.Of(
            "initial",
            Enumerable.Repeat(InitialValue, keys).ToArray());
        Initial.Freeze();
        Snapshots = new[] { Initial }.Concat(owned).ToArray();
    }

    /// <summary>The number of keys every transaction writes.</summary>
    public int Keys { get; }

    /// <summary>The transactions a client may submit.</summary>
    public IReadOnlyList<WriteSet> Transactions { get; }

    /// <summary>The snapshot every key starts at: all keys at <see cref="InitialValue"/>.</summary>
    public WriteSet Initial { get; }

    /// <summary>
    /// Every whole-store snapshot that can ever be committed: the initial one
    /// plus one per transaction. Because write sets are absolute, this list is
    /// closed under repeated transactions, so "the store is never half
    /// written" is the membership statement <see cref="IsSnapshot"/>.
    /// </summary>
    public IReadOnlyList<WriteSet> Snapshots { get; }

    /// <summary>Whether <paramref name="values"/> is one whole snapshot.</summary>
    public bool IsSnapshot(IReadOnlyList<int> values)
        => Snapshots.Any(snapshot => snapshot.Matches(values));

    /// <summary>Whether <paramref name="writeSet"/> is a configured transaction.</summary>
    public bool IsTransaction(WriteSet writeSet)
        => writeSet != null &&
            Transactions.Any(transaction =>
                transaction.Name == writeSet.Name &&
                transaction.Matches(writeSet.Values));
}

// ---------------------------------------------------------------------
// The specification.
// ---------------------------------------------------------------------

/// <summary>
/// The specification state: an atomic key-value transaction store. A
/// transaction installs its <em>whole</em> write set in one indivisible step,
/// so a state holding part of one does not exist in this model at all.
/// </summary>
[State]
public partial class StoreState
{
    /// <summary>The committed value of each key.</summary>
    public int[] Values { get; set; }

    /// <summary>The phase of the in-flight transaction.</summary>
    public TxnPhase Phase { get; set; }

    /// <summary>The write set in flight, or <c>null</c> when nothing is.</summary>
    public WriteSet Request { get; set; }

    /// <summary>The outcome the client was last told.</summary>
    public Outcome LastOutcome { get; set; }
}

/// <summary>An action of the specification, tagged with the store action it is.</summary>
public sealed class StoreStep : Step<StoreState>
{
    public StoreStep(
        StoreAction action,
        Func<StoreState, bool> when,
        Action<StoreState> then,
        string subject = null)
        : base(Step.Id(action, subject, prefix: "spec"), subject, when, then)
        => Action = action;

    /// <summary>The store action this step performs.</summary>
    public StoreAction Action { get; }

    /// <summary>Declares that a WAL transition performs this store action.</summary>
    public static AbstractResponse Performs(StoreAction action, string subject = null)
        => AbstractResponse.Step(step =>
            step is StoreStep store &&
            store.Action == action &&
            (subject == null || store.Subject == subject),
            Step.Id(action, subject, prefix: "spec"));

    /// <summary>Selects store actions, for a fairness assumption.</summary>
    public static Func<IStepFunction, bool> Any(params StoreAction[] actions)
        => step => step is StoreStep store && Array.IndexOf(actions, store.Action) >= 0;
}

/// <summary>Builds the specification model.</summary>
public static class AtomicStore
{
    /// <summary>The initial store: the initial snapshot, nothing in flight.</summary>
    public static StoreState InitialState(WalConfig config)
        => new StoreState
        {
            Values = config.Initial.ToValues(),
            Phase = TxnPhase.Idle,
            Request = null,
            LastOutcome = Outcome.None
        };

    /// <summary>
    /// The actions:
    /// <c>Idle --submit--&gt; Pending --commit--&gt; Committed --report--&gt; Idle</c>,
    /// with <c>abort</c> as the other way out of <c>Pending</c>.
    /// </summary>
    public static IList<IStepFunction> Steps(WalConfig config)
    {
        var steps = new List<IStepFunction>();

        foreach (var transaction in config.Transactions)
        {
            steps.Add(new StoreStep(
                StoreAction.Submit,
                subject: transaction.Name,
                when: store => store.Phase == TxnPhase.Idle,
                then: store =>
                {
                    store.Phase = TxnPhase.Pending;
                    store.Request = transaction.Copy();
                    store.LastOutcome = Outcome.None;
                }));
        }

        // The whole write set is installed in the same indivisible step. This
        // is the atomicity the implementation has to earn.
        steps.Add(new StoreStep(
            StoreAction.Commit,
            when: store => store.Phase == TxnPhase.Pending,
            then: store =>
            {
                store.Values = store.Request.ToValues();
                store.Phase = TxnPhase.Committed;
            }));

        steps.Add(new StoreStep(
            StoreAction.Abort,
            when: store => store.Phase == TxnPhase.Pending,
            then: store => store.Phase = TxnPhase.Aborted));

        steps.Add(new StoreStep(
            StoreAction.ReportCommit,
            when: store => store.Phase == TxnPhase.Committed,
            then: store =>
            {
                store.Phase = TxnPhase.Idle;
                store.Request = null;
                store.LastOutcome = Outcome.Committed;
            }));

        steps.Add(new StoreStep(
            StoreAction.ReportAbort,
            when: store => store.Phase == TxnPhase.Aborted,
            then: store =>
            {
                store.Phase = TxnPhase.Idle;
                store.Request = null;
                store.LastOutcome = Outcome.Aborted;
            }));

        return steps;
    }

    /// <summary>Explores the specification graph.</summary>
    public static StateGraphNode Explore(WalConfig config = null, bool lazy = true)
    {
        config ??= WalConfig.Default;
        return StateGraph.ExploreStateGraph(Steps(config), InitialState(config), lazy: lazy);
    }
}
