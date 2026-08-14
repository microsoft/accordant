// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines.Tests;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>
/// The corrected process WAL design: a local handler that reads like ordinary
/// sequential implementation code (AppendRedo -> FlushCommit -> AckCommit with no
/// scheduling guards between the steps), structural failure-domain ownership with
/// no boolean flag, and a normal/recovery reporting split in which the original
/// handler or the recovery worker reports — never both.
/// </summary>
[TestFixture]
public class WalProcessDesignTests
{
    private static readonly WalConfig Config = WalConfig.Default;

    private static IEnumerable<(StateGraphNode Node, StateGraphEdge Edge, ProcessTransition Transition)>
        AllEdges(StateGraphNode root)
        => ModelGraph.Reachable(root)
            .SelectMany(node => node.Edges.Select(edge =>
                (node, edge, ModelGraph.Transition(edge))));

    // ---------------------------------------------------------------
    // Structural ownership.
    // ---------------------------------------------------------------

    [Test]
    public void OwnershipIsStructuralWithTheClientOutsideTheNamedServerDomain()
    {
        var root = WriteAheadLog.Explore(Config);

        // At the root every persistent process is live with its structural
        // domain: the client is outside every domain, the workers are inside the
        // named "server" domain — expressed by where each was registered, not by
        // a boolean flag.
        var atRoot = ModelGraph.LiveProcesses(root).ToDictionary(p => p.Role, p => p.Domain);
        Assert.That(atRoot[Roles.Client], Is.Null, "the client survives crashes: outside every domain");
        Assert.That(atRoot[Roles.PageWriter], Is.EqualTo("server"));
        Assert.That(atRoot[Roles.Recovery], Is.EqualTo("server"));

        // The launched handler is also inside the server domain wherever it runs.
        var handlerDomains = ModelGraph.Reachable(root)
            .SelectMany(ModelGraph.LiveProcesses)
            .Where(p => p.Role == Roles.Handler)
            .Select(p => p.Domain)
            .Distinct()
            .ToList();
        Assert.That(handlerDomains, Is.EquivalentTo(new[] { "server" }));

        // Every crash/restart control edge names the failure domain it belongs to.
        var controlDomains = AllEdges(root)
            .Where(x => x.Transition.Control == ProcessControlKind.Crash ||
                        x.Transition.Control == ProcessControlKind.Restart)
            .Select(x => x.Transition.Domain)
            .Distinct()
            .ToList();
        Assert.That(controlDomains, Is.EquivalentTo(new[] { "server" }));
    }

    [Test]
    public void ThereIsNoPersistentReporterRole()
    {
        var root = WriteAheadLog.Explore(Config);

        // No role named "reporter" is ever registered or live.
        Assert.That(
            ModelGraph.Reachable(root).SelectMany(ModelGraph.LiveRoles).Distinct(),
            Is.EquivalentTo(new[] { Roles.Client, Roles.PageWriter, Roles.Recovery, Roles.Handler }));
    }

    // ---------------------------------------------------------------
    // The handler reads like sequential local code.
    // ---------------------------------------------------------------

    [Test]
    public void TheHandlerProgressesAppendRedoThenFlushCommitThenAckCommitWithNoGuardEdge()
    {
        var root = WriteAheadLog.Explore(Config);
        var handlerEdges = AllEdges(root)
            .Where(x => x.Transition.ProcessRole == Roles.Handler && !x.Transition.IsControl)
            .ToList();

        // Every visible handler edge is a Step carrying one of exactly three
        // typed actions — never a When (a guard is internal and edge-less, and
        // the handler declares none between its steps).
        Assert.That(
            handlerEdges.Select(x => x.Transition.CheckpointKind).Distinct(),
            Is.EquivalentTo(new[] { (ModelCheckpointKind?)ModelCheckpointKind.Step }));
        Assert.That(
            handlerEdges.Select(x => x.Transition.SemanticAction).Distinct(),
            Is.EquivalentTo(new object[]
            {
                WalAction.AppendRedo, WalAction.FlushCommit, WalAction.AckCommit
            }));

        // Each step is only ever taken from its correct predecessor state, which
        // pins the order AppendRedo -> FlushCommit -> AckCommit.
        foreach (var (node, _, t) in handlerEdges)
        {
            var s = (WalProcessState)node.State;
            switch ((WalAction)t.SemanticAction)
            {
                case WalAction.AppendRedo:
                    Assert.That(s.LogRedo, Is.False, "append-redo is first");
                    Assert.That(s.LogCommit, Is.False);
                    break;
                case WalAction.FlushCommit:
                    Assert.That(s.LogRedo, Is.True, "flush-commit follows append-redo");
                    Assert.That(s.LogCommit, Is.False);
                    break;
                case WalAction.AckCommit:
                    Assert.That(s.LogCommit, Is.True, "ack-commit follows flush-commit");
                    Assert.That(s.Client, Is.EqualTo(ClientPhase.Waiting));
                    break;
            }
        }
    }

    [Test]
    public void DurableCommitAlwaysImpliesADurableRedoRecord()
    {
        // The order is also visible as a state invariant: no reachable state has
        // a commit record without the redo record that must precede it.
        foreach (var node in ModelGraph.Reachable(WriteAheadLog.Explore(Config)))
        {
            var s = (WalProcessState)node.State;
            if (s.LogCommit)
            {
                Assert.That(s.LogRedo, Is.True, s.StringRepresentation());
            }
        }
    }

