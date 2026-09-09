// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Accordant;
using NUnit.Framework;

[TestFixture]
public class StateGraphValidationTests
{
    private sealed class TestState : State
    {
        public int Value { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
        {
            clonedMap[this] = new TestState { Value = Value };
        }

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute)
        {
            return Value.ToString(CultureInfo.InvariantCulture);
        }

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class TestStep : BaseStepFunction
    {
        private readonly string id;
        private readonly Func<IState, IList<StepResult>> apply;

        public TestStep(string id, Func<IState, IList<StepResult>> apply = null)
        {
            this.id = id;
            this.apply = apply ?? (state => null);
        }

        public override string StepFunctionId => id;

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            return apply(state);
        }
    }

    private static StateGraphNode Explore(
        IList<IStepFunction> steps,
        int maxDepth = 1)
    {
        return StateGraph.ExploreStateGraph(
            steps,
            new TestState(),
            maxDepth: maxDepth);
    }

    [Test]
    public void ExploreStateGraph_RejectsInvalidArgumentsAtTheBoundary()
    {
        Assert.That(
            Assert.Throws<ArgumentNullException>(() =>
                StateGraph.ExploreStateGraph(null, new TestState())),
            Has.Property(nameof(ArgumentNullException.ParamName)).EqualTo("steps"));

        Assert.That(
            Assert.Throws<ArgumentNullException>(() =>
                StateGraph.ExploreStateGraph(Array.Empty<IStepFunction>(), null)),
            Has.Property(nameof(ArgumentNullException.ParamName)).EqualTo("startingState"));

        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Explore(Array.Empty<IStepFunction>(), maxDepth: -2)),
            Has.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("maxDepth"));

        Assert.Throws<ArgumentException>(() =>
            Explore(new IStepFunction[] { null }));

        Assert.Throws<ArgumentException>(() =>
            Explore(new IStepFunction[] { new TestStep(string.Empty) }));

        var duplicateIdSteps = new IStepFunction[]
        {
            new TestStep("duplicate"),
            new TestStep("duplicate")
        };

        Assert.Throws<ArgumentException>(() => Explore(duplicateIdSteps));
    }

    [Test]
    public void StateGraphNode_SnapshotsCollectionsAndExposesReadOnlyProperties()
    {
        var step = new TestStep("step");
        var steps = new List<IStepFunction> { step };
        var root = Explore(steps);

        steps.Clear();

        Assert.That(root.StepFunctions, Has.Count.EqualTo(1));
        Assert.That(root.StepFunctions[0], Is.SameAs(step));
        Assert.That(typeof(StateGraphNode).GetProperty(nameof(StateGraphNode.State)).CanWrite, Is.False);
        Assert.That(typeof(StateGraphNode).GetProperty(nameof(StateGraphNode.StepFunctions)).CanWrite, Is.False);
        Assert.That(typeof(StateGraphNode).GetProperty(nameof(StateGraphNode.Edges)).CanWrite, Is.False);
        Assert.That(typeof(StateGraphEdge).GetProperty(nameof(StateGraphEdge.Target)).CanWrite, Is.False);
        Assert.That(typeof(StateGraphEdge).GetProperty(nameof(StateGraphEdge.StepFunction)).CanWrite, Is.False);
        Assert.That(typeof(StateGraphEdge).GetProperty(nameof(StateGraphEdge.Metadata)).CanWrite, Is.False);
    }

    [Test]
    public void ExploreStateGraph_RejectsNullStepResults()
    {
        var step = new TestStep("null-result", _ => new List<StepResult> { null });

        var exception = Assert.Throws<StepFunctionApplicationException>(() => Explore(new[] { step }));

        Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerException.Message, Does.Contain("null StepResult"));
    }

    [Test]
    public void ExploreStateGraph_RejectsNullResultStates()
    {
        var step = new TestStep("null-state", _ => new List<StepResult>
        {
            new StepResult()
        });

        var exception = Assert.Throws<StepFunctionApplicationException>(() => Explore(new[] { step }));

        Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerException.Message, Does.Contain("null State"));
    }

    [Test]
    public void ExploreStateGraph_RejectsNullSuccessorStepFunctions()
    {
        var step = new TestStep("null-successor", state => new List<StepResult>
        {
            new StepResult
            {
                State = state,
                StepFunctions = new IStepFunction[] { null }
            }
        });

        var exception = Assert.Throws<StepFunctionApplicationException>(() => Explore(new[] { step }));

        Assert.That(exception.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(exception.InnerException.Message, Does.Contain("null step functions"));
    }

    [Test]
    public void ExploreStateGraph_RejectsDuplicateSuccessorStepFunctions()
    {
        var otherStep = new TestStep("other");
        var step = new TestStep("source", state => new List<StepResult>
        {
            new StepResult
            {
                State = state,
                StepFunctions = new IStepFunction[] { otherStep }
            }
        });

        var exception = Assert.Throws<StepFunctionApplicationException>(() =>
            Explore(new IStepFunction[] { step, otherStep }));

        Assert.That(exception.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(exception.InnerException.Message, Does.Contain("duplicate StepFunctionId"));
    }

    [Test]
    public void LazyExpansion_RethrowsTheOriginalFailureInsteadOfReturningPartialEdges()
    {
        var step = new TestStep("failing", _ =>
            throw new InvalidOperationException("expected lazy expansion failure"));
        var root = StateGraph.ExploreStateGraph(
            new IStepFunction[] { step },
            new TestState(),
            lazy: true);

        var firstException = Assert.Throws<StepFunctionApplicationException>(() => _ = root.Edges);
        var secondException = Assert.Throws<StepFunctionApplicationException>(() => _ = root.Edges);

        Assert.That(secondException, Is.SameAs(firstException));
        Assert.That(firstException.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(firstException.InnerException.Message, Does.Contain("expected lazy expansion failure"));
    }

    [Test]
    public void ContractStepFunction_RejectsValidVerificationWithoutAStateProfile()
    {
        var contract = new ContractStepFunction(
            request: null,
            observedResponse: null,
            verify: (_, _, _) => (true, (StateProfile)null));

        var exception = Assert.Throws<InvalidOperationException>(() => contract.Apply(
            new TestState(),
            Array.Empty<(IStepFunction, StateGraphNode)>()));

        Assert.That(exception.Message, Does.Contain("non-null StateProfile"));
    }

    [Test]
    public void ContractStepFunction_RejectsValidVerificationWithNoOutcomes()
    {
        var emptyProfile = new StateProfile(
            new List<(IState, IList<IStepFunction>)>());
        var contract = new ContractStepFunction(
            request: null,
            observedResponse: null,
            verify: (_, _, _) => (true, emptyProfile));

        var exception = Assert.Throws<InvalidOperationException>(() => contract.Apply(
            new TestState(),
            Array.Empty<(IStepFunction, StateGraphNode)>()));

        Assert.That(exception.Message, Does.Contain("at least one state outcome"));
    }

    [Test]
    public void ContractStepFunction_RejectsInvalidPredecessorIds()
    {
        Assert.Throws<ArgumentException>(() => new ContractStepFunction(
            request: null,
            observedResponse: null,
            verify: (_, _, _) => (false, (StateProfile)null),
            predecessorIds: new[] { "" }));

        var contract = new ContractStepFunction(
            request: null,
            observedResponse: null,
            verify: (_, _, _) => (false, (StateProfile)null));

        Assert.Throws<ArgumentException>(() =>
            contract.SetPredecessorIds(new[] { "duplicate", "duplicate" }));
    }
}
