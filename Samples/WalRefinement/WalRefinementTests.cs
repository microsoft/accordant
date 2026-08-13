namespace WalRefinement;

using System.Linq;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Safety refinement of the write-ahead log against the atomic transaction
/// store, the invariants of the implementation graph, and the four broken
/// implementations that each drop one ordering constraint the protocol
/// depends on.
/// </summary>
[TestFixture]
public class WalRefinementTests
{
    // ---------------------------------------------------------------
    // The correct control.
    // ---------------------------------------------------------------

    [Test]
    public void WriteAheadLoggingRefinesTheAtomicStore()
    {
        var result = WalRefinementCheck.Build().Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void DeclaringTheStoreActionsRefinesToo()
    {
        // The declarations only narrow, so this is the stronger claim: every
        // implementation action performs the store action the model names.
        var result = WalRefinementCheck.BuildDeclared().Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    // ---------------------------------------------------------------
    // Invariants of the two graphs.
    // ---------------------------------------------------------------

    [Test]
    public void ConcreteGraphPreservesTheProtocolInvariants()
    {
        foreach (var node in ModelGraph.Nodes(WriteAheadLog.Explore(WalConfig.Default)))
        {
            var wal = (WalState)node.State;

            Assert.That(
                !wal.LogCommit || wal.LogRedo,
                "write-ahead ordering: no commit record without its redo record");
            Assert.That(
                !wal.LogCommit ||
                    wal.Server == ServerPhase.Committed ||
                    wal.Server == ServerPhase.Down ||
                    wal.Server == ServerPhase.Recovering,
                "a durable commit record is only ever forgotten by truncation");
            Assert.That(
                wal.Server != ServerPhase.Active || wal.Client == ClientPhase.Waiting,
                "the server only holds a write while a client is waiting for it");
            Assert.That(
                wal.LogRedo == (wal.LogValue != TransactionStore.NoValue),
                "the logged payload is canonical, so no two states differ by a dead field");
            Assert.That(
                (wal.Client == ClientPhase.Waiting) ==
                    (wal.Request != TransactionStore.NoValue),
                "the outstanding request is canonical");
            Assert.That(
                WriteAheadLog.Recovered(wal, 0),
                Is.EqualTo(WriteAheadLog.Recovered(wal, 1)),
                "the state recovery would install is never torn");
        }
    }

    [Test]
    public void AbstractGraphNeverHoldsAPartiallyAppliedTransaction()
    {
        foreach (var node in ModelGraph.Nodes(TransactionStore.Explore(WalConfig.Default)))
        {
            var store = (StoreState)node.State;

            Assert.That(store.Values[0], Is.EqualTo(store.Values[1]));
            Assert.That(
                store.Phase == TxnPhase.Idle,
                Is.EqualTo(store.Request == TransactionStore.NoValue));
        }
    }

    [Test]
    public void TheDataPagesAreTornEvenThoughTheRecoverableStoreIsNot()
    {
        // This is the whole point of the log. Write-back installs one page at
        // a time, so durable data really does go through states where the
        // keys disagree; the store the client can ever observe does not.
        var wal = Formula.For<WalState>();
        var root = WriteAheadLog.Explore(WalConfig.Default);
        var tornPages = wal.Observe(s => s.Data[0] != s.Data[1], "TornPages");
        var tornRecovered = wal.Observe(
            s => WriteAheadLog.Recovered(s, 0) != WriteAheadLog.Recovered(s, 1),
            "TornRecovered");

        var pages = root.Check(wal.Always(!tornPages));
        var recovered = root.Check(wal.Always(!tornRecovered));

        Assert.That(recovered.Valid, Is.True, recovered.GetTraceString());
        Assert.That(pages.Valid, Is.False, "write-back is not atomic");
        Assert.That(
            ModelGraph.Nodes(root)
                .Select(node => (WalState)node.State)
                .Where(state => state.Data[0] != state.Data[1]),
            Is.Not.Empty.And.All.Matches<WalState>(state => state.LogCommit),
            "torn pages exist, and are always covered by a durable commit record");
    }

    // ---------------------------------------------------------------
    // Four broken implementations. Each is one flag on WalOptions.
    // ---------------------------------------------------------------

    [Test]
    public void InstallingUncommittedDataWithoutUndoTearsTheStore()
    {
        // Write-back that does not wait for the log puts an uncommitted value
        // into durable data. There is no undo record, so the store recovery
        // would install is immediately a state the specification cannot be in.
        var options = new WalOptions { InstallUncommittedPages = true };
        var result = WalRefinementCheck.BuildDeclared(options: options).Check();
        var failure = result.Trace[^1];
        var mapped = (StoreState)failure.MappedAbstractState;

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(failure.ConcreteStepFunction, Is.TypeOf<InstallDataStep>());
        Assert.That(failure.AbstractCandidates, Is.Empty);
        Assert.That(mapped.Phase, Is.EqualTo(TxnPhase.Pending));
        Assert.That(
            mapped.Values[0],
            Is.Not.EqualTo(mapped.Values[1]),
            "half of an undecided transaction is durable");

        // The same defect is visible without refinement at all.
        var wal = Formula.For<WalState>();
        var torn = wal.Observe(
            s => WriteAheadLog.Recovered(s, 0) != WriteAheadLog.Recovered(s, 1),
            "TornRecovered");

        Assert.That(
            WriteAheadLog.Explore(WalConfig.Default, options).Check(wal.Always(!torn)).Valid,
            Is.False);
    }

    [Test]
    public void AcknowledgingBeforeTheCommitRecordIsDurableIsNotAllowed()
    {
        // The redo record alone is not a commit: recovery would discard it.
        var result = WalRefinementCheck
            .BuildDeclared(options: new WalOptions { AckBeforeCommitIsDurable = true })
            .Check();
        var failure = result.Trace[^1];
        var before = (StoreState)result.Trace[^2].MappedAbstractState;

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(failure.ConcreteStepFunction, Is.TypeOf<AckCommitStep>());
        Assert.That(failure.AbstractCandidates, Is.Empty);
        Assert.That(
            before.Phase,
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
        var result = WalRefinementCheck
            .BuildDeclared(options: new WalOptions { RecoveryIgnoresCommitRecord = true })
            .Check();
        var failure = result.Trace[^1];

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(failure.ConcreteStepFunction, Is.TypeOf<RecoverStep>());
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
        // Reclaiming the log is safe only once nothing depends on it any
        // more. Here the commit record is the last durable evidence that a
        // waiting client's transaction committed.
        var result = WalRefinementCheck
            .BuildDeclared(options: new WalOptions { TruncateBeforeAcknowledgement = true })
            .Check();
        var failure = result.Trace[^1];

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(failure.ConcreteStepFunction, Is.TypeOf<TruncateLogStep>());
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
        // data pages puts it at the write-back, which happens after the
        // commit and possibly after a crash — so the store does not move when
        // the specification says it must.
        var result = WalRefinementCheck
            .Build(mapping: WalRefinementCheck.MapDurablePagesOnly)
            .Check();
        var failure = result.Trace[^1];
        var mapped = (StoreState)failure.MappedAbstractState;

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(failure.ConcreteStepFunction, Is.TypeOf<FlushCommitStep>());
        Assert.That(mapped.Phase, Is.EqualTo(TxnPhase.Committed));
        Assert.That(
            mapped.Values,
            Has.All.EqualTo(TransactionStore.InitialValue),
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
            WalRefinementCheck.Build(lazy: false).Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void MeasuredModelSizes()
    {
        var config = WalConfig.Default;

        Assert.That(ModelGraph.Measure(WriteAheadLog.Explore(config)), Is.EqualTo((95, 184)));
        Assert.That(ModelGraph.Measure(TransactionStore.Explore(config)), Is.EqualTo((15, 24)));

        var mappings = 0;
        var result = WalRefinementCheck
            .Build(mapping: wal =>
            {
                // The mapping is memoized per concrete node, and this check
                // carries no auxiliary or witness state, so one proof
                // configuration per concrete state is all there is.
                mappings++;
                return WalRefinementCheck.MapToStore(wal);
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(mappings, Is.EqualTo(95));
    }

    [Test]
    public void AThirdValueAndAThirdKeyStillRefine()
    {
        var values = new WalConfig(2, new[] { 0, 1, 2 });
        var keys = new WalConfig(3, new[] { 0, 1 });

        Assert.That(ModelGraph.Measure(WriteAheadLog.Explore(values)), Is.EqualTo((219, 433)));
        Assert.That(ModelGraph.Measure(WriteAheadLog.Explore(keys)), Is.EqualTo((143, 288)));
        Assert.That(
            WalRefinementCheck.BuildDeclared(values).Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            WalRefinementCheck.BuildDeclared(keys).Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }
}
