// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

[TestFixture]
public class ExperimentalCoroutineTests
{
    [Test]
    public void ReadIsReplayedAndStepsMutateOnlyWhenTheirEdgesAreSelected()
    {
        var firstCalls = 0;
        var secondCalls = 0;

        async ModelTask Workflow(ModelContext<CounterState> context)
        {
            var observed = await context.Read("initial-count", state => state.Count);
            await context.Step("first", state =>
            {
                firstCalls++;
                state.Count = observed + 1;
            });
            await context.Step("second", state =>
            {
                secondCalls++;
                state.Count++;
            });
        }

        var root = CoroutineModel.Explore("replay", new CounterState { Count = 5 }, Workflow);
        var first = root.Edges.Single();
        var second = first.Target.Edges.Single();
        var terminal = second.Target;

        Assert.That(((CounterState)terminal.State).Count, Is.EqualTo(7));
        Assert.That(firstCalls, Is.EqualTo(1));
        Assert.That(secondCalls, Is.EqualTo(1));
        Assert.That(terminal.StepFunctions, Is.Empty);
        Assert.That(terminal.Edges, Is.Empty);
    }

    [Test]
    public void ChooseCreatesFiniteVisibleBranchesAndReadDoesNot()
    {
        var root = CoroutineModel.Explore("branch", new CounterState { Count = 3 }, BranchingWorkflow);

        Assert.That(root.Edges, Has.Count.EqualTo(2));
        Assert.That(
            root.Edges.Select(edge => ((CoroutineTransition)edge.Metadata).ToString()),
            Is.EquivalentTo(new[] { "choose:delta=1", "choose:delta=2" }));

        var terminalCounts = root.Edges
            .Select(edge => edge.Target.Edges.Single().Target)
            .Select(node => ((CounterState)node.State).Count);
        Assert.That(terminalCounts, Is.EquivalentTo(new[] { 4, 5 }));
    }

    [Test]
    public void VisibleCheckpointMetadataAndActionsExposeTypedReplayPrefixes()
    {
        var root = CoroutineModel.Explore("branch", new CounterState(), BranchingWorkflow);
        var choose = (CoroutineTransition)root.Edges.First().Metadata;
        var afterChoose = root.Edges.First().Target;
        var claimAction = (ICoroutineCheckpointStep)afterChoose.StepFunctions.Single();
        var step = (CoroutineTransition)afterChoose.Edges.Single().Metadata;

        Assert.That(
            choose.ReplayPrefix.Select(entry => (entry.Kind, entry.Name, entry.Value)),
            Is.EqualTo(new[] { (ModelCheckpointKind.Read, "count", (object)0) }));
        Assert.That(choose.Kind, Is.EqualTo(ModelCheckpointKind.Choose));
        Assert.That(claimAction.CheckpointKind, Is.EqualTo(ModelCheckpointKind.Step));
        Assert.That(claimAction.CheckpointName, Is.EqualTo("apply-delta"));
        Assert.That(
            claimAction.ReplayPrefix.Select(entry => (entry.Kind, entry.Name, entry.Value)),
            Is.EqualTo(new[] { (ModelCheckpointKind.Read, "count", (object)0), (ModelCheckpointKind.Choose, "delta", (object)1) }));
        Assert.That(
            step.ReplayPrefix.Select(entry => (entry.Kind, entry.Name, entry.Value)),
            Is.EqualTo(claimAction.ReplayPrefix.Select(entry => (entry.Kind, entry.Name, entry.Value))));
    }

