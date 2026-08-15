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
    private enum RuntimeAction { Set, Claim, Pick, Pulse, NoOp }

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
    public void ForeverCanonicalizesDirectlyToTheNextIteration()
    {
        var structured = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("toggle", ctx => ctx.Forever("toggle-loop", ToggleIteration))
            .Explore();

        var structuredReport = ProcessGraphDiagnostics.Describe(structured);
        var structuredProcess = ((IProcessSchedulerStep)structured.StepFunctions.Single())
            .LiveProcesses.Single();

        Assert.Multiple(() =>
        {
            Assert.That(structuredReport.Complete, Is.True);
            Assert.That(structuredReport.DomainStateCount, Is.EqualTo(2));
            Assert.That(structuredReport.ConfigurationCount, Is.EqualTo(2));
            Assert.That(
                structured.Edges.Single().Target.StepFunctions.Single().StepFunctionId,
                Is.EqualTo(structured.StepFunctions.Single().StepFunctionId),
                "the completed body is discarded on the same semantic edge");
            Assert.That(
                structuredProcess.Frames.Select(frame => frame.Kind),
                Is.EqualTo(new[]
                {
                    ProcessContinuationFrameKind.Root,
                    ProcessContinuationFrameKind.ForeverIteration
                }));
            Assert.That(structuredProcess.Frames.Last().CheckpointHistory, Is.Empty);
        });
    }

    [Test]
    public void ForeverDoesNotPreReadTheNextIterationAcrossAnInterleaving()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("copier", ctx => ctx.Forever("copy-loop", CopyIteration))
            .Process("writer", async ctx =>
                await ctx.Step("write-a", s => s.A = 1));

        var root = model.Explore();
        var afterInitialCopy = root.Edges.Single(edge =>
            ModelGraph.Transition(edge).ProcessRole == "copier").Target;
        var afterWrite = afterInitialCopy.Edges.Single(edge =>
            ModelGraph.Transition(edge).ProcessRole == "writer").Target;
        var afterFreshCopy = afterWrite.Edges.Single(edge =>
            ModelGraph.Transition(edge).ProcessRole == "copier").Target;

        Assert.Multiple(() =>
        {
            Assert.That(((RuntimeState)afterInitialCopy.State).B, Is.EqualTo(0));
            Assert.That(((RuntimeState)afterWrite.State).A, Is.EqualTo(1));
            Assert.That(
                ((RuntimeState)afterFreshCopy.State).B,
                Is.EqualTo(1),
                "the next iteration's Read must run only when copier is scheduled again");
            Assert.That(
                ((IProcessSchedulerStep)afterInitialCopy.StepFunctions.Single())
                    .LiveProcesses.Single(process => process.Role == "copier")
                    .Frames.Last().CheckpointHistory,
                Is.Empty,
                "iteration-local Read data is discarded at return");
        });
    }

    [Test]
    public void ForeverRejectsAnInternallyNonproductiveIteration()
    {
        var error = Assert.Throws<ModelDefinitionException>(() =>
            new ProcessSystemModel<RuntimeState>(new RuntimeState())
                .Process("empty", ctx => ctx.Forever("empty-loop", EmptyIteration))
                .Explore());

        Assert.That(error.Message, Does.Contain("completed without a visible"));
        Assert.That(error.Message, Does.Contain("empty-loop"));
    }

    [Test]
    public void NestedCallsComposeReturnScalarsAndDiscardCompletedFrames()
    {
        var root = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("caller", RootCall)
            .Explore();
        var initial = ((IProcessSchedulerStep)root.StepFunctions.Single())
            .LiveProcesses.Single();

        Assert.That(
            initial.Frames.Select(frame => frame.Kind),
            Is.EqualTo(new[]
            {
                ProcessContinuationFrameKind.Root,
                ProcessContinuationFrameKind.Call,
                ProcessContinuationFrameKind.Call
            }));
        Assert.That(initial.Frames.Last().Name, Is.EqualTo("inner"));
        Assert.That(
            initial.Frames.Last().CapturedLocals.Select(local => (local.Name, local.Value)),
            Is.EqualTo(new[] { ("argument", (object)2) }));

        var chooseOne = root.Edges.Single(edge =>
            Equals(ModelGraph.Transition(edge).Value, 1));
        Assert.That(((RuntimeState)chooseOne.Target.State).A, Is.EqualTo(1));

        var afterInner = ((IProcessSchedulerStep)chooseOne.Target.StepFunctions.Single())
            .LiveProcesses.Single();
        Assert.That(
            afterInner.Frames.Select(frame => frame.Name),
            Is.EqualTo(new[] { "caller", "outer" }),
            "the completed inner frame is discarded on its ChooseStep edge");
        Assert.That(
            afterInner.Frames.Last().CheckpointHistory.Single(entry =>
                entry.Kind == ModelCheckpointKind.Call).Value,
            Is.EqualTo(3));

        var outerStep = chooseOne.Target.Edges.Single();
        Assert.That(((RuntimeState)outerStep.Target.State).B, Is.EqualTo(3));
        var afterOuter = ((IProcessSchedulerStep)outerStep.Target.StepFunctions.Single())
            .LiveProcesses.Single();
        Assert.That(afterOuter.Frames.Select(frame => frame.Name), Is.EqualTo(new[] { "caller" }));
        Assert.That(
            afterOuter.Frames.Single().CheckpointHistory.Single(entry =>
                entry.Kind == ModelCheckpointKind.Call).Value,
            Is.EqualTo(13));

        var terminal = outerStep.Target.Edges.Single().Target;
        Assert.That(((RuntimeState)terminal.State).Slot, Is.EqualTo("13"));
        Assert.That(ModelGraph.LiveRoles(terminal), Does.Not.Contain("caller"));
        Assert.That(ModelGraph.IsComplete(root), Is.True);

        var report = ProcessGraphDiagnostics.Describe(root);
        Assert.That(report.ConfigurationCount, Is.EqualTo(ModelGraph.Reachable(root).Count));
        Assert.That(
            report.ContinuationFormsByRole["caller"]
                .Any(form => form.Contains("call:inner") && form.Contains("argument=2")),
            Is.True);
    }

    [Test]
    public void DirectNestedModelTaskAwaitIsRejectedInFavorOfCall()
    {
        var error = Assert.Throws<ModelDefinitionException>(() =>
            new ProcessSystemModel<RuntimeState>(new RuntimeState())
                .Process("bad", DirectNestedCall)
                .Explore());

        Assert.That(error.Message, Does.Contain("ModelContext.Call"));
    }

    [Test]
    public void CallExceptionsNameTheStructuredFramePath()
    {
        var error = Assert.Throws<StepFunctionApplicationException>(() =>
            new ProcessSystemModel<RuntimeState>(new RuntimeState())
                .Process("root", CallFailingHelper)
                .Explore());

        Assert.Multiple(() =>
        {
            Assert.That(error.InnerException, Is.TypeOf<ModelDefinitionException>());
            Assert.That(error.InnerException.Message, Does.Contain("call 'failing'"));
            Assert.That(error.InnerException.Message, Does.Contain("boom"));
        });
    }

    [Test]
    public void CallResultsAndExplicitArgumentsUseTheScalarWhitelist()
    {
        var resultError = Assert.Throws<ModelDefinitionException>(() =>
            new ProcessSystemModel<RuntimeState>(new RuntimeState())
                .Process("bad-result", CallMutableResult)
                .Explore());
        var argumentError = Assert.Throws<ModelDefinitionException>(() =>
            new ProcessSystemModel<RuntimeState>(new RuntimeState())
                .Process("bad-argument", CallMutableArgument)
                .Explore());

        Assert.Multiple(() =>
        {
            Assert.That(resultError.Message, Does.Contain("unsupported value type"));
            Assert.That(resultError.Message, Does.Contain("mutable-result"));
            Assert.That(argumentError.Message, Does.Contain("unsupported value type"));
            Assert.That(argumentError.Message, Does.Contain("mutable-argument"));
        });
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

    [Test]
    public void ChooseStepBranchesAndMutatesAtomicallyWithoutAChooseConfiguration()
    {
        var root = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("picker", ChooseAndStore)
            .Explore();

        Assert.That(root.Edges, Has.Count.EqualTo(2));
        foreach (var edge in root.Edges)
        {
            var transition = ModelGraph.Transition(edge);
            var selected = (int)transition.Value;
            var state = (RuntimeState)edge.Target.State;

            Assert.Multiple(() =>
            {
                Assert.That(transition.CheckpointKind, Is.EqualTo(ModelCheckpointKind.ChooseStep));
                Assert.That(transition.SemanticAction, Is.EqualTo(RuntimeAction.Pick));
                Assert.That(transition.Subject, Is.EqualTo(selected));
                Assert.That(state.A, Is.EqualTo(selected));
                Assert.That(state.B, Is.EqualTo(selected * 10));
                Assert.That(
                    edge.Target.State.StringRepresentation(),
                    Is.Not.EqualTo(root.State.StringRepresentation()),
                    "selection and mutation are one edge");
            });
        }

        Assert.That(
            root.Edges.Select(edge => (int)ModelGraph.Transition(edge).Value),
            Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(ModelGraph.IsComplete(root), Is.True);
    }

    [Test]
    public void StateDerivedChooseStepAlternativesAreReadWhenScheduled()
    {
        var root = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("picker", async ctx =>
                await ctx.ChooseStep(
                    "current-choice",
                    RuntimeAction.Pick,
                    state => state.Flag ? new[] { 2 } : new[] { 1 },
                    (state, value) => state.A = value))
            .Process("setter", async ctx =>
                await ctx.Step("set-flag", state => state.Flag = true))
            .Explore();
        var afterSet = root.Edges.Single(edge =>
            ModelGraph.Transition(edge).ProcessRole == "setter").Target;
        var pick = afterSet.Edges.Single(edge =>
            ModelGraph.Transition(edge).ProcessRole == "picker");

        Assert.Multiple(() =>
        {
            Assert.That(ModelGraph.Transition(pick).Value, Is.EqualTo(2));
            Assert.That(((RuntimeState)pick.Target.State).A, Is.EqualTo(2));
            Assert.That(
                afterSet.Edges.Select(ModelGraph.Transition)
                    .Where(transition => transition.ProcessRole == "picker")
                    .Select(transition => transition.Value),
                Does.Not.Contain(1));
        });
    }

    [TestCaseSource(nameof(InvalidChooseStepCases))]
    public void ChooseStepValidatesFiniteDistinctScalarChoices(
        object[] choices,
        string expectedMessage)
    {
        async ModelTask Invalid(ModelContext<RuntimeState> ctx)
        {
            await ctx.ChooseStep(
                "invalid",
                RuntimeAction.Pick,
                choices,
                (_, __) => { });
        }

        var error = Assert.Throws<StepFunctionApplicationException>(() =>
            new ProcessSystemModel<RuntimeState>(new RuntimeState())
                .Process("invalid", Invalid)
                .Explore());

        Assert.That(error.InnerException, Is.TypeOf<ModelDefinitionException>());
        Assert.That(error.InnerException.Message, Does.Contain(expectedMessage));
    }

    private static IEnumerable<TestCaseData> InvalidChooseStepCases()
    {
        yield return new TestCaseData(
            new object[0],
            "requires at least one finite choice");
        yield return new TestCaseData(
            new object[] { 1, 1 },
            "contains duplicate value");
        yield return new TestCaseData(
            new object[] { new List<int>() },
            "unsupported value type");
    }

    [Test]
    public void RepeatedActionHasNoContinuationAndNoOpIsACompleteSelfLoop()
    {
        var root = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .RepeatedAction("noop", RuntimeAction.NoOp, _ => { }, subject: "system")
            .Explore();
        var report = ProcessGraphDiagnostics.Describe(root);
        var transition = ModelGraph.Transition(root.Edges.Single());

        Assert.Multiple(() =>
        {
            Assert.That(root.Edges.Single().Target, Is.SameAs(root));
            Assert.That(ModelGraph.LiveRoles(root), Is.Empty);
            Assert.That(transition.ProcessRole, Is.EqualTo("noop"));
            Assert.That(transition.SemanticAction, Is.EqualTo(RuntimeAction.NoOp));
            Assert.That(transition.Subject, Is.EqualTo("system"));
            Assert.That(report.Complete, Is.True);
            Assert.That(report.DomainStateCount, Is.EqualTo(1));
            Assert.That(report.ConfigurationCount, Is.EqualTo(1));
            Assert.That(report.TransitionCount, Is.EqualTo(1));
            Assert.That(
                report.ContinuationFormsByRole["noop"],
                Is.EqualTo(new[] { "<stateless recurring action>" }));
        });
    }

    [Test]
    public void RepeatedActionGuardIsEvaluatedAgainstTheLiveState()
    {
        var root = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .RepeatedAction(
                "pulse",
                state => state.Flag,
                RuntimeAction.Pulse,
                state => state.A = 1 - state.A)
            .Process("setter", async ctx =>
                await ctx.Step("enable", state => state.Flag = true))
            .Explore();

        Assert.That(
            root.Edges.Select(ModelGraph.Transition).Select(transition => transition.ProcessRole),
            Is.EqualTo(new[] { "setter" }));

        var afterSet = root.Edges.Single().Target;
        Assert.That(
            afterSet.Edges.Select(ModelGraph.Transition)
                .Any(transition => transition.ProcessRole == "pulse"),
            Is.True);
    }

    [Test]
    public void FailureDomainRepeatedActionIsUnavailableWhileCrashed()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState());
        var server = model.FailureDomain(
            "server",
            crashEnabled: s => !s.Down,
            onCrash: s => s.Down = true,
            restartEnabled: s => s.Down,
            onRestart: s => s.Down = false);
        server.RepeatedAction(
            "pulse",
            guard: _ => true,
            RuntimeAction.Pulse,
            s => s.A = 1 - s.A,
            subject: "server");

        var root = model.Explore();
        var pulse = root.Edges.Single(edge =>
            ModelGraph.Transition(edge).ProcessRole == "pulse");
        var pulseTransition = ModelGraph.Transition(pulse);
        var crashed = root.Edges.Single(edge =>
            ModelGraph.Transition(edge).Control == ProcessControlKind.Crash).Target;

        Assert.Multiple(() =>
        {
            Assert.That(pulseTransition.Domain, Is.EqualTo("server"));
            Assert.That(pulseTransition.SemanticAction, Is.EqualTo(RuntimeAction.Pulse));
            Assert.That(pulseTransition.Subject, Is.EqualTo("server"));
            Assert.That(
                crashed.Edges.Select(ModelGraph.Transition)
                    .Any(transition => transition.ProcessRole == "pulse"),
                Is.False,
                "domain ownership, not the user guard, disables the action");
        });

        var restarted = crashed.Edges.Single(edge =>
            ModelGraph.Transition(edge).Control == ProcessControlKind.Restart).Target;
        Assert.That(
            restarted.Edges.Select(ModelGraph.Transition)
                .Any(transition => transition.ProcessRole == "pulse"),
            Is.True);
        Assert.That(ModelGraph.IsComplete(root), Is.True);
    }

    [Test]
    public void RepeatedActionRolesMustBeUniqueAcrossAllRegistrationKinds()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState())
            .Process("duplicate", async ctx => await ctx.Step("once", _ => { }))
            .RepeatedAction("duplicate", RuntimeAction.NoOp, _ => { });

        Assert.That(
            () => model.Explore(),
            Throws.TypeOf<ModelDefinitionException>()
                .With.Message.Contains("registered more than once"));
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
    public void ACrashDiscardsNestedStructuredFramesAndRestartCreatesFreshOnes()
    {
        var model = new ProcessSystemModel<RuntimeState>(new RuntimeState());
        var server = model.FailureDomain(
            "server",
            crashEnabled: s => !s.Down,
            onCrash: s => s.Down = true,
            restartEnabled: s => s.Down,
            onRestart: s => s.Down = false);
        server.Process(
            "worker",
            ctx => ctx.Forever("worker-loop", StructuredWorkerIteration));

        var root = model.Explore();
        var initialWorker = ((IProcessSchedulerStep)root.StepFunctions.Single())
            .LiveProcesses.Single(process => process.Role == "worker");
        var crashed = root.Edges.Single(edge =>
            ModelGraph.Transition(edge).Control == ProcessControlKind.Crash).Target;
        var restarted = crashed.Edges.Single(edge =>
            ModelGraph.Transition(edge).Control == ProcessControlKind.Restart).Target;
        var restartedWorker = ((IProcessSchedulerStep)restarted.StepFunctions.Single())
            .LiveProcesses.Single(process => process.Role == "worker");

        Assert.Multiple(() =>
        {
            Assert.That(
                initialWorker.Frames.Select(frame => frame.Kind),
                Is.EqualTo(new[]
                {
                    ProcessContinuationFrameKind.Root,
                    ProcessContinuationFrameKind.ForeverIteration,
                    ProcessContinuationFrameKind.Call
                }));
            Assert.That(ModelGraph.LiveRoles(crashed), Does.Not.Contain("worker"));
            Assert.That(
                restartedWorker.Frames.Select(frame => frame.Kind),
                Is.EqualTo(initialWorker.Frames.Select(frame => frame.Kind)));
            Assert.That(restartedWorker.Frames.SelectMany(frame => frame.CheckpointHistory), Is.Empty);
            Assert.That(ModelGraph.IsComplete(root), Is.True);
        });
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
            Continuation(x.node, "worker") != workerInstance.ContinuationId);

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
        Assert.That(
            Continuation(restartEdge.Target, "worker"),
            Is.EqualTo(workerInstance.ContinuationId));
    }

    private static async ModelTask ToggleIteration(ModelContext<RuntimeState> ctx)
    {
        await ctx.Step("toggle", s => s.A = 1 - s.A);
    }

    private static async ModelTask CopyIteration(ModelContext<RuntimeState> ctx)
    {
        var observed = await ctx.Read("a", s => s.A);
        await ctx.Step("copy-a", s => s.B = observed);
    }

