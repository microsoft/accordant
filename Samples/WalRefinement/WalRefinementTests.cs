namespace WalRefinement;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Safety refinement of the write-ahead log against the atomic transaction
/// store: the protocol invariants, what the abstraction hides, the four broken
/// implementations, and the broken mapping.
/// </summary>
[TestFixture]
public class WalRefinementTests
{
    [Test]
    public void TransactionNamesMustBeUniqueBecauseTheyNameActions()
    {
        Assert.That(
            () => new WalConfig(
                2,
                new[]
                {
                    WriteSet.Of("transfer", 1, 2),
                    WriteSet.Of("transfer", 2, 1)
                }),
            Throws.ArgumentException.With.Message.Contains(
                "write-set name 'transfer' is not unique"));
    }

    // ---------------------------------------------------------------
    // The correct control.
    // ---------------------------------------------------------------

    [Test]
    public void TheWriteAheadLogRefinesTheAtomicStore()
    {
        // Declarations only narrow, so the declared check is the stronger
        // claim: every implementation action performs the store action the
        // model names it, and every other one leaves the store where it is.
        Assert.That(
            StoreRefinement.Build().Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            StoreRefinement.StateOnly().Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    // ---------------------------------------------------------------
    // Invariants of the two graphs.
    // ---------------------------------------------------------------

    [Test]
    public void TheImplementationHoldsTheProtocolInvariants()
    {
        var config = WalConfig.Default;
        var states = ModelGraph
            .Nodes(WriteAheadLog.Explore(config))
            .Select(node => (WalState)node.State)
            .ToArray();

        foreach (var wal in states)
        {
            Assert.That(
                !wal.LogCommit || wal.LogRedo,
                "write-ahead ordering: no commit record without its redo record");
            Assert.That(
                wal.LogRedo == (wal.LogRecord != null),
                "the logged write set is canonical, so no two states differ by a dead field");
            Assert.That(
                wal.LogRecord == null || config.IsTransaction(wal.LogRecord),
                $"the log only ever holds a whole configured write set — was {wal.LogRecord}");
            Assert.That(
                wal.LogCommit || config.IsSnapshot(wal.Data),
                "no-steal: while no commit record is durable the data pages are still a " +
                    $"whole snapshot — were [{string.Join(", ", wal.Data)}]");
            Assert.That(
                config.IsSnapshot(WriteAheadLog.RecoveredSnapshot(wal)),
                "the store recovery would install is always one whole snapshot, never a " +
                    $"mixture — was [{string.Join(", ", WriteAheadLog.RecoveredSnapshot(wal))}]");
            Assert.That(
                !wal.LogCommit ||
                    wal.Server is ServerPhase.Committed or ServerPhase.Down or ServerPhase.Recovering,
                "a durable commit record is only ever forgotten by truncation");
            Assert.That(
                wal.Server != ServerPhase.Active || wal.Client == ClientPhase.Waiting,
                "the server only holds a write while a client is waiting for it");
            Assert.That(
                (wal.Client == ClientPhase.Waiting) == (wal.Request != null),
                "the outstanding request is canonical");
        }

        Assert.That(
            states,
            Has.Some.Matches<WalState>(wal =>
                !wal.LogCommit && !config.Initial.Matches(wal.Data)),
            "no-steal is not vacuous: the pages really do advance to another whole " +
                "snapshot, and the log is empty again once they have");
    }

    [Test]
    public void OnlyTheCommitRecordFlushMovesTheRecoverableStore()
    {
        // That the store becomes the logged write set at the commit flush is
        // definitional: it is how Recovered reads a committed log. The claim
        // worth checking is the other half — no other action moves the store
        // at all. Write-back, truncation, abort and recovery each have a
        // guard that earns this, and the broken variants below move the store
        // at exactly those steps.
        var config = WalConfig.Default;
        var moves = 0;

        foreach (var (source, edge) in ModelGraph.Edges(WriteAheadLog.Explore(config)))
        {
            var wal = (WalState)source.State;
            var before = WriteAheadLog.RecoveredSnapshot(wal);
            var after = WriteAheadLog.RecoveredSnapshot((WalState)edge.Target.State);

            if (after.SequenceEqual(before))
            {
                continue;
            }

            moves++;
            Assert.That(
                WalStep.ActionOf(edge.StepFunction),
                Is.EqualTo(WalAction.FlushCommit),
                $"{edge.StepFunction.StepFunctionId} moved the recoverable store to " +
                    $"[{string.Join(", ", after)}]");
            Assert.That(
                config.IsSnapshot(before) && wal.LogRecord.Matches(after),
                "and it moved from one whole snapshot — the durable pages — to another");
        }

        Assert.That(moves, Is.Not.Zero);
    }

    [Test]
    public void TheSpecificationNeverHoldsAPartialWriteSet()
    {
        var config = WalConfig.Default;

        foreach (var node in ModelGraph.Nodes(AtomicStore.Explore(config)))
        {
            var store = (StoreState)node.State;

            Assert.That(
                config.IsSnapshot(store.Values),
                $"[{string.Join(", ", store.Values)}] is not a whole write set");
            Assert.That(store.Phase == TxnPhase.Idle, Is.EqualTo(store.Request == null));
            Assert.That(
                store.Phase != TxnPhase.Committed || store.Request.Matches(store.Values),
                "a committed transaction has installed its whole write set");
        }
    }

    [Test]
    public void TheDataPagesAreTornThoughTheRecoverableStoreIsNot()
    {
        // This is the whole point of the log. Write-back installs one page at
        // a time, so durable data really does pass through mixtures of two
        // write sets; the store a client can observe never does.
        var config = WalConfig.Default;
        var wal = Formula.For<WalState>();
        var root = WriteAheadLog.Explore(config);
        var tornPages = wal.Observe(s => !config.IsSnapshot(s.Data), "TornPages");
        var tornRecovered = wal.Observe(
            s => !config.IsSnapshot(WriteAheadLog.RecoveredSnapshot(s)),
            "TornRecovered");

        var pages = root.Check(wal.Always(!tornPages));
        var recovered = root.Check(wal.Always(!tornRecovered));

        Assert.That(recovered.Valid, Is.True, recovered.GetTraceString());
        Assert.That(pages.Valid, Is.False, "write-back is not atomic");
        Assert.That(
            ModelGraph.Nodes(root)
                .Select(node => (WalState)node.State)
                .Where(state => !config.IsSnapshot(state.Data)),
            Is.Not.Empty.And.All.Matches<WalState>(state => state.LogCommit),
            "torn pages exist, and are always covered by a durable commit record");
    }

    // ---------------------------------------------------------------
    // What the abstraction hides, and what it refuses to hide.
    // ---------------------------------------------------------------

    [Test]
    public void DurabilityPlumbingAndRecoveryAreHidden()
    {
        var declarations = DeclaredResponses();

        Assert.That(
            declarations
                .Where(entry => entry.Value.All(response => response.HidesConcreteAction))
                .Select(entry => entry.Key)
                .OrderBy(id => id),
            Is.EqualTo(new[]
            {
                "append-redo",
                "install-data-k0",
                "install-data-k1",
                "reconnect",
                "recover",
                "restart",
                "truncate-log"
            }),
            "everything the log does for durability is internal to the implementation");

        Assert.That(
            declarations
                .Where(entry => entry.Value.All(response => !response.HidesConcreteAction))
                .Select(entry => entry.Key)
                .OrderBy(id => id),
            Is.EqualTo(new[]
            {
                "abort",
                "ack-abort",
                "ack-commit",
                "flush-commit",
                "submit-swap",
                "submit-topup"
            }));
    }

    [Test]
    public void ACrashIsHiddenExceptWhenItDoomsATransaction()
    {
        var crashes = DeclaredResponses()["crash"];

        Assert.That(
            crashes.Any(response => response.HidesConcreteAction),
            Is.True,
            "a crash with nothing in doubt is invisible to the client");
        Assert.That(
            crashes.Any(response => !response.HidesConcreteAction),
            Is.True,
            "a crash that loses the server's copy of an in-flight write is the abort");
    }

    [Test]
    public void DeclaringEveryCrashHiddenIsAMismatch()
    {
        // Hiding is a checked claim, not a way to suppress a transition.
        var result = StoreRefinement
            .StateOnly()
            .MapTransition(StoreRefinement.CrashIsAlwaysHidden)
            .Check();
        var failure = result.Trace[^1];

        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(WalStep.ActionOf(failure.ConcreteStepFunction), Is.EqualTo(WalAction.Crash));
        Assert.That(failure.DeclaredAbstractResponse.HidesConcreteAction, Is.True);
        Assert.That(
            failure.StateConsistentAbstractTransitions
                .Select(response => response.StepFunction?.StepFunctionId),
            Is.EqualTo(new[] { "spec-abort" }),
            "the mapping already knows which store action the crash performs");
        Assert.That(
            result.GetTraceString(),
            Does.Contain("The declaration hides this concrete transition, but the mapping does not"));
    }

    [Test]
    public void DeclaringTheCommitFlushAnAbortIsAMismatch()
    {
        var result = StoreRefinement
            .StateOnly()
            .MapTransition(StoreRefinement.CommitFlushIsAnAbort)
            .Check();
        var failure = result.Trace[^1];

        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            WalStep.ActionOf(failure.ConcreteStepFunction),
            Is.EqualTo(WalAction.FlushCommit));
        Assert.That(
            failure.StateConsistentAbstractTransitions
                .Select(response => response.StepFunction?.StepFunctionId),
            Is.EqualTo(new[] { "spec-commit" }));
    }

    [Test]
    public void TheProjectedTraceSeparatesHiddenActionsFromStoreActions()
    {
        var text = StoreRefinement
            .Build(options: new WalOptions { RecoveryIgnoresCommitRecord = true })
            .Check()
            .GetProjectionString();

        Assert.That(text, Does.Contain("--append-redo--"));
        Assert.That(text, Does.Contain("hidden action (abstract stutter)"));
        Assert.That(text, Does.Contain("abstract step"));
        Assert.That(text, Does.Contain("no abstract projection"));
    }

    // ---------------------------------------------------------------
    // Four broken implementations. Each is one flag on WalOptions.
    // ---------------------------------------------------------------

    [Test]
    public void InstallingUncommittedPagesWithoutUndoTearsTheStore()
    {
        // Write-back that does not wait for the log puts an uncommitted value
        // into durable data. There is no undo record, so the store recovery
        // would install is immediately a mixture of two snapshots — a state
        // the specification cannot be in.
        var config = WalConfig.Default;
        var options = new WalOptions { InstallUncommittedPages = true };
        var result = StoreRefinement.Build(config, options).Check();
        var failure = result.Trace[^1];
        var mapped = (StoreState)failure.MappedAbstractState;

        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            WalStep.ActionOf(failure.ConcreteStepFunction),
            Is.EqualTo(WalAction.InstallData));
        Assert.That(failure.AbstractCandidates, Is.Empty);
        Assert.That(mapped.Phase, Is.EqualTo(TxnPhase.Pending));
        Assert.That(
            config.IsSnapshot(mapped.Values),
            Is.False,
            $"[{string.Join(", ", mapped.Values)}] is half of an undecided write set");

        // The same defect is visible without refinement at all.
        var wal = Formula.For<WalState>();
        var torn = wal.Observe(
            s => !config.IsSnapshot(WriteAheadLog.RecoveredSnapshot(s)),
            "TornRecovered");

        Assert.That(
            WriteAheadLog.Explore(config, options).Check(wal.Always(!torn)).Valid,
            Is.False);
        Assert.That(
            ModelGraph.Nodes(WriteAheadLog.Explore(config, options))
                .Select(node => (WalState)node.State),
            Has.Some.Matches<WalState>(state =>
                !state.LogCommit && !config.IsSnapshot(state.Data)),
            "this flag is exactly the no-steal invariant of the correct model, broken: " +
                "uncommitted pages are durable, and there is nothing to undo them with");
    }

