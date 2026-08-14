// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;

/// <summary>
/// The three fixed, concurrent clients. Each one is one-shot: it submits exactly
/// one transaction and keeps the outcome it is told.
/// </summary>
public enum ClientId { Alice, Bob, Carol }

/// <summary>The outcome a client is told for its transaction.</summary>
public enum Outcome { None, Committed, Aborted }

/// <summary>
/// The phase of the single in-flight transaction. Only one transaction is ever
/// admitted at a time even though three clients contend for admission, because
/// the request slot has capacity one — it models one server with one redo record
/// and one in-flight transaction, not a single-writer key-value store.
/// <see cref="Committed"/> and <see cref="Aborted"/> mean decided but not yet
/// reported.
/// </summary>
public enum TxnPhase { Idle, Pending, Committed, Aborted }

/// <summary>The five guarded actions of the specification.</summary>
public enum StoreAction { Submit, Commit, Abort, ReportCommit, ReportAbort }

// ---------------------------------------------------------------------
// The shared communication payload both models use.
// ---------------------------------------------------------------------

/// <summary>
/// One accepted request in the capacity-one slot: which client submitted it and
/// which transaction it is. It carries only the immutable transaction name; the
/// write set is resolved from configuration, so the envelope never retains a
/// mutable reference into the payload.
/// </summary>
[State]
public partial class RequestEnvelope
{
    /// <summary>The client that submitted this request.</summary>
    public ClientId Client { get; set; }

    /// <summary>The name of the transaction the client submitted.</summary>
    public string TransactionName { get; set; }

    /// <summary>An unaliased copy, for storing into another state.</summary>
    public RequestEnvelope Copy() => (RequestEnvelope)Clone();

    /// <inheritdoc/>
    public override string ToString() => $"{Client}:{TransactionName}";
}

/// <summary>
/// The payload of one transaction: a named <em>write set</em> with one absolute
/// target value per key, for example <c>topup = [1, 2]</c>. Because the values
/// are absolute the committed store is always one of finitely many snapshots,
/// which keeps the model finite.
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

/// <summary>The size of one model instance: its keys and its per-client requests.</summary>
public sealed class WalConfig
{
    /// <summary>The value every key starts at.</summary>
    public const int InitialValue = 0;

    /// <summary>
    /// Two keys starting at <c>[0, 0]</c>, with three concurrent one-shot
    /// clients: Alice submits <c>topup = [1, 2]</c>, Bob submits
    /// <c>swap = [2, 1]</c>, and Carol submits <c>clear = [0, 0]</c>.
    /// </summary>
    public static WalConfig Default { get; } = new WalConfig(
        keys: 2,
        assignments: new[]
        {
            (ClientId.Alice, WriteSet.Of("topup", 1, 2)),
            (ClientId.Bob, WriteSet.Of("swap", 2, 1)),
            (ClientId.Carol, WriteSet.Of("clear", 0, 0)),
        });

    private readonly Dictionary<ClientId, WriteSet> byClient;

    public WalConfig(int keys, IReadOnlyList<(ClientId Client, WriteSet Request)> assignments)
    {
        if (keys <= 0) throw new ArgumentOutOfRangeException(nameof(keys));
        if (assignments == null) throw new ArgumentNullException(nameof(assignments));
        if (assignments.Count == 0)
            throw new ArgumentException("At least one client is required.", nameof(assignments));

        var owned = assignments.Select(assignment =>
        {
            var transaction = assignment.Request;
            if (transaction == null)
                throw new ArgumentException("Write sets cannot be null.", nameof(assignments));
            if (transaction.Keys != keys)
            {
                throw new ArgumentException(
                    $"the write set {transaction} does not cover {keys} keys",
                    nameof(assignments));
            }
            var copy = transaction.Copy();
            copy.Freeze();
            return (assignment.Client, Request: copy);
        }).ToArray();

        var duplicateClient = owned
            .GroupBy(assignment => assignment.Client)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateClient != null)
            throw new ArgumentException(
                $"client '{duplicateClient.Key}' is assigned more than one transaction",
                nameof(assignments));