#pragma warning disable CS1998
    private static async ModelTask EmptyIteration(ModelContext<RuntimeState> ctx)
    {
    }
#pragma warning restore CS1998

    private static async ModelTask RootCall(ModelContext<RuntimeState> ctx)
    {
        var value = await ctx.Call("outer", OuterCall);
        await ctx.Step("store-root", s => s.Slot = value.ToString());
    }

    private static async ModelTask<int> OuterCall(ModelContext<RuntimeState> ctx)
    {
        var value = await ctx.Call("inner", 2, InnerCall);
        await ctx.Step("store-outer", s => s.B = value);
        return value + 10;
    }

    private static async ModelTask<int> InnerCall(
        ModelContext<RuntimeState> ctx,
        int offset)
    {
        var selected = await ctx.ChooseStep(
            "inner-choice",
            RuntimeAction.Pick,
            new[] { 1, 2 },
            (state, value) => state.A = value,
            subject: value => value);
        return selected + offset;
    }

    private static async ModelTask DirectNestedCall(ModelContext<RuntimeState> ctx)
    {
        // This fixture deliberately exercises the runtime backstop behind ACC1001.
#pragma warning disable ACC1001
        await NestedStep(ctx);
#pragma warning restore ACC1001
    }

    private static async ModelTask NestedStep(ModelContext<RuntimeState> ctx)
    {
        await ctx.Step("nested", s => s.A++);
    }

    private static async ModelTask CallFailingHelper(ModelContext<RuntimeState> ctx)
    {
        await ctx.Call("failing", FailingHelper);
    }

    private static async ModelTask FailingHelper(ModelContext<RuntimeState> ctx)
    {
        await ctx.Step("before-failure", s => s.A++);
        throw new System.InvalidOperationException("boom");
    }

    private static async ModelTask CallMutableResult(ModelContext<RuntimeState> ctx)
    {
        // These fixtures deliberately exercise the runtime scalar validation behind ACC1004.
#pragma warning disable ACC1004
        await ctx.Call("mutable-result", MutableResult);
#pragma warning restore ACC1004
    }

