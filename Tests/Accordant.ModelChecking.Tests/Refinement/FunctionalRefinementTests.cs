namespace Accordant.ModelChecking.Tests.Refinement;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

[TestFixture]
public class FunctionalRefinementTests
{
    private sealed class ConcreteState : State
    {
        public int Value { get; }

        public ConcreteState(int value)
        {
            Value = value;
        }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new ConcreteState(Value);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Concrete({Value})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AbstractState : State
    {
        public int Value { get; }

        public AbstractState(int value)
        {
            Value = value;
        }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new AbstractState(Value);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Abstract({Value})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class LabelStep : IStepFunction
    {
        public LabelStep(string id)
        {
            StepFunctionId = id;
        }

        public string StepFunctionId { get; }

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
            => null;
    }

    private sealed class AdvanceConcreteStep : IStepFunction
    {
        public string StepFunctionId => "advance-concrete";

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            var current = (ConcreteState)state;
            return new[]
            {
                new StepResult
                {
                    State = new ConcreteState(current.Value + 1),
                    StepFunctions = new IStepFunction[] { this }
                }
            };
        }
    }

    private sealed class AdvanceAbstractStep : IStepFunction
    {
        public string StepFunctionId => "advance-abstract";

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            var current = (AbstractState)state;
            return new[]
            {
                new StepResult
                {
                    State = new AbstractState(current.Value + 1),
                    StepFunctions = new IStepFunction[] { this }
                }
            };
        }
    }

    [Test]
    public void MatchingInitialStatesWithoutTransitions_Refine()
    {
        var concrete = ConcreteNode(0);
        var abstraction = AbstractNode(0);

        var result = Check(concrete, abstraction);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(result.Valid, Is.True);
        Assert.That(result.Trace, Is.Null);
    }