        var duplicateName = owned
            .GroupBy(assignment => assignment.Request.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName != null)
            throw new ArgumentException(
                $"write-set name '{duplicateName.Key}' is not unique",
                nameof(assignments));

        Keys = keys;
        Clients = owned.Select(assignment => assignment.Client).ToArray();
        byClient = owned.ToDictionary(assignment => assignment.Client, assignment => assignment.Request);
        Transactions = owned.Select(assignment => assignment.Request).ToArray();
        Initial = WriteSet.Of(
            "initial",
            Enumerable.Repeat(InitialValue, keys).ToArray());
        Initial.Freeze();
        Snapshots = new[] { Initial }.Concat(Transactions).ToArray();
    }

    /// <summary>The number of keys every transaction writes.</summary>
    public int Keys { get; }

    /// <summary>The clients of this instance, in submission-slot order.</summary>
    public IReadOnlyList<ClientId> Clients { get; }

    /// <summary>The transactions a client may submit.</summary>
    public IReadOnlyList<WriteSet> Transactions { get; }

    /// <summary>The names of the transactions a client may submit.</summary>
    public IReadOnlyList<string> TransactionNames
        => Transactions.Select(transaction => transaction.Name).ToArray();

    /// <summary>The snapshot every key starts at.</summary>
    public WriteSet Initial { get; }

    /// <summary>Every whole-store snapshot that can ever be committed.</summary>
    public IReadOnlyList<WriteSet> Snapshots { get; }

    /// <summary>The one transaction the given client submits.</summary>
    public WriteSet RequestOf(ClientId client) => byClient[client];

    /// <summary>The transaction with the given name.</summary>
    public WriteSet Find(string name)
        => Transactions.First(transaction => transaction.Name == name);

    /// <summary>Whether <paramref name="values"/> is one whole snapshot.</summary>
    public bool IsSnapshot(IReadOnlyList<int> values)
        => Snapshots.Any(snapshot => snapshot.Matches(values));

    /// <summary>The number of persistent reply slots (one per possible client).</summary>
    public static int ReplyCount => Enum.GetValues(typeof(ClientId)).Length;

    /// <summary>The persistent reply-slot index of a client.</summary>
    public static int IndexOf(ClientId client) => (int)client;
}

// ---------------------------------------------------------------------
// The specification: guarded atomic actions.
// ---------------------------------------------------------------------

/// <summary>
/// The specification state: an atomic key-value transaction store with one
/// capacity-one request slot and one persistent reply per client. A transaction
/// installs its <em>whole</em> write set in one indivisible step. Three clients
/// may each submit once; a client whose reply is already set can never submit
/// again.
/// </summary>
[State]
public partial class StoreState
{
    /// <summary>The committed value of each key.</summary>
    public int[] Values { get; set; }

    /// <summary>The phase of the single in-flight transaction.</summary>
    public TxnPhase Phase { get; set; }

    /// <summary>The request in the capacity-one slot, or <c>null</c> when empty.</summary>
    public RequestEnvelope Pending { get; set; }

    /// <summary>The persistent outcome told to each client, indexed by client.</summary>
    public Outcome[] Replies { get; set; }
}

/// <summary>
/// One guarded action of the specification, declared inline: a stable id, a
/// guard, and one atomic mutation. Applying an action re-emits it, so the set
/// of actions never changes.
/// </summary>
public sealed class StoreStep : BaseStepFunction
{
    private readonly Func<StoreState, bool> when;
    private readonly Action<StoreState> then;

    public StoreStep(
        StoreAction action,
        Func<StoreState, bool> when,
        Action<StoreState> then,
        string subject = null)
    {
        Action = action;
        Subject = subject;
        StepFunctionId = Id(action, subject);
        this.when = when;
        this.then = then;
    }

