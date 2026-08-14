// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines.Tests;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>
/// The process WAL design: a local handler that reads like ordinary sequential
/// implementation code (AppendRedo -> FlushCommit -> AckCommit with no scheduling
/// guards between the steps), structural failure-domain ownership with no boolean
/// flag, and a normal/recovery reporting split in which the original handler or
/// the recovery worker reports — never both — with no recovery-only shared phase.
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
    public void OwnershipIsStructuralWithClientsOutsideTheNamedServerDomain()
    {
        var root = WriteAheadLog.Explore(Config);

        var atRoot = ModelGraph.LiveProcesses(root).ToDictionary(p => p.Role, p => p.Domain);
        foreach (var client in Roles.Clients)
        {
            Assert.That(atRoot[client], Is.Null, $"client {client} survives crashes: outside every domain");
        }

        Assert.That(atRoot[Roles.PageWriter], Is.EqualTo("server"));
        Assert.That(atRoot[Roles.Recovery], Is.EqualTo("server"));

        var handlerDomains = ModelGraph.Reachable(root)
            .SelectMany(ModelGraph.LiveProcesses)
            .Where(p => p.Role == Roles.Handler)
            .Select(p => p.Domain)
            .Distinct()
            .ToList();
        Assert.That(handlerDomains, Is.EquivalentTo(new[] { "server" }));

        var controlDomains = AllEdges(root)
            .Where(x => x.Transition.Control == ProcessControlKind.Crash ||
                        x.Transition.Control == ProcessControlKind.Restart)
            .Select(x => x.Transition.Domain)
            .Distinct()
            .ToList();
        Assert.That(controlDomains, Is.EquivalentTo(new[] { "server" }));
    }

    [Test]
    public void TheOnlyRolesAreTwoClientsAndThreeServerWorkers()
    {
        var root = WriteAheadLog.Explore(Config);

        // No persistent "reporter" role, and no per-client server process: the
        // clients are outside the domain, the three workers inside it.
        Assert.That(
            ModelGraph.Reachable(root).SelectMany(ModelGraph.LiveRoles).Distinct(),
            Is.EquivalentTo(
                Roles.Clients.Concat(new[] { Roles.PageWriter, Roles.Recovery, Roles.Handler })));
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

        Assert.That(
            handlerEdges.Select(x => x.Transition.CheckpointKind).Distinct(),
            Is.EquivalentTo(new[] { (ModelCheckpointKind?)ModelCheckpointKind.Step }));
        Assert.That(
            handlerEdges.Select(x => x.Transition.SemanticAction).Distinct(),
            Is.EquivalentTo(new object[]
            {
                WalAction.AppendRedo, WalAction.FlushCommit, WalAction.AckCommit
            }));

        foreach (var (node, _, t) in handlerEdges)
        {
            var s = (WalProcessState)node.State;
            switch ((WalAction)t.SemanticAction)
            {
                case WalAction.AppendRedo:
                    Assert.That(s.Wal.LogRedo, Is.False, "append-redo is first");
                    Assert.That(s.Wal.LogCommit, Is.False);
                    break;
                case WalAction.FlushCommit:
                    Assert.That(s.Wal.LogRedo, Is.True, "flush-commit follows append-redo");
                    Assert.That(s.Wal.LogCommit, Is.False);
                    break;
                case WalAction.AckCommit:
                    Assert.That(s.Wal.LogCommit, Is.True, "ack-commit follows flush-commit");
                    Assert.That(s.Exchange.Pending, Is.Not.Null);
                    break;
            }
        }
    }

    [Test]
    public void DurableCommitAlwaysImpliesADurableRedoRecord()
    {
        foreach (var node in ModelGraph.Reachable(WriteAheadLog.Explore(Config)))
        {
            var s = (WalProcessState)node.State;
            if (s.Wal.LogCommit)
            {
                Assert.That(s.Wal.LogRedo, Is.True, s.StringRepresentation());
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

        var precommitCrash = AllEdges(root).First(x =>
            x.Transition.Control == ProcessControlKind.Crash &&
            ModelGraph.LiveRoles(x.Node).Contains(Roles.Handler) &&
            StoreRefinement.DoomsInFlightTransaction((WalProcessState)x.Node.State));
        Assert.That(ModelGraph.LiveRoles(precommitCrash.Node), Contains.Item(Roles.Handler));
        Assert.That(
            ModelGraph.LiveRoles(precommitCrash.Edge.Target),
            Does.Not.Contain(Roles.Handler),
            "the crash atomically destroys the handler continuation");

        // Recovery — not the handler — takes the abort acknowledgement, from a
        // recovering server holding an uncommitted request.
        var recoveryAbort = AllEdges(root)
            .Where(x => x.Transition.ProcessRole == Roles.Recovery &&
                        x.Transition.SemanticAction is WalAction.AckAbort)
            .ToList();
        Assert.That(recoveryAbort, Is.Not.Empty, "recovery reports the abort of a doomed transaction");
        foreach (var (node, _, _) in recoveryAbort)
        {
            var s = (WalProcessState)node.State;
            Assert.That(s.Server.Mode, Is.EqualTo(ServerMode.Recovering));
            Assert.That(s.Wal.LogCommit, Is.False);
            Assert.That(s.Exchange.Pending, Is.Not.Null);
        }
    }

    [Test]
    public void ACrashAfterCommitBeforeReportRemovesTheHandlerAndRecoveryReportsTheCommit()
    {
        var root = WriteAheadLog.Explore(Config);

        var postCommitCrash = AllEdges(root).First(x =>
            x.Transition.Control == ProcessControlKind.Crash &&
            ModelGraph.LiveRoles(x.Node).Contains(Roles.Handler) &&
            ((WalProcessState)x.Node.State) is { Wal.LogCommit: true } s &&
            s.Exchange.Pending != null);
        Assert.That(ModelGraph.LiveRoles(postCommitCrash.Node), Contains.Item(Roles.Handler));
        Assert.That(
            ModelGraph.LiveRoles(postCommitCrash.Edge.Target),
            Does.Not.Contain(Roles.Handler));

        // Recovery takes the commit acknowledgement, from a recovering server
        // holding a durable commit.
        var recoveryCommit = AllEdges(root)
            .Where(x => x.Transition.ProcessRole == Roles.Recovery &&
                        x.Transition.SemanticAction is WalAction.AckCommit)
            .ToList();
        Assert.That(recoveryCommit, Is.Not.Empty, "recovery reports a durable commit whose handler was killed");
        foreach (var (node, _, _) in recoveryCommit)
        {
            var s = (WalProcessState)node.State;
            Assert.That(s.Server.Mode, Is.EqualTo(ServerMode.Recovering));
            Assert.That(s.Wal.LogCommit, Is.True);
            Assert.That(s.Exchange.Pending, Is.Not.Null);
        }
    }

    [Test]
    public void ACrashAfterTheReportDoesNotReportTwice()
    {
        var root = WriteAheadLog.Explore(Config);

        // No acknowledgement — by the handler or by recovery — ever fires unless
        // a request is genuinely still outstanding. Whichever of the two reports
        // first clears the slot, and the other can no longer report.
        foreach (var (node, _, t) in AllEdges(root))
        {
            if (t.SemanticAction is WalAction.AckCommit or WalAction.AckAbort)
            {
                Assert.That(
                    ((WalProcessState)node.State).Exchange.Pending,
                    Is.Not.Null,
                    $"{t.ProcessRole} {t.SemanticAction} fired with no outstanding request");
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

    [Test]
    public void ThereIsNoRecoveryOnlyServerPhaseTheDecisionIsALocal()
    {
        // The server has exactly the three lifecycle modes and no recovered-commit
        // or recovered-abort phase: recovery's decision lives only as a replay
        // local, so the mode enum stays minimal.
        Assert.That(
            System.Enum.GetNames(typeof(ServerMode)),
            Is.EquivalentTo(new[] { "Running", "Down", "Recovering" }));
    }
}
