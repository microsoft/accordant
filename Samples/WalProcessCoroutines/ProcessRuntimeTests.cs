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
}

/// <summary>
/// Focused tests of the experimental process runtime, independent of the WAL
/// model: independently active processes interleave, guarded waits re-evaluate
/// against live shared state, and a crash discards server-domain continuations
/// while client continuations survive.
/// </summary>
[TestFixture]
public class ProcessRuntimeTests
{
    private enum RuntimeAction { Set }

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

        // Both processes are live and both first steps are enabled at the root.
        Assert.That(ModelGraph.LiveRoles(root), Is.EquivalentTo(new[] { "p", "q" }));
        var rootRoles = root.Edges.Select(e => ModelGraph.Transition(e).ProcessRole).ToHashSet();
        Assert.That(rootRoles, Is.EquivalentTo(new[] { "p", "q" }));

        // Taking p's step preserves q as a live independent process.
        var afterP = root.Edges.Single(e => ModelGraph.Transition(e).ProcessRole == "p").Target;
        Assert.That(ModelGraph.LiveRoles(afterP), Contains.Item("q"));

        // A genuinely interleaved state exists: each did exactly one step.
        Assert.That(
            ModelGraph.Reachable(root)
                .Any(node => ((RuntimeState)node.State).A == 1 && ((RuntimeState)node.State).B == 1),
            Is.True);

        // Both processes complete: the terminal state has A == 2 and B == 2.
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

        // The waiter is blocked at the root: only the setter can move even
        // though both processes are live.
        Assert.That(ModelGraph.LiveRoles(root), Is.EquivalentTo(new[] { "setter", "waiter" }));
        Assert.That(
            root.Edges.Select(e => ModelGraph.Transition(e).ProcessRole),
            Is.EquivalentTo(new[] { "setter" }),
            "the waiter's guard is false against the live root state");

        // After the setter flips the flag, the waiter's guard is re-evaluated
        // against the new live state and it becomes enabled.
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

        // The worker belongs to the named failure domain; the client does not.
        var workerInstance = ((IProcessSchedulerStep)root.StepFunctions.Single())
            .LiveProcesses.Single(p => p.Role == "worker");
        var clientInstance = ((IProcessSchedulerStep)root.StepFunctions.Single())
            .LiveProcesses.Single(p => p.Role == "client");
        Assert.That(workerInstance.Domain, Is.EqualTo("server"));
        Assert.That(clientInstance.Domain, Is.Null);

        // A crash edge departs from a state where the server-domain worker has
        // advanced its continuation past the start.
        var crashEdges = reachable
            .SelectMany(node => node.Edges.Select(edge => new { node, edge }))
            .Where(x => ModelGraph.Transition(x.edge).Control == ProcessControlKind.Crash)
            .ToList();
        Assert.That(crashEdges, Is.Not.Empty);

        // The crash edge names the failure domain it belongs to.
        Assert.That(
            crashEdges.Select(x => ModelGraph.Transition(x.edge).Domain).Distinct(),
            Is.EquivalentTo(new[] { "server" }));

        var advancedCrash = crashEdges.First(x =>
            Continuation(x.node, "worker") != "start");

        // The worker continuation is discarded by the crash; the client survives.
        Assert.That(ModelGraph.LiveRoles(advancedCrash.node), Contains.Item("worker"));
        Assert.That(
            ModelGraph.LiveRoles(advancedCrash.edge.Target),
            Does.Not.Contain("worker"),
            "the crash disposed the server-domain continuation");
        Assert.That(
            ModelGraph.LiveRoles(advancedCrash.edge.Target),
            Contains.Item("client"),
            "the external client process survives the crash");

        // Restart relaunches the worker with a fresh continuation.
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
