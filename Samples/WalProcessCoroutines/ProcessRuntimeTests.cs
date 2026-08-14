// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines.Tests;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>A small shared state used to exercise the process runtime primitives.</summary>
[State]
public partial class RuntimeState
{
    public int A { get; set; }
    public int B { get; set; }
    public bool Flag { get; set; }
    public bool Down { get; set; }

    /// <summary>A capacity-one slot claimed by <c>StepWhen</c>.</summary>
    public string Slot { get; set; }
}

/// <summary>
/// Focused tests of the experimental process runtime, independent of the WAL
/// model: independently active processes interleave, guarded waits and guarded
/// atomic steps re-evaluate against live shared state, and a crash discards
/// server-domain continuations while client continuations survive.
/// </summary>
[TestFixture]
public class ProcessRuntimeTests
{
    private enum RuntimeAction { Set, Claim }

    private static IEnumerable<(StateGraphNode Node, StateGraphEdge Edge, ProcessTransition Transition)>
        AllEdges(StateGraphNode root)
        => ModelGraph.Reachable(root)
            .SelectMany(node => node.Edges.Select(edge =>
                (node, edge, ModelGraph.Transition(edge))));

    [Test]
    public void IndependentlyActiveProcessesInterleaveAndArePreserved()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("p", async ctx =>
            {
                await ctx.Step("p1", s => s.A++);
                await ctx.Step("p2", s => s.A++);
            })
            .Process("q", async ctx =>
            {
                await ctx.Step("q1", s => s.B++);
                await ctx.Step("q2", s => s.B++);
            });

        var root = model.Explore();

        Assert.That(ModelGraph.LiveRoles(root), Is.EquivalentTo(new[] { "p", "q" }));
        var rootRoles = root.Edges.Select(e => ModelGraph.Transition(e).ProcessRole).ToHashSet();
        Assert.That(rootRoles, Is.EquivalentTo(new[] { "p", "q" }));

        var afterP = root.Edges.Single(e => ModelGraph.Transition(e).ProcessRole == "p").Target;
        Assert.That(ModelGraph.LiveRoles(afterP), Contains.Item("q"));

        Assert.That(
            ModelGraph.Reachable(root)
                .Any(node => ((RuntimeState)node.State).A == 1 && ((RuntimeState)node.State).B == 1),
            Is.True);