    [Test]
    public void ContextDoesNotExposeStateAndMutableReplayValuesAreRejected()
    {
        Assert.That(typeof(ModelContext<CounterState>).GetProperty("State"), Is.Null);

        async ModelTask BadValue(ModelContext<CounterState> context)
        {
            await context.Read("bad", _ => new List<int> { 1 });
        }

        var error = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore("bad-value", new CounterState(), BadValue));
        Assert.That(error.Message, Does.Contain("unsupported value type"));
    }

    [Test]
    public void GraphIdentityIsStableAndExistingPropertyCheckingWorks()
    {
        var first = CoroutineModel.Explore("branch", new CounterState { Count = 0 }, BranchingWorkflow);
        var second = CoroutineModel.Explore("branch", new CounterState { Count = 0 }, BranchingWorkflow);

        Assert.That(first.GetNodeFingerprint(), Is.EqualTo(second.GetNodeFingerprint()));
        Assert.That(
            first.Edges.Select(edge => edge.StepFunction.StepFunctionId),
            Is.EqualTo(second.Edges.Select(edge => edge.StepFunction.StepFunctionId)));

        var formula = Formula.For<CounterState>();
        var completed = formula.Observe(state => state.Count > 0, "Completed");
        Assert.That(first.Check(formula.Eventually(completed)).Valid, Is.True);
    }

    [Test]
    public void ExistingTemporalRefinementAcceptsCoroutineGraphs()
    {
        var concrete = CoroutineModel.Explore("branch", new CounterState(), BranchingWorkflow);
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AbstractLoopStep() },
            new AbstractState());

        var result = Microsoft.Accordant.ModelChecking.Refinement
            .Between<CounterState, AbstractState>(concrete, abstraction)
            .Map(_ => new AbstractState())
            .MapTransition(_ => AbstractResponse.Step<AbstractLoopStep>())
            .CheckTemporal();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void RepeatedCheckpointNamesGrowTheNaiveTapeUntilTheGraphDepthBound()
    {
        async ModelTask Repeated(ModelContext<CounterState> context)
        {
            while (true)
            {
                await context.Step("toggle", state => state.Count = 1 - state.Count);
            }
        }

        var root = CoroutineModel.Explore(
            "repeated",
            new CounterState(),
            Repeated,
            maxDepth: 3);
        var nodes = Reachable(root).ToArray();
        var activeTapes = nodes
            .Where(node => node.StepFunctions.Count != 0)
            .Select(node => ((ICoroutineCheckpointStep)node.StepFunctions.Single()).ReplayPrefix.Count)
            .ToArray();
        var formula = Formula.For<CounterState>();
        var neverReached = formula.Observe(state => state.Count == 99, "never-reached");

        Assert.That(nodes, Has.Length.EqualTo(3));
        Assert.That(root.GetNodeFingerprint(), Is.Not.EqualTo(nodes[2].GetNodeFingerprint()),
            "the domain state and Step location repeat, but the full replay tape changes identity");
        Assert.That(activeTapes, Is.EquivalentTo(new[] { 0, 1, 2 }));
        Assert.That(nodes.Last().IsDepthFrontier, Is.True);
        Assert.That(
            root.Check(formula.Eventually(neverReached)).Status,
            Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
    }

    [Test]
    public void LoopRebasesTheTapeAndCreatesAnOrdinaryGraphCycle()
    {
        var root = CoroutineModel.Explore("loop", new CounterState(), LoopingWorkflow);
        var nodes = Reachable(root).ToArray();
        var formula = Formula.For<CounterState>().AllowStutterSensitiveFormulas();
        var toggleEnabled = formula.Enabled(step =>
            step is ICoroutineCheckpointStep checkpoint &&
            checkpoint.CheckpointKind == ModelCheckpointKind.Step &&
            checkpoint.CheckpointName == "toggle");

        Assert.That(nodes, Has.Length.EqualTo(2));
        Assert.That(nodes.All(node => node.Edges.Single().Target != null), Is.True);
        Assert.That(nodes.SelectMany(node => node.Edges).Select(edge =>
            ((CoroutineTransition)edge.Metadata).Kind), Is.All.EqualTo(ModelCheckpointKind.Step));
        Assert.That(
            nodes.SelectMany(node => node.Edges)
                .Select(edge => ((CoroutineTransition)edge.Metadata).CheckpointName),
            Is.All.EqualTo("toggle"));
        Assert.That(
            root.Check(formula.Always(toggleEnabled), fairness: Fairness.Weak(
                step => step is ICoroutineCheckpointStep)).Status,
            Is.EqualTo(PropertyCheckingStatus.Holds));
        Assert.That(
            root.Check(formula.Always(toggleEnabled), fairness: Fairness.Strong(
                step => step is ICoroutineCheckpointStep)).Status,
            Is.EqualTo(PropertyCheckingStatus.Holds));
    }

    [Test]
    public void LoopPersistentValuePreventsAMergeUntilTheValueRepeats()
    {
        var root = CoroutineModel.Explore("persistent-loop", new CounterState(), PersistentLoopWorkflow);
        var first = root;
        var second = first.Edges.Single().Target;
        var third = second.Edges.Single().Target;

        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(third, Is.Not.SameAs(first));
        Assert.That(third.Edges.Single().Target, Is.SameAs(first));
        Assert.That(
            new[] { first, second, third }
                .Select(node => ((ICoroutineCheckpointStep)node.StepFunctions.Single())
                .ReplayPrefix.Single(entry => entry.Kind == ModelCheckpointKind.Loop)
                .Value),
            Is.EqualTo(new object[] { 0, 1, 2 }));
    }

    [Test]
    public void LoopPersistentValuesUseTheSameImmutableScalarRules()
    {
        async ModelTask MutablePersistentValue(ModelContext<CounterState> context)
        {
            await context.LoopState("iteration", new List<int> { 1 });
        }

        var error = Assert.Throws<ModelDefinitionException>(() =>
            CoroutineModel.Explore(
                "mutable-loop-value",
                new CounterState(),
                MutablePersistentValue));

        Assert.That(error.Message, Does.Contain("unsupported value type"));
    }

    [Test]
    public void MultipleLoopBoundariesAreRejected()
    {
        async ModelTask Ambiguous(ModelContext<CounterState> context)
        {
            await context.Loop("iteration");
            await context.Step("tick", _ => { });
            await context.Loop("iteration");
        }

        var error = Assert.Throws<StepFunctionApplicationException>(() =>
            CoroutineModel.Explore("ambiguous-loop", new CounterState(), Ambiguous));

        Assert.That(error.InnerException, Is.TypeOf<ModelDefinitionException>());
        Assert.That(error.InnerException.Message, Does.Contain("support one Loop boundary"));
    }

    [Test]
    public void LoopMustBeTheFirstAccordantCheckpoint()
    {
        async ModelTask Misplaced(ModelContext<CounterState> context)
        {
            await context.Read("count", state => state.Count);
            await context.Loop("iteration");
            await context.Step("tick", _ => { });
        }

        var error = Assert.Throws<ModelDefinitionException>(() =>
            CoroutineModel.Explore("misplaced-loop", new CounterState(), Misplaced));

        Assert.That(error.Message, Does.Contain("first Accordant checkpoint"));
    }

    [Test]
    public void LoopSupportsStringPersistentValues()
    {
        async ModelTask StringPhase(ModelContext<CounterState> context)
        {
            var phase = "zero";
            while (true)
            {
                phase = await context.LoopState("iteration", phase);
                await context.Step("tick", _ => { });
                phase = phase == "zero" ? "one" : "zero";
            }
        }

        var root = CoroutineModel.Explore("string-loop", new CounterState(), StringPhase);
        var first = (ICoroutineCheckpointStep)root.StepFunctions.Single();
        var second = (ICoroutineCheckpointStep)root.Edges.Single().Target.StepFunctions.Single();

        Assert.That(first.ReplayPrefix.Single().Value, Is.EqualTo("zero"));
        Assert.That(second.ReplayPrefix.Single().Value, Is.EqualTo("one"));
    }

    [Test]
    public void LoopIsInternalAndStepTransitionsRefineAnOrdinaryToggleGraph()
    {
        var concrete = CoroutineModel.Explore("loop-refinement", new CounterState(), LoopingWorkflow);
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new ToggleStep() },
            new CounterState());

        var result = Microsoft.Accordant.ModelChecking.Refinement
            .Between<CounterState, CounterState>(concrete, abstraction)
            .Map(state => new CounterState { Count = state.Count })
            .MapTransition(transition =>
            {
                var checkpoint = (CoroutineTransition)transition.Metadata;
                return checkpoint.Kind == ModelCheckpointKind.Step &&
                    checkpoint.CheckpointName == "toggle"
                    ? AbstractResponse.Step(step => step.StepFunctionId == "toggle")
                    : throw new AssertionException("Loop must not create a visible transition.");
            })
            .CheckTemporal(
                concreteFairness: Fairness.Weak(step => step is ICoroutineCheckpointStep),
                abstractFairness: Fairness.Weak(step => step.StepFunctionId == "toggle"));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void DefinitionErrorsAreClearForExternalAwaits()
    {
        async ModelTask ExternalAwait(ModelContext<CounterState> context)
        {
            await Task.Yield();
            await context.Step("unreachable", _ => { });
        }

        var external = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore("external", new CounterState(), ExternalAwait));
        Assert.That(external.Message, Does.Contain("only supports incomplete awaits"));
    }

    [Test]
    public void CapturedStateCannotBeMutatedOutsideAStep()
    {
        var initial = new CounterState();

        async ModelTask MutatingWorkflow(ModelContext<CounterState> context)
        {
            initial.Count = 42;
            await context.Step("visible", _ => { });
        }

        var previous = State.EnableFreezeValidation;
        State.EnableFreezeValidation = false;
        try
        {
            var error = Assert.Throws<ModelDefinitionException>(
                () => CoroutineModel.Explore(
                    "captured-mutation",
                    initial,
                    MutatingWorkflow));
            Assert.That(
                error.Message,
                Does.Contain("outside ModelContext.Step"));
        }
        finally
        {
            State.EnableFreezeValidation = previous;
        }
    }

    [Test]
    public void ScalarIdentityPreservesDateTimeKind()
    {
        var ticks = new DateTime(2024, 1, 1).Ticks;

        async ModelTask Dates(ModelContext<CounterState> context)
        {
            var selected = await context.Choose(
                "when",
                new[]
                {
                    new DateTime(ticks, DateTimeKind.Utc),
                    new DateTime(ticks, DateTimeKind.Local)
                });
            await context.Step(
                "record-kind",
                state => state.Count = (int)selected.Kind);
        }

        var root = CoroutineModel.Explore(
            "date-kinds",
            new CounterState(),
            Dates);

        Assert.That(root.Edges, Has.Count.EqualTo(2));
        Assert.That(
            root.Edges
                .Select(edge => edge.Target.Edges.Single().Target)
                .Select(node => ((CounterState)node.State).Count),
            Is.EquivalentTo(new[]
            {
                (int)DateTimeKind.Utc,
                (int)DateTimeKind.Local
            }));
    }

    private static async ModelTask BranchingWorkflow(ModelContext<CounterState> context)
    {
        var count = await context.Read("count", state => state.Count);
        var delta = await context.Choose("delta", state => new[] { 1, 2 });
        await context.Step("apply-delta", state => state.Count = count + delta);
    }

    private static async ModelTask LoopingWorkflow(ModelContext<CounterState> context)
    {
        while (true)
        {
            await context.Loop("iteration");
            await context.Step("toggle", state => state.Count = 1 - state.Count);
        }
    }

    private static async ModelTask PersistentLoopWorkflow(ModelContext<CounterState> context)
    {
        var phase = 0;
        while (true)
        {
            phase = await context.LoopState("iteration", phase);
            await context.Step("tick", _ => { });
            phase = (phase + 1) % 3;
        }
    }

    private static IEnumerable<StateGraphNode> Reachable(StateGraphNode root)
    {
        var seen = new HashSet<string>();
        var pending = new Stack<StateGraphNode>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node.GetNodeFingerprint()))
            {
                continue;
            }

            yield return node;
            foreach (var edge in node.Edges)
            {
                pending.Push(edge.Target);
            }
        }
    }

    private sealed class CounterState : State
    {
        public int Count { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new CounterState { Count = Count };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute)
            => $"Count={Count}";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AbstractState : State
    {
        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new AbstractState();

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute)
            => "abstract";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AbstractLoopStep : BaseStepFunction
    {
        public override string StepFunctionId => "abstract-loop";

        protected override IList<StepResult> ApplyInternal(IState state)
            => new[]
            {
                new StepResult
                {
                    State = state,
                    StepFunctions = new IStepFunction[] { this }
                }
            };
    }

    private sealed class ToggleStep : BaseStepFunction
    {
        public override string StepFunctionId => "toggle";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var next = (CounterState)state.Clone();
            next.Count = 1 - next.Count;
            return new[]
            {
                new StepResult
                {
                    State = next,
                    StepFunctions = new IStepFunction[] { this }
                }
            };
        }
    }
}