    [Test]
    public void AcknowledgingBeforeTheCommitRecordIsDurableIsNotAllowed()
    {
        // The redo record alone is not a commit: recovery would discard it.
        var result = StoreRefinement
            .Build(options: new WalOptions { AckBeforeCommitIsDurable = true })
            .Check();
        var failure = result.Trace[^1];

        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            WalStep.ActionOf(failure.ConcreteStepFunction),
            Is.EqualTo(WalAction.AckCommit));
        Assert.That(failure.AbstractCandidates, Is.Empty);
        Assert.That(
            ((StoreState)result.Trace[^2].MappedAbstractState).Phase,
            Is.EqualTo(TxnPhase.Pending),
            "the transaction the client was told about had not committed");
        Assert.That(
            ((StoreState)failure.MappedAbstractState).LastOutcome,
            Is.EqualTo(Outcome.Committed));
        Assert.That(
            ((WalState)failure.ConcreteNode.State).LogCommit,
            Is.False,
            "no commit record was durable when the client was told");
    }

    [Test]
    public void RecoveryThatIgnoresTheCommitRecordUncommitsATransaction()
    {
        // Roll-forward is not optional: a durable commit record has already
        // moved the store, and no specification action moves it back.
        var result = StoreRefinement
            .Build(options: new WalOptions { RecoveryIgnoresCommitRecord = true })
            .Check();
        var failure = result.Trace[^1];

        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            WalStep.ActionOf(failure.ConcreteStepFunction),
            Is.EqualTo(WalAction.Recover));
        Assert.That(
            result.Trace.Select(item => item.ConcreteStepFunction?.StepFunctionId),
            Does.Contain("crash").And.Contains("restart"),
            "the counterexample needs a real crash and restart");
        Assert.That(
            ((StoreState)result.Trace[^2].MappedAbstractState).Phase,
            Is.EqualTo(TxnPhase.Committed));
        Assert.That(
            ((StoreState)failure.MappedAbstractState).Phase,
            Is.EqualTo(TxnPhase.Aborted));
    }

    [Test]
    public void TruncatingBeforeTheClientIsToldLosesTheOutcome()
    {
        // Reclaiming the log is safe only once nothing depends on it any more.
        // Here the commit record is the last durable evidence that a waiting
        // client's transaction committed.
        var result = StoreRefinement
            .Build(options: new WalOptions { TruncateBeforeAcknowledgement = true })
            .Check();
        var failure = result.Trace[^1];

        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            WalStep.ActionOf(failure.ConcreteStepFunction),
            Is.EqualTo(WalAction.TruncateLog));
        Assert.That(
            ((WalState)failure.ConcreteNode.State).Client,
            Is.EqualTo(ClientPhase.Waiting));
        Assert.That(
            ((StoreState)result.Trace[^2].MappedAbstractState).Phase,
            Is.EqualTo(TxnPhase.Committed));
        Assert.That(
            ((StoreState)failure.MappedAbstractState).Phase,
            Is.EqualTo(TxnPhase.Aborted),
            "with the commit record gone, recovery would abort the transaction");
    }

    // ---------------------------------------------------------------
    // A broken mapping rather than a broken implementation.
    // ---------------------------------------------------------------

    [Test]
    public void MappingTheDataPagesInsteadOfTheRecoveredStoreFails()
    {
        // The mapping decides where the linearization point is. Reading the
        // data pages puts it at the write-back, which happens after the commit
        // and possibly after a crash — so the store does not move when the
        // specification says it must.
        var result = StoreRefinement.StateOnly(map: StoreRefinement.PagesOnly).Check();
        var failure = result.Trace[^1];
        var mapped = (StoreState)failure.MappedAbstractState;

        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            WalStep.ActionOf(failure.ConcreteStepFunction),
            Is.EqualTo(WalAction.FlushCommit));
        Assert.That(mapped.Phase, Is.EqualTo(TxnPhase.Committed));
        Assert.That(
            WalConfig.Default.Initial.Matches(mapped.Values),
            Is.True,
            "the commit is visible in the phase but not in the values");
        Assert.That(failure.AbstractCandidates, Is.Empty);
    }

    // ---------------------------------------------------------------
    // Measurements.
    // ---------------------------------------------------------------

    [Test]
    public void EagerAndLazyExplorationAgree()
    {
        var eager = WriteAheadLog.Explore(WalConfig.Default, lazy: false);
        var lazy = WriteAheadLog.Explore(WalConfig.Default, lazy: true);

        Assert.That(ModelGraph.Measure(eager), Is.EqualTo(ModelGraph.Measure(lazy)));
        Assert.That(
            eager.GetNodeFingerprint(),
            Is.EqualTo(lazy.GetNodeFingerprint()),
            "node identity is the state hash plus the step-function set");
        Assert.That(
            StoreRefinement.Build(lazy: false).Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void MeasuredModelSizes()
    {
        var config = WalConfig.Default;
        var mappings = 0;

        Assert.That(ModelGraph.Measure(WriteAheadLog.Explore(config)), Is.EqualTo((150, 292)));
        Assert.That(ModelGraph.Measure(AtomicStore.Explore(config)), Is.EqualTo((20, 32)));

        var result = StoreRefinement
            .Build(map: wal =>
            {
                // The mapping is memoized per concrete node, and this check
                // carries no auxiliary or witness state, so there is exactly
                // one proof configuration per concrete state.
                mappings++;
                return StoreRefinement.ToStore(wal);
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(mappings, Is.EqualTo(150));
    }

    [Test]
    public void AThirdWriteSetAndAThirdKeyStillRefine()
    {
        var writeSets = new WalConfig(
            2,
            new[]
            {
                WriteSet.Of("topup", 1, 2),
                WriteSet.Of("swap", 2, 1),
                WriteSet.Of("clear", 0, 0)
            });
        var keys = new WalConfig(
            3,
            new[]
            {
                WriteSet.Of("topup", 1, 2, 3),
                WriteSet.Of("swap", 3, 2, 1)
            });

        Assert.That(ModelGraph.Measure(WriteAheadLog.Explore(writeSets)), Is.EqualTo((219, 433)));
        Assert.That(ModelGraph.Measure(WriteAheadLog.Explore(keys)), Is.EqualTo((198, 396)));
        Assert.That(
            StoreRefinement.Build(writeSets).Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            StoreRefinement.Build(keys).Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    /// <summary>
    /// Applies the declaration to every reachable implementation transition and
    /// groups the answers by step-function id.
    /// </summary>
    private static Dictionary<string, List<AbstractResponse>> DeclaredResponses()
    {
        var responses = new Dictionary<string, List<AbstractResponse>>();
        foreach (var (source, edge) in ModelGraph.Edges(WriteAheadLog.Explore(WalConfig.Default)))
        {
            var id = edge.StepFunction.StepFunctionId;
            if (!responses.TryGetValue(id, out var declared))
            {
                declared = new List<AbstractResponse>();
                responses[id] = declared;
            }

            declared.Add(StoreRefinement.Declare(edge.StepFunction, (WalState)source.State));
        }

        return responses;
    }
}