        Assert.That(
            ModelGraph.Reachable(root)
                .Any(node => ((RuntimeState)node.State).A == 2 && ((RuntimeState)node.State).B == 2),
            Is.True);
    }

    [Test]
    public void AGuardedWaitReEvaluatesAgainstLiveStateChangedByAnotherProcess()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("waiter", async ctx =>
            {
                await ctx.When("flag-set", s => s.Flag);
                await ctx.Step("react", s => s.A = 1);
            })
            .Process("setter", async ctx =>
            {
                await ctx.Step("set", s => s.Flag = true);
            });

        var root = model.Explore();

        Assert.That(ModelGraph.LiveRoles(root), Is.EquivalentTo(new[] { "setter", "waiter" }));
        Assert.That(
            root.Edges.Select(e => ModelGraph.Transition(e).ProcessRole),
            Is.EquivalentTo(new[] { "setter" }),
            "the waiter's guard is false against the live root state");

        var afterSet = root.Edges.Single().Target;
        Assert.That(((RuntimeState)afterSet.State).Flag, Is.True);
        Assert.That(
            afterSet.Edges.Select(e => ModelGraph.Transition(e).CheckpointName),
            Contains.Item("react"));
    }

    [Test]
    public void AGuardedWaitRequiresTheProcessScheduler()
    {
        Assert.That(
            () => CoroutineModel.Explore(
                "standalone-wait",
                new RuntimeState(),
                async ctx =>
                {
                    await ctx.When("flag", s => s.Flag);
                    await ctx.Step("react", s => s.A = 1);
                }),
            Throws.TypeOf<ModelDefinitionException>()
                .With.Message.Contains("requires ProcessSystemModel"));
    }

    [Test]
    public void AStepKeepsSemanticActionSeparateFromCheckpointIdentity()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("setter", async ctx =>
            {
                await ctx.Step(
                    "set-readable-name",
                    RuntimeAction.Set,
                    s => s.Flag = true,
                    subject: "flag");
            });

        var transition = ModelGraph.Transition(model.Explore().Edges.Single());

        Assert.That(transition.ProcessRole, Is.EqualTo("setter"));
        Assert.That(transition.CheckpointName, Is.EqualTo("set-readable-name"));
        Assert.That(transition.SemanticAction, Is.EqualTo(RuntimeAction.Set));
        Assert.That(transition.Subject, Is.EqualTo("flag"));
    }

    [Test]
    public void AnEnumStepGeneratesItsCheckpointNameFromTheTypedAction()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("setter", async ctx =>
                await ctx.Step(RuntimeAction.Set, s => s.Flag = true, subject: "flag"));

        var transition = ModelGraph.Transition(model.Explore().Edges.Single());

        // The typed action is authoritative; the generated stable name is a
        // collision-safe function of the enum type and value.
        Assert.That(transition.SemanticAction, Is.EqualTo(RuntimeAction.Set));
        Assert.That(transition.CheckpointName, Is.EqualTo("RuntimeAction.Set"));
        Assert.That(transition.Subject, Is.EqualTo("flag"));
    }

    // ---------------------------------------------------------------
    // StepWhen: an atomic guarded resource claim.
    // ---------------------------------------------------------------

    [Test]
    public void StepWhenClaimsAResourceAtomicallyWithNoStaleOverwrite()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("p", async ctx =>
            {
                await ctx.StepWhen(
                    RuntimeAction.Claim, when: s => s.Slot == null,
                    then: s => s.Slot = "p", subject: "p");
                await ctx.Step("release-p", s => s.Slot = null);
            })
            .Process("q", async ctx =>
            {
                await ctx.StepWhen(
                    RuntimeAction.Claim, when: s => s.Slot == null,
                    then: s => s.Slot = "q", subject: "q");
                await ctx.Step("release-q", s => s.Slot = null);
            });

        var root = model.Explore();
        var reachable = ModelGraph.Reachable(root);

        Assert.That(ModelGraph.IsComplete(root), Is.True);

        // At the root both processes race: exactly two atomic Claim edges, one
        // per contender.
        var rootClaims = root.Edges
            .Select(ModelGraph.Transition)
            .Where(t => t.SemanticAction is RuntimeAction.Claim)
            .ToList();
        Assert.That(rootClaims.Select(t => t.Subject), Is.EquivalentTo(new object[] { "p", "q" }));

        // Every Claim edge in the whole graph departs a free slot: the guard is
        // re-evaluated live, so no historical guard fires stale after the other
        // process interleaved and filled the slot. There is no overwrite.
        foreach (var (node, _, t) in AllEdges(root))
        {
            if (t.SemanticAction is RuntimeAction.Claim)
            {
                Assert.That(
                    ((RuntimeState)node.State).Slot, Is.Null,
                    "a Claim only ever fires against a free slot");
            }
        }

        // Both contenders win the slot on some path; it is single-valued, so at
        // most one holds it at a time.
        var claimed = reachable
            .Select(n => ((RuntimeState)n.State).Slot)
            .Where(slot => slot != null)
            .Distinct()
            .ToList();
        Assert.That(claimed, Is.EquivalentTo(new[] { "p", "q" }));

        // While the winner holds the slot the loser is blocked, contributing no
        // edge; after the winner releases, the loser becomes enabled.
        var heldByP = reachable.First(n => ((RuntimeState)n.State).Slot == "p");
        Assert.That(
            heldByP.Edges.Select(ModelGraph.Transition).Any(t => t.SemanticAction is RuntimeAction.Claim),
            Is.False,
            "the loser cannot claim while the slot is held");

        var qAfterRelease = AllEdges(root).Any(x =>
            x.Transition.SemanticAction is RuntimeAction.Claim &&
            (string)x.Transition.Subject == "q" &&
            ((RuntimeState)x.Node.State).Slot == null);
        Assert.That(qAfterRelease, Is.True, "the loser claims once the slot clears");
    }

    [Test]
    public void AStepWhenRequiresTheProcessScheduler()
    {
        Assert.That(
            () => CoroutineModel.Explore(
                "standalone-claim",
                new RuntimeState(),
                async ctx =>
                    await ctx.StepWhen(
                        RuntimeAction.Claim, s => s.Slot == null, s => s.Slot = "x")),
            Throws.TypeOf<ModelDefinitionException>()
                .With.Message.Contains("requires ProcessSystemModel"));
    }

    [Test]
    public void ACrashDiscardsServerDomainContinuationsAndKeepsTheClient()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState());
        var server = model.FailureDomain(
            "server",
            crashEnabled: s => !s.Down,
            onCrash: s => s.Down = true,
            restartEnabled: s => s.Down,
            onRestart: s => s.Down = false);
        server.Process("worker", Worker);
        model.Process("client", Client);

        var root = model.Explore();
        var reachable = ModelGraph.Reachable(root);

        Assert.That(ModelGraph.IsComplete(root), Is.True, "no unbounded crash generations");

        var workerInstance = ((IProcessSchedulerStep)root.StepFunctions.Single())
            .LiveProcesses.Single(p => p.Role == "worker");
        var clientInstance = ((IProcessSchedulerStep)root.StepFunctions.Single())
            .LiveProcesses.Single(p => p.Role == "client");
        Assert.That(workerInstance.Domain, Is.EqualTo("server"));
        Assert.That(clientInstance.Domain, Is.Null);

        var crashEdges = reachable
            .SelectMany(node => node.Edges.Select(edge => new { node, edge }))
            .Where(x => ModelGraph.Transition(x.edge).Control == ProcessControlKind.Crash)
            .ToList();
        Assert.That(crashEdges, Is.Not.Empty);

        Assert.That(
            crashEdges.Select(x => ModelGraph.Transition(x.edge).Domain).Distinct(),
            Is.EquivalentTo(new[] { "server" }));

        var advancedCrash = crashEdges.First(x =>
            Continuation(x.node, "worker") != "start");

        Assert.That(ModelGraph.LiveRoles(advancedCrash.node), Contains.Item("worker"));
        Assert.That(
            ModelGraph.LiveRoles(advancedCrash.edge.Target),
            Does.Not.Contain("worker"),
            "the crash disposed the server-domain continuation");
        Assert.That(
            ModelGraph.LiveRoles(advancedCrash.edge.Target),
            Contains.Item("client"),
            "the external client process survives the crash");

        var restartEdge = reachable
            .SelectMany(node => node.Edges)
            .First(edge => ModelGraph.Transition(edge).Control == ProcessControlKind.Restart);
        Assert.That(Continuation(restartEdge.Target, "worker"), Is.EqualTo("start"));
    }

    private static async ModelTask Worker(ModelContext<RuntimeState> ctx)
    {
        while (true)
        {
            await ctx.Loop("worker-loop");
            await ctx.Step("w1", s => s.A = 1);
            await ctx.Step("w2", s => s.A = 0);
        }
    }

    private static async ModelTask Client(ModelContext<RuntimeState> ctx)
    {
        while (true)
        {
            await ctx.Loop("client-loop");
            await ctx.Step("c1", s => s.B = s.B == 0 ? 1 : 0);
        }
    }

    private static string Continuation(StateGraphNode node, string role)
        => ((IProcessSchedulerStep)node.StepFunctions.Single())
            .LiveProcesses
            .Single(process => process.Role == role)
            .ContinuationId;
}