    /// <summary>The store action this step performs.</summary>
    public StoreAction Action { get; }

    /// <summary>The client this action ranges over, or <c>null</c>.</summary>
    public string Subject { get; }

    /// <inheritdoc/>
    public override string StepFunctionId { get; }

    /// <summary>The stable id, for example <c>spec-submit-Alice</c>.</summary>
    public static string Id(StoreAction action, string subject = null)
    {
        var name = action.ToString();
        var id = "spec";
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                id += "-";
            }
            else if (i == 0)
            {
                id += "-";
            }

            id += char.ToLowerInvariant(name[i]);
        }

        return subject == null ? id : id + "-" + subject;
    }

    /// <summary>Declares that a WAL transition performs this store action.</summary>
    public static Microsoft.Accordant.ModelChecking.AbstractResponse Performs(
        StoreAction action, string subject = null)
        => Microsoft.Accordant.ModelChecking.AbstractResponse.Step(
            step => step is StoreStep store &&
                store.Action == action &&
                (subject == null || store.Subject == subject),
            Id(action, subject));

    /// <summary>Selects store actions, for a fairness assumption.</summary>
    public static Func<IStepFunction, bool> Any(params StoreAction[] actions)
        => step => step is StoreStep store && Array.IndexOf(actions, store.Action) >= 0;

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var source = (StoreState)state;
        if (!when(source))
        {
            return null;
        }

        var next = (StoreState)source.Clone();
        then(next);
        return new[]
        {
            new StepResult
            {
                State = next,
                StepFunctions = new IStepFunction[] { this },
                EdgeMetadata = this
            }
        };
    }

    public override string ToString() => StepFunctionId;
}

/// <summary>Builds the specification model.</summary>
public static class AtomicStore
{
    /// <summary>The initial store: the initial snapshot, nothing in flight, no replies.</summary>
    public static StoreState InitialState(WalConfig config)
        => new StoreState
        {
            Values = config.Initial.ToValues(),
            Phase = TxnPhase.Idle,
            Pending = null,
            Replies = Enumerable.Repeat(Outcome.None, WalConfig.ReplyCount).ToArray()
        };

    /// <summary>
    /// The actions:
    /// <c>Idle --submit(client)--&gt; Pending --commit--&gt; Committed --report--&gt; Idle</c>,
    /// with <c>abort</c> as the other way out of <c>Pending</c>. One Submit action
    /// per client, guarded so a client with a reply cannot submit again; the
    /// decision and report actions read the single pending request.
    /// </summary>
    public static IList<IStepFunction> Steps(WalConfig config)
    {
        var steps = new List<IStepFunction>();

        foreach (var client in config.Clients)
        {
            var owner = client;
            var transaction = config.RequestOf(owner).Name;
            steps.Add(new StoreStep(
                StoreAction.Submit,
                subject: owner.ToString(),
                when: store => store.Phase == TxnPhase.Idle &&
                    store.Replies[WalConfig.IndexOf(owner)] == Outcome.None,
                then: store =>
                {
                    store.Phase = TxnPhase.Pending;
                    store.Pending = new RequestEnvelope
                    {
                        Client = owner,
                        TransactionName = transaction
                    };
                }));
        }

        steps.Add(new StoreStep(
            StoreAction.Commit,
            when: store => store.Phase == TxnPhase.Pending,
            then: store =>
            {
                store.Values = config.Find(store.Pending.TransactionName).ToValues();
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
                store.Replies[WalConfig.IndexOf(store.Pending.Client)] = Outcome.Committed;
                store.Phase = TxnPhase.Idle;
                store.Pending = null;
            }));

        steps.Add(new StoreStep(
            StoreAction.ReportAbort,
            when: store => store.Phase == TxnPhase.Aborted,
            then: store =>
            {
                store.Replies[WalConfig.IndexOf(store.Pending.Client)] = Outcome.Aborted;
                store.Phase = TxnPhase.Idle;
                store.Pending = null;
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
