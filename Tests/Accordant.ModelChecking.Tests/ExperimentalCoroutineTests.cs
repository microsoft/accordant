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
    public void DefinitionErrorsAreClearForRepeatedCheckpointsAndExternalAwaits()
    {
        async ModelTask Repeated(ModelContext<CounterState> context)
        {
            await context.Read("same", _ => 1);
            await context.Read("same", _ => 2);
        }

        async ModelTask ExternalAwait(ModelContext<CounterState> context)
        {
            await Task.Yield();
            await context.Step("unreachable", _ => { });
        }

        var repeated = Assert.Throws<ModelDefinitionException>(
            () => CoroutineModel.Explore("repeated", new CounterState(), Repeated));
        Assert.That(repeated.Message, Does.Contain("reached more than once"));

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
}