#pragma warning disable CS1998
    private static async ModelTask<List<int>> MutableResult(ModelContext<RuntimeState> ctx)
    {
        return new List<int> { 1 };
    }
#pragma warning restore CS1998

    private static async ModelTask CallMutableArgument(ModelContext<RuntimeState> ctx)
    {
#pragma warning disable ACC1004
        await ctx.Call(
            "mutable-argument",
            new List<int> { 1 },
            MutableArgument);
#pragma warning restore ACC1004
    }

    private static async ModelTask MutableArgument(
        ModelContext<RuntimeState> ctx,
        List<int> value)
    {
        await ctx.Step("unreachable", _ => { });
    }

    private static async ModelTask ChooseAndStore(ModelContext<RuntimeState> ctx)
    {
        await ctx.ChooseStep(
            "pick",
            RuntimeAction.Pick,
            new[] { 1, 2 },
            (state, value) =>
            {
                state.A = value;
                state.B = value * 10;
            },
            subject: value => value);
        await ctx.Step("finish", s => s.Flag = true);
    }

    private static async ModelTask StructuredWorkerIteration(ModelContext<RuntimeState> ctx)
    {
        await ctx.Call("worker-helper", StructuredWorkerHelper);
    }

    private static async ModelTask StructuredWorkerHelper(ModelContext<RuntimeState> ctx)
    {
        await ctx.Step("worker-toggle", s => s.A = 1 - s.A);
    }

    private static ModelTask Worker(ModelContext<RuntimeState> ctx)
        => ctx.Forever("worker-loop", WorkerIteration);

    private static async ModelTask WorkerIteration(ModelContext<RuntimeState> ctx)
    {
        await ctx.Step("w1", s => s.A = 1);
        await ctx.Step("w2", s => s.A = 0);
    }

    private static ModelTask Client(ModelContext<RuntimeState> ctx)
        => ctx.Forever("client-loop", ClientIteration);

    private static async ModelTask ClientIteration(ModelContext<RuntimeState> ctx)
    {
        await ctx.Step("c1", s => s.B = s.B == 0 ? 1 : 0);
    }

    private static string Continuation(StateGraphNode node, string role)
        => ((IProcessSchedulerStep)node.StepFunctions.Single())
            .LiveProcesses
            .Single(process => process.Role == role)
            .ContinuationId;
}
