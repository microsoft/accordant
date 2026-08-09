namespace Accordant.ModelChecking.Tests.Refinement;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

[TestFixture]
public class FunctionalTemporalRefinementTests
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

    private sealed class ConcreteCounterStep : IStepFunction
    {
        public string StepFunctionId => "concrete-counter";

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

    private sealed class AbstractCounterStep : IStepFunction
    {
        public string StepFunctionId => "abstract-counter";

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
    public void TerminalConcreteStutterRefinesWithoutAbstractFairness()
    {
        var concrete = ConcreteNode(0);
        var a0 = AbstractNode(0);
        AddEdge(a0, AbstractNode(1), "abstract-progress");

        var result = Check(concrete, a0);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void InitialMismatchWithFairTerminalContinuationFails()
    {
        var result = Check(ConcreteNode(0), AbstractNode(1));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.InitialStateMismatch));
        Assert.That(result.Trace.Any(item => item.IsInCycle), Is.True);
    }

    [Test]
    public void TerminalConcreteStutterCanViolateAbstractWeakFairness()
    {
        var concrete = ConcreteNode(0);
        var a0 = AbstractNode(0);
        AddEdge(a0, AbstractNode(1), "abstract-progress");

        var result = Check(
            concrete,
            a0,
            abstractFairness: Fairness.Weak(
                step => step.StepFunctionId == "abstract-progress"));

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(result.Trace.Any(item => item.IsInCycle), Is.True);
        Assert.That(
            result.GetTraceString(),
            Does.Contain("[cycle]"));
    }

    [Test]
    public void ConcreteFairnessExcludesAnInfiniteInternalStutter()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c0, "concrete-wait");
        AddEdge(c0, c1, "concrete-progress");

        var a0 = AbstractNode(0);
        var a1 = AbstractNode(1);
        AddEdge(a0, a1, "abstract-progress");

        var result = Check(
            c0,
            a0,
            concreteFairness: Fairness.Weak(
                step => step.StepFunctionId == "concrete-progress"),
            abstractFairness: Fairness.Weak(
                step => step.StepFunctionId == "abstract-progress"));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void UnfairConcreteStutterIsCounterexampleWithoutConcreteFairness()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c0, "concrete-wait");
        AddEdge(c0, c1, "concrete-progress");

        var a0 = AbstractNode(0);
        AddEdge(a0, AbstractNode(1), "abstract-progress");

        var result = Check(
            c0,
            a0,
            abstractFairness: Fairness.Weak(
                step => step.StepFunctionId == "abstract-progress"));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
    }

    [Test]
    public void AbstractWeakAndStrongFairnessDistinguishRecurringEnablement()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c1, "concrete-forward");
        AddEdge(c1, c0, "concrete-back");

        var a0 = AbstractNode(0);
        var a1 = AbstractNode(1);
        AddEdge(a0, a1, "abstract-forward");
        AddEdge(a1, a0, "abstract-back");
        AddEdge(a0, AbstractNode(2), "abstract-escape");

        var weak = Check(
            c0,
            a0,
            abstractFairness: Fairness.Weak(
                step => step.StepFunctionId == "abstract-escape"));
        var strong = Check(
            c0,
            a0,
            abstractFairness: Fairness.Strong(
                step => step.StepFunctionId == "abstract-escape"));

        Assert.That(weak.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            strong.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
    }

    [Test]
    public void ConcreteStrongFairnessCanExcludeAnIntermittentlyEnabledCycle()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        var c2 = ConcreteNode(2);
        AddEdge(c0, c1, "concrete-forward");
        AddEdge(c1, c0, "concrete-back");
        AddEdge(c0, c2, "concrete-escape");

        var a0 = AbstractNode(0);
        var a1 = AbstractNode(1);
        var a2 = AbstractNode(2);
        AddEdge(a0, a1, "abstract-forward");
        AddEdge(a1, a0, "abstract-back");
        AddEdge(a0, a2, "abstract-escape");

        var weakConcrete = Check(
            c0,
            a0,
            concreteFairness: Fairness.Weak(
                step => step.StepFunctionId == "concrete-escape"),
            abstractFairness: Fairness.Strong(
                step => step.StepFunctionId == "abstract-escape"));
        var strongConcrete = Check(
            c0,
            a0,
            concreteFairness: Fairness.Strong(
                step => step.StepFunctionId == "concrete-escape"),
            abstractFairness: Fairness.Strong(
                step => step.StepFunctionId == "abstract-escape"));

        Assert.That(
            weakConcrete.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(
            strongConcrete.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void ConcreteStrongFairnessPruningPreservesAbstractEnablement()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        var c2 = ConcreteNode(2);
        AddEdge(c0, c1, "s01");
        AddEdge(c0, c2, "s02");
        AddEdge(c1, c0, "s10");
        AddEdge(c1, c2, "s12");
        AddEdge(c2, c0, "s20");

        var a0 = AbstractNode(0);
        var a1 = AbstractNode(1);
        var a2 = AbstractNode(2);
        AddEdge(a0, a1, "s01");
        AddEdge(a0, a2, "s02");
        AddEdge(a1, a0, "s10");
        AddEdge(a1, a2, "s12");
        AddEdge(a2, a0, "s20");

        var result = Check(
            c0,
            a0,
            concreteFairness: Fairness.Strong(
                step => step.StepFunctionId == "s10"),
            abstractFairness: Fairness.Strong(
                step => step.StepFunctionId == "s10"));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void AbstractEdgePredicateFairnessIsPreserved()
    {
        var concrete = ConcreteNode(0);
        var a0 = AbstractNode(0);
        AddEdge(a0, AbstractNode(1), "abstract-progress");

        var result = Check(
            concrete,
            a0,
            abstractFairness: Fairness.Weak<AbstractState>(
                (_, action, _) =>
                    action.StepFunctionId == "abstract-progress"));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
    }

    [Test]
    public void TransitionMismatchFailsOnlyAfterFindingFairContinuation()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c1, "unsupported");

        var result = Check(c0, AbstractNode(0));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(result.Trace.Any(item => item.IsInCycle), Is.True);
    }

    [Test]
    public void EqualStateAbstractEdgeAndStutterAreReportedAsAmbiguous()
    {
        var concrete = ConcreteNode(0);
        var a0 = AbstractNode(0, "a0");
        AddEdge(a0, AbstractNode(0, "a0-next"), "change-configuration");

        Assert.That(
            () => Check(concrete, a0),
            Throws.TypeOf<AmbiguousTemporalRefinementException>()
                .With.Property(nameof(
                    AmbiguousTemporalRefinementException.MatchCount))
                .EqualTo(2));
    }

    [Test]
    public void MatchingBoundedGraphsAreInconclusive()
    {
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new ConcreteCounterStep() },
            new ConcreteState(0),
            maxDepth: 2);
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AbstractCounterStep() },
            new AbstractState(0),
            maxDepth: 2);

        var result = Check(concrete, abstraction);

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
        Assert.That(result.Trace.Last().ConcreteNode.IsDepthFrontier, Is.True);
    }

    [Test]
    public void LazyConcreteFrontierIsNotTreatedAsTerminal()
    {
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new ConcreteCounterStep() },
            new ConcreteState(0),
            maxDepth: 1,
            lazy: true);

        var result = Check(concrete, AbstractNode(0));

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
        Assert.That(concrete.IsDepthFrontier, Is.True);
    }

    [Test]
    public void InitialMismatchAtConcreteFrontierIsInconclusive()
    {
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new ConcreteCounterStep() },
            new ConcreteState(0),
            maxDepth: 1,
            lazy: true);

        var result = Check(concrete, AbstractNode(1));

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
    }

    [Test]
    public void LazyAbstractFrontierMakesKnownStutterInconclusive()
    {
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AbstractCounterStep() },
            new AbstractState(0),
            maxDepth: 1,
            lazy: true);

        var result = Check(ConcreteNode(0), abstraction);

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
        Assert.That(abstraction.IsDepthFrontier, Is.True);
    }

    [Test]
    public void TransitionMismatchTraceRetainsMappedAbstractStates()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        AddEdge(c0, c1, "unsupported");

        var result = Check(c0, AbstractNode(0));

        Assert.That(
            result.Trace.Select(item =>
                ((AbstractState)item.MappedAbstractState).Value),
            Is.EqualTo(new[] { 0, 1, 1 }));
    }

    [Test]
    public void NullFairnessValuesMeanNoFairness()
    {
        var result = Check(
            ConcreteNode(0),
            AbstractNode(0),
            concreteFairness: null,
            abstractFairness: null);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    private static RefinementCheckingResult Check(
        StateGraphNode concrete,
        StateGraphNode abstraction,
        Fairness concreteFairness = null,
        Fairness abstractFairness = null)
        => Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .Map(state => new AbstractState(state.Value))
            .CheckTemporal(concreteFairness, abstractFairness);

    private static StateGraphNode ConcreteNode(
        int value,
        string configuration = null)
        => Node(
            new ConcreteState(value),
            configuration ?? $"concrete-{value}");

    private static StateGraphNode AbstractNode(
        int value,
        string configuration = null)
        => Node(
            new AbstractState(value),
            configuration ?? $"abstract-{value}");

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
        string stepId)
        => source.Edges.Add(new StateGraphEdge
        {
            Target = target,
            StepFunction = new LabelStep(stepId)
        });
}