    // ---------------------------------------------------------------
    // Normal vs recovery reporting.
    // ---------------------------------------------------------------

    [Test]
    public void ACrashBeforeCommitRemovesTheHandlerAndRecoveryReportsTheAbort()
    {
        var root = WriteAheadLog.Explore(Config);

        // A precommit crash departs an in-flight state where the handler is live
        // and discards it.
        var precommitCrash = AllEdges(root).First(x =>
            x.Transition.Control == ProcessControlKind.Crash &&
            ModelGraph.LiveRoles(x.Node).Contains(Roles.Handler) &&
            StoreRefinement.DoomsInFlightTransaction((WalProcessState)x.Node.State));
        Assert.That(ModelGraph.LiveRoles(precommitCrash.Node), Contains.Item(Roles.Handler));
        Assert.That(
            ModelGraph.LiveRoles(precommitCrash.Edge.Target),
            Does.Not.Contain(Roles.Handler),
            "the crash atomically destroys the handler continuation");

        // Recovery — not the handler — takes the abort acknowledgement, from the
        // recovery-only RecoveredAbort phase.
        var recoveryAbort = AllEdges(root)
            .Where(x => x.Transition.ProcessRole == Roles.Recovery &&
                        x.Transition.SemanticAction is WalAction.AckAbort)
            .ToList();
        Assert.That(recoveryAbort, Is.Not.Empty, "recovery reports the abort of a doomed transaction");
        foreach (var (node, _, _) in recoveryAbort)
        {
            var s = (WalProcessState)node.State;
            Assert.That(s.Server, Is.EqualTo(ServerPhase.RecoveredAbort));
            Assert.That(s.Client, Is.EqualTo(ClientPhase.Waiting));
        }
    }

    [Test]
    public void ACrashAfterCommitBeforeReportRemovesTheHandlerAndRecoveryReportsTheCommit()
    {
        var root = WriteAheadLog.Explore(Config);

        // There is a crash that departs a durably-committed, still-waiting state
        // where the handler is live and discards it before it acknowledged.
        var postCommitCrash = AllEdges(root).First(x =>
            x.Transition.Control == ProcessControlKind.Crash &&
            ModelGraph.LiveRoles(x.Node).Contains(Roles.Handler) &&
            ((WalProcessState)x.Node.State) is { LogCommit: true, Client: ClientPhase.Waiting });
        Assert.That(ModelGraph.LiveRoles(postCommitCrash.Node), Contains.Item(Roles.Handler));
        Assert.That(
            ModelGraph.LiveRoles(postCommitCrash.Edge.Target),
            Does.Not.Contain(Roles.Handler));

        // Recovery takes the commit acknowledgement, from the recovery-only
        // RecoveredCommit phase.
        var recoveryCommit = AllEdges(root)
            .Where(x => x.Transition.ProcessRole == Roles.Recovery &&
                        x.Transition.SemanticAction is WalAction.AckCommit)
            .ToList();
        Assert.That(recoveryCommit, Is.Not.Empty, "recovery reports a durable commit whose handler was killed");
        foreach (var (node, _, _) in recoveryCommit)
        {
            var s = (WalProcessState)node.State;
            Assert.That(s.Server, Is.EqualTo(ServerPhase.RecoveredCommit));
            Assert.That(s.LogCommit, Is.True);
            Assert.That(s.Client, Is.EqualTo(ClientPhase.Waiting));
        }
    }

    [Test]
    public void ACrashAfterTheReportDoesNotReportTwice()
    {
        var root = WriteAheadLog.Explore(Config);

        // No acknowledgement — by the handler or by recovery — ever fires unless
        // a client is genuinely still waiting. That is the whole no-double-report
        // guarantee: whichever of the two reports first sets the client idle, and
        // the other can no longer report.
        foreach (var (node, _, t) in AllEdges(root))
        {
            if (t.SemanticAction is WalAction.AckCommit or WalAction.AckAbort)
            {
                Assert.That(
                    ((WalProcessState)node.State).Client,
                    Is.EqualTo(ClientPhase.Waiting),
                    $"{t.ProcessRole} {t.SemanticAction} fired without a waiting client");
            }
        }
    }

    [Test]
    public void TheHandlerAndRecoveryAreAlternativeReporters()
    {
        var root = WriteAheadLog.Explore(Config);
        var reporters = AllEdges(root)
            .Where(x => x.Transition.SemanticAction is WalAction.AckCommit or WalAction.AckAbort)
            .Select(x => x.Transition.ProcessRole)
            .Distinct()
            .ToList();

        // Both the handler and recovery report somewhere in the graph, and only
        // those two ever do.
        Assert.That(reporters, Is.EquivalentTo(new[] { Roles.Handler, Roles.Recovery }));

        // The handler only ever commits (it has no abort); every abort report is
        // recovery's.
        var handlerActions = AllEdges(root)
            .Where(x => x.Transition.ProcessRole == Roles.Handler &&
                        x.Transition.SemanticAction is WalAction.AckCommit or WalAction.AckAbort)
            .Select(x => x.Transition.SemanticAction)
            .Distinct();
        Assert.That(handlerActions, Is.EquivalentTo(new object[] { WalAction.AckCommit }));
    }
}