    [Test]
    public void InitialStateMismatch_ReturnsFiniteCounterexample()
    {
        var concrete = ConcreteNode(0);
        var abstraction = AbstractNode(1);

        var result = Check(concrete, abstraction);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.InitialStateMismatch));
        Assert.That(result.Trace, Has.Count.EqualTo(1));
        Assert.That(result.Trace[0].ConcreteNode, Is.SameAs(concrete));
        Assert.That(result.Trace[0].AbstractCandidates, Is.Empty);
    }

    [Test]
    public void ConcreteTransitionMatchingAbstractTransition_Refines()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c1, "concrete");

        var a0 = AbstractNode(0);
        var a1 = AbstractNode(1);
        AddEdge(a0, a1, "abstract");

        Assert.That(Check(c0, a0).Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void SeveralConcreteStepsMayMapToAbstractStutter()
    {
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(0, "c1");
        var c2 = ConcreteNode(0, "c2");
        var c3 = ConcreteNode(1, "c3");
        AddEdge(c0, c1, "internal-1");
        AddEdge(c1, c2, "internal-2");
        AddEdge(c2, c3, "visible");

        var a0 = AbstractNode(0);
        var a1 = AbstractNode(1);
        AddEdge(a0, a1, "visible");

        Assert.That(Check(c0, a0).Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void MissingAbstractTransition_ReturnsTransitionMismatch()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        var metadata = new object();
        AddEdge(c0, c1, "missing", metadata);

        var result = Check(c0, AbstractNode(0));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(result.Trace, Has.Count.EqualTo(2));
        Assert.That(result.Trace[1].ConcreteStepFunction.StepFunctionId, Is.EqualTo("missing"));
        Assert.That(result.Trace[1].ConcreteEdgeMetadata, Is.SameAs(metadata));
        Assert.That(result.Trace[1].AbstractCandidates, Is.Empty);
    }

    [Test]
    public void EqualStateAbstractEdgeCanAdvanceConfiguration()
    {
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(0, "c1");
        var c2 = ConcreteNode(1, "c2");
        AddEdge(c0, c1, "prepare");
        AddEdge(c1, c2, "finish");

        var a0 = AbstractNode(0, "before");
        var a1 = AbstractNode(0, "after");
        var a2 = AbstractNode(1, "done");
        AddEdge(a0, a1, "prepare");
        AddEdge(a1, a2, "finish");

        Assert.That(Check(c0, a0).Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void EqualStateConfigurationsAreNotStitchedAcrossPaths()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        var c2 = ConcreteNode(2);
        AddEdge(c0, c1, "to-one");
        AddEdge(c1, c2, "to-two");

        var a0 = AbstractNode(0, "root");
        var deadOne = AbstractNode(1, "dead-one");
        AddEdge(a0, deadOne, "to-dead-one");

        var detour = AbstractNode(9, "detour");
        var liveOne = AbstractNode(1, "live-one");
        var a2 = AbstractNode(2, "two");
        AddEdge(a0, detour, "detour");
        AddEdge(detour, liveOne, "to-live-one");
        AddEdge(liveOne, a2, "to-two");

        var result = Check(c0, a0);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(result.Trace, Has.Count.EqualTo(3));
    }

    [Test]
    public void ConcreteMergeIsCheckedForEachAbstractCandidateSet()
    {
        var c0 = ConcreteNode(0, "root");
        var cX = ConcreteNode(10, "x");
        var cY = ConcreteNode(20, "y");
        var merge = ConcreteNode(30, "merge");
        var goal = ConcreteNode(40, "goal");
        AddEdge(c0, cY, "good-first");
        AddEdge(c0, cX, "bad-second");
        AddEdge(cX, merge, "x-merge");
        AddEdge(cY, merge, "y-merge");
        AddEdge(merge, goal, "finish");

        var a0 = AbstractNode(0, "root");
        var aX = AbstractNode(10, "x");
        var aY = AbstractNode(20, "y");
        var badMerge = AbstractNode(30, "bad-merge");
        var goodMerge = AbstractNode(30, "good-merge");
        var aGoal = AbstractNode(40, "goal");
        AddEdge(a0, aX, "x");
        AddEdge(a0, aY, "y");
        AddEdge(aX, badMerge, "x-merge");
        AddEdge(aY, goodMerge, "y-merge");
        AddEdge(goodMerge, aGoal, "finish");

        var result = Check(c0, a0);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(
            result.Trace.Select(item => ((ConcreteState)item.ConcreteNode.State).Value),
            Is.EqualTo(new[] { 0, 10, 30, 40 }));
    }

    [Test]
    public void AlignedCyclesTerminateAndRefine()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c1, "up");
        AddEdge(c1, c0, "down");

        var a0 = AbstractNode(0);
        var a1 = AbstractNode(1);
        AddEdge(a0, a1, "up");
        AddEdge(a1, a0, "down");

        Assert.That(Check(c0, a0).Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ConcreteConstructionFrontier_IsInconclusive(bool lazy)
    {
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AdvanceConcreteStep() },
            new ConcreteState(0),
            maxDepth: 1,
            lazy: lazy);
        var abstraction = AbstractNode(0);

        var result = Check(concrete, abstraction);

        Assert.That(concrete.IsDepthFrontier, Is.True);
        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
        Assert.That(result.Valid, Is.Null);
        Assert.That(result.Trace, Has.Count.EqualTo(1));
    }

    [Test]
    public void AbstractConstructionFrontierAsOnlyPossibleMatch_IsInconclusive()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c1, "advance");

        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AdvanceAbstractStep() },
            new AbstractState(0),
            maxDepth: 1);

        var result = Check(c0, abstraction);

        Assert.That(abstraction.IsDepthFrontier, Is.True);
        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
        Assert.That(result.Trace, Has.Count.EqualTo(2));
    }

    [Test]
    public void KnownStutterMatchAtAbstractFrontier_RemainsConclusive()
    {
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(0, "c1");
        AddEdge(c0, c1, "internal");

        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AdvanceAbstractStep() },
            new AbstractState(0),
            maxDepth: 1);

        Assert.That(Check(c0, abstraction).Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void UnknownFrontierAlternativeSurvivesAlongsideKnownCandidate()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        var c2 = ConcreteNode(2);
        var c3 = ConcreteNode(3);
        AddEdge(c0, c1, "one");
        AddEdge(c1, c2, "two");
        AddEdge(c2, c3, "three");

        var a0 = AbstractNode(0);
        var known1 = AbstractNode(1, "known-one");
        var known2 = AbstractNode(2, "known-two");
        AddEdge(a0, known1, "known-one");
        AddEdge(known1, known2, "known-two");

        var frontier1 = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AdvanceAbstractStep() },
            new AbstractState(1),
            maxDepth: 1);
        AddEdge(a0, frontier1, "frontier-one");

        var result = Check(c0, a0);

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
        Assert.That(result.Trace, Has.Count.EqualTo(4));
    }

    [Test]
    public void DefinitiveFailureOverridesFrontierUncertainty()
    {
        var c0 = ConcreteNode(0);
        var uncertain = ConcreteNode(1);
        var uncertainNext = ConcreteNode(2);
        var failureStart = ConcreteNode(10);
        var failureEnd = ConcreteNode(11);
        AddEdge(c0, uncertain, "uncertain-branch");
        AddEdge(c0, failureStart, "failure-branch");
        AddEdge(uncertain, uncertainNext, "unknown-next");
        AddEdge(failureStart, failureEnd, "missing-next");

        var a0 = AbstractNode(0);
        var frontier = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AdvanceAbstractStep() },
            new AbstractState(1),
            maxDepth: 1);
        AddEdge(a0, frontier, "to-frontier");
        AddEdge(a0, AbstractNode(10), "to-failure-start");

        var result = Check(c0, a0);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(
            ((ConcreteState)result.Trace[^1].ConcreteNode.State).Value,
            Is.EqualTo(11));
    }

    [Test]
    public void NewlyAllocatedSemanticallyEqualMappedStatesAreAccepted()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c1, "advance");

        var a0 = AbstractNode(0);
        var a1 = AbstractNode(1);
        AddEdge(a0, a1, "advance");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(c0, a0)
            .Map(state => new AbstractState(state.Value))
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void MappingIsEvaluatedOncePerConcreteConfiguration()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c1, "first");
        AddEdge(c1, c0, "second");

        var a0 = AbstractNode(0);
        var a1 = AbstractNode(1);
        AddEdge(a0, a1, "first");
        AddEdge(a1, a0, "second");

        var calls = 0;
        var result = Refinement
            .Between<ConcreteState, AbstractState>(c0, a0)
            .Map(state =>
            {
                calls++;
                return new AbstractState(state.Value);
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void NullMappingResultIsRejected()
    {
        var check = Refinement
            .Between<ConcreteState, AbstractState>(
                ConcreteNode(0),
                AbstractNode(0))
            .Map(_ => null);

        Assert.That(
            () => check.Check(),
            Throws.InvalidOperationException.With.Message.Contains("returned null"));
    }

    [Test]
    public void DiagnosticTextIdentifiesTransitionMismatch()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c1, "advance");

        var result = Check(c0, AbstractNode(0));
        var text = result.GetTraceString();

        Assert.That(text, Does.Contain("concrete transition has no coherent abstract match"));
        Assert.That(text, Does.Contain("--advance-->"));
        Assert.That(text, Does.Contain("candidates 0"));
    }

    private static RefinementCheckingResult Check(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot)
        => Refinement
            .Between<ConcreteState, AbstractState>(concreteRoot, abstractRoot)
            .Map(state => new AbstractState(state.Value))
            .Check();

    private static StateGraphNode ConcreteNode(int value, string configuration = null)
        => Node(new ConcreteState(value), configuration ?? $"c-{value}");

    private static StateGraphNode AbstractNode(int value, string configuration = null)
        => Node(new AbstractState(value), configuration ?? $"a-{value}");

    private static StateGraphNode Node(State state, string configuration)
    {
        state.Freeze();
        return new StateGraphNode
        {
            State = state,
            StepFunctions = new IStepFunction[]
            {
                new LabelStep($"configuration-{configuration}")
            },
            Edges = new List<StateGraphEdge>()
        };
    }

    private static void AddEdge(
        StateGraphNode source,
        StateGraphNode target,
        string stepId,
        object metadata = null)
    {
        source.Edges.Add(new StateGraphEdge
        {
            Target = target,
            StepFunction = new LabelStep(stepId),
            Metadata = metadata
        });
    }
}
