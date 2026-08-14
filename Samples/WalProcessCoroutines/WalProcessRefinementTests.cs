// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines.Tests;

using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>
/// The process-oriented write-ahead log refines the guarded-action atomic
/// store, and the compiled process graph is finite with no depth frontier.
/// </summary>
[TestFixture]
public class WalProcessRefinementTests
{
    private static readonly WalConfig Config = WalConfig.Default;

    [Test]
    public void TheCompiledProcessGraphIsFiniteWithNoDepthFrontier()
    {
        var wal = WriteAheadLog.Explore(Config);
        var store = AtomicStore.Explore(Config, lazy: false);

        Assert.That(ModelGraph.IsComplete(wal), Is.True, "a depth frontier would make verdicts bounded");

        var walSize = ModelGraph.Size(wal);
        var storeSize = ModelGraph.Size(store);
        TestContext.WriteLine($"process WAL {walSize}, atomic store {storeSize}");

        Assert.That(walSize.Nodes, Is.GreaterThan(storeSize.Nodes));

        // A concrete guard against runaway growth: three one-shot clients, one
        // in-flight transaction, and a single failure domain keep this well
        // under a hundred thousand nodes.
        Assert.That(walSize.Nodes, Is.LessThan(100_000));
    }

    [Test]
    public void TheRecoveredSnapshotIsAlwaysAWholeSnapshot()
    {
        var wal = WriteAheadLog.Explore(Config);
        foreach (var node in ModelGraph.Reachable(wal))
        {
            var state = (WalProcessState)node.State;
            Assert.That(
                Config.IsSnapshot(WriteAheadLog.RecoveredSnapshot(state)),
                Is.True,
                state.StringRepresentation());
        }
    }

    [Test]
    public void TheProcessWalSafelyRefinesTheAtomicStore()
    {
        var result = StoreRefinement.Build(Config).Check();
        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            result.GetTraceString());
    }

    [Test]
    public void EveryClientRoleIsLiveOutsideTheServerDomainAlongsideTheWorkers()
    {
        var wal = WriteAheadLog.Explore(Config);

        // The initial configuration has every client and every persistent worker
        // active: five independently active processes at once.
        Assert.That(
            ModelGraph.LiveRoles(wal),
            Is.EquivalentTo(
                Roles.Clients.Concat(new[] { Roles.PageWriter, Roles.Recovery })));

        // Somewhere a launched handler coexists with the workers and the clients.
        Assert.That(
            ModelGraph.Reachable(wal)
                .Any(node => ModelGraph.LiveRoles(node).Contains(Roles.Handler)),
            Is.True);
    }

    [Test]
    public void APrecommitCrashIsDeclaredAndCheckedAsAnAbstractAbort()
    {
        var wal = WriteAheadLog.Explore(Config);

        var doomedCrash = ModelGraph.Reachable(wal)
            .SelectMany(node => node.Edges.Select(edge => new { node, edge }))
            .First(x =>
                ModelGraph.Transition(x.edge).Control == ProcessControlKind.Crash &&
                StoreRefinement.DoomsInFlightTransaction((WalProcessState)x.node.State));

        var response = StoreRefinement.Declare(
            ModelGraph.Transition(doomedCrash.edge),
            (WalProcessState)doomedCrash.node.State);
        Assert.That(
            response.ToString(),
            Is.EqualTo(StoreStep.Performs(StoreAction.Abort).ToString()));

        Assert.That(
            StoreRefinement.PhaseOf((WalProcessState)doomedCrash.node.State),
            Is.EqualTo(TxnPhase.Pending));
        Assert.That(
            StoreRefinement.PhaseOf((WalProcessState)doomedCrash.edge.Target.State),
            Is.EqualTo(TxnPhase.Aborted));

        Assert.That(
            StoreRefinement.Build(Config).Check().Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void DeclaringEveryCrashHiddenIsRejected()
    {
        var result = Refinement
            .Between<WalProcessState, StoreState>(
                WriteAheadLog.Explore(Config),
                AtomicStore.Explore(Config))
            .Map(StoreRefinement.ToStore)
            .MapTransition(transition =>
            {
                var process = (ProcessTransition)transition.Metadata;
                return process.Control == ProcessControlKind.Crash
                    ? AbstractResponse.Hidden
                    : StoreRefinement.Declarations(transition);
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
    }

    [Test]
    public void DeclaringTheCommitFlushAnAbortIsRejected()
    {
        var result = Refinement
            .Between<WalProcessState, StoreState>(
                WriteAheadLog.Explore(Config),
                AtomicStore.Explore(Config))
            .Map(StoreRefinement.ToStore)
            .MapTransition(transition =>
            {
                var process = (ProcessTransition)transition.Metadata;
                return process.SemanticAction is WalAction.FlushCommit
                    ? StoreStep.Performs(StoreAction.Abort)
                    : StoreRefinement.Declarations(transition);
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
    }

    [Test]
    public void AtMostOneRequestHandlerIsEverLive()
    {
        var wal = WriteAheadLog.Explore(Config);
        foreach (var node in ModelGraph.Reachable(wal))
        {
            Assert.That(
                ModelGraph.LiveRoles(node).Count(role => role == Roles.Handler),
                Is.LessThanOrEqualTo(1),
                "the guarded launch never duplicates the handler");
        }
    }
}
