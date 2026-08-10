namespace Accordant.ModelChecking.Tests.Refinement;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

[TestFixture]
public class AugmentedRefinementTests
{
    private sealed class ConcreteState : State
    {
        public int Stage { get; }

        public ConcreteState(int stage)
        {
            Stage = stage;
        }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new ConcreteState(Stage);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Concrete({Stage})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AbstractState : State
    {
        public int Stage { get; }
        public string Owner { get; }

        public AbstractState(int stage, string owner)
        {
            Stage = stage;
            Owner = owner;
        }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new AbstractState(Stage, Owner);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Abstract(Stage={Stage},Owner={Owner})";

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

    private sealed class MetadataChoiceStep : IStepFunction
    {
        public string StepFunctionId => "metadata-choice";

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            var concrete = (ConcreteState)state;
            if (concrete.Stage != 0)
            {
                return null;
            }

            return new[]
            {
                Choice("red"),
                Choice("blue")
            };
        }

        private StepResult Choice(string owner)
            => new StepResult
            {
                State = new ConcreteState(stage: 1),
                StepFunctions = new IStepFunction[] { this },
                EdgeMetadata = owner
            };
    }

    private sealed class HistoryState : State
    {
        public HistoryState(string owner)
        {
            Owner = owner;
        }

        public string Owner { get; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new HistoryState(Owner);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"History({Owner})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class MutableHistoryState : State
    {
        public string Owner { get; set; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new MutableHistoryState { Owner = Owner };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"MutableHistory({Owner})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    [Test]
    public void FunctionalSafetyKeepsHistoriesAtSameConcreteNodeSeparate()
    {
        var concrete = BuildConcrete();
        var abstraction = BuildAbstract(blueCanFinish: true);

        var result = FunctionalCheck(concrete, abstraction).Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void MissingHistorySpecificFutureProducesDiagnostic()
    {
        var concrete = BuildConcrete();
        var abstraction = BuildAbstract(blueCanFinish: false);

        var result = FunctionalCheck(concrete, abstraction).Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            ((HistoryState)result.Trace.Last().AuxiliaryState).Owner,
            Is.EqualTo("blue"));
        Assert.That(
            result.GetTraceString(),
            Does.Contain("auxiliary History(blue)"));
    }

    [Test]
    public void RelationalSafetyCanReadAugmentationState()
    {
        var result = Refinement
            .Between<ConcreteState, AbstractState>(
                BuildConcrete(),
                BuildAbstract(blueCanFinish: true))
            .Augment(
                initial: _ => new HistoryState("none"),
                next: UpdateOwner)
            .Corresponds((concrete, owner, abstraction) =>
                concrete.Stage == abstraction.Stage &&
                owner.Owner == abstraction.Owner)
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void TemporalCheckPreservesAugmentationAcrossTerminalStutter()
    {
        var nextCalls = 0;
        HistoryState CountedUpdate(
            HistoryState owner,
            RefinementTransition<ConcreteState> transition)
        {
            nextCalls++;
            return UpdateOwner(owner, transition);
        }

        var result = Refinement
            .Between<ConcreteState, AbstractState>(
                BuildConcrete(),
                BuildAbstract(blueCanFinish: true))
            .Augment(
                initial: _ => new HistoryState("none"),
                next: CountedUpdate)
            .Map(Map)
            .CheckTemporal();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            nextCalls,
            Is.EqualTo(4),
            "synthetic terminal stutter must retain augmentation without updating it");
    }

    [Test]
    public void SemanticStateIdentityMergesEquivalentAugmentationValues()
    {
        var concrete = ConcreteNode(stage: 0);
        AddEdge(concrete, concrete, "loop");
        var nextCalls = 0;

        var result = Refinement
            .Between<ConcreteState, AbstractState>(
                concrete,
                AbstractNode(stage: 0, owner: "same"))
            .Augment(
                initial: _ => new HistoryState("same"),
                next: (history, _) =>
                {
                    nextCalls++;
                    return new HistoryState(history.Owner);
                })
            .Map((state, history) =>
                new AbstractState(state.Stage, history.Owner))
            .CheckTemporal();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(nextCalls, Is.EqualTo(1));
    }

    [Test]
    public void TransitionSuppliesSourceActionMetadataAndTarget()
    {
        var c0 = ConcreteNode(stage: 0);
        var c1 = ConcreteNode(stage: 1);
        AddEdge(c0, c1, "remember", metadata: "blue");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(
                c0,
                BuildSingleAbstract("blue"))
            .Augment(
                initial: _ => new HistoryState("none"),
                next: (_, transition) =>
                {
                    Assert.That(transition.Source.Stage, Is.EqualTo(0));
                    Assert.That(transition.StepFunction.StepFunctionId, Is.EqualTo("remember"));
                    Assert.That(transition.Metadata, Is.EqualTo("blue"));
                    Assert.That(transition.Target.Stage, Is.EqualTo(1));
                    return new HistoryState((string)transition.Metadata);
                })
            .Map(Map)
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExploredMetadataDistinctBranchesRetainSeparateHistories(
        bool lazy)
    {
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new MetadataChoiceStep() },
            new ConcreteState(stage: 0),
            lazy: lazy);
        var abstraction = AbstractNode(stage: 0, owner: "none");
        AddEdge(
            abstraction,
            AbstractNode(stage: 1, owner: "red"),
            "choose-red");
        AddEdge(
            abstraction,
            AbstractNode(stage: 1, owner: "blue"),
            "choose-blue");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .Augment(
                initial: _ => new HistoryState("none"),
                next: (history, transition) =>
                    transition.Metadata is string owner
                        ? new HistoryState(owner)
                        : new HistoryState(history.Owner))
            .Map(Map)
            .Check();

        Assert.That(concrete.Edges, Has.Count.EqualTo(2));
        Assert.That(
            concrete.Edges.Select(edge => edge.Metadata),
            Is.EquivalentTo(new[] { "red", "blue" }));
        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void AugmentationValuesAreFrozenBeforeTheyEnterCallbacks()
    {
        var c0 = ConcreteNode(stage: 0);
        var c1 = ConcreteNode(stage: 1);
        AddEdge(c0, c1, "advance");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(
                c0,
                BuildSingleAbstract("none"))
            .Augment(
                initial: _ => new HistoryState("none"),
                next: (history, _) =>
                {
                    Assert.That(history.IsFrozen, Is.True);
                    return new HistoryState(history.Owner);
                })
            .Map((concrete, history) =>
            {
                Assert.That(history.IsFrozen, Is.True);
                return Map(concrete, history);
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void NullAugmentationCallbacksAreRejected()
    {
        var builder = Refinement.Between<ConcreteState, AbstractState>(
            ConcreteNode(0),
            AbstractNode(0, "none"));

        Assert.That(
            () => builder.Augment<HistoryState>(
                initial: null,
                next: UpdateOwner),
            Throws.ArgumentNullException);
        Assert.That(
            () => builder.Augment<HistoryState>(
                initial: _ => new HistoryState("none"),
                next: null),
            Throws.ArgumentNullException);
    }

    [Test]
    public void NullAugmentedMappingAndCorrespondenceAreRejected()
    {
        var builder = Refinement
            .Between<ConcreteState, AbstractState>(
                ConcreteNode(0),
                AbstractNode(0, "none"))
            .Augment(
                initial: _ => new HistoryState("none"),
                next: UpdateOwner);

        Assert.That(
            () => builder.Map(null),
            Throws.ArgumentNullException);
        Assert.That(
            () => builder.Corresponds(null),
            Throws.ArgumentNullException);
    }

    [Test]
    public void NullAugmentationValuesAreRejected()
    {
        var roots = Refinement.Between<ConcreteState, AbstractState>(
            ConcreteNode(0),
            AbstractNode(0, "none"));

        Assert.That(
            () => roots
                .Augment<HistoryState>(
                    initial: _ => null,
                    next: UpdateOwner)
                .Map(Map)
                .Check(),
            Throws.InvalidOperationException.With.Message.Contains(
                "initializer returned null"));

        var c0 = ConcreteNode(0);
        AddEdge(c0, ConcreteNode(1), "advance");
        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(
                    c0,
                    BuildSingleAbstract("none"))
                .Augment(
                    initial: _ => new HistoryState("none"),
                    next: (_, _) => null)
                .Map(Map)
                .Check(),
            Throws.InvalidOperationException.With.Message.Contains(
                "update returned null"));
    }

    [Test]
    public void MutatingAugmentationInUpdateIsRejected()
    {
        var c0 = ConcreteNode(0);
        AddEdge(c0, ConcreteNode(1), "advance");

        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(
                    c0,
                    BuildSingleAbstract("none"))
                .Augment(
                    initial: _ => new MutableHistoryState { Owner = "none" },
                    next: (history, _) =>
                    {
                        history.Owner = "changed";
                        return history;
                    })
                .Map((concrete, history) =>
                    new AbstractState(concrete.Stage, history.Owner))
                .Check(),
            Throws.TypeOf<StateFrozenException>());
    }

    [Test]
    public void MutatingAugmentationInMappingIsRejected()
    {
        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(
                    ConcreteNode(0),
                    AbstractNode(0, "none"))
                .Augment(
                    initial: _ => new MutableHistoryState { Owner = "none" },
                    next: (history, _) => history)
                .Map((concrete, history) =>
                {
                    history.Owner = "changed";
                    return new AbstractState(concrete.Stage, "none");
                })
                .Check(),
            Throws.TypeOf<StateFrozenException>());
    }

    [Test]
    public void RefreezingCannotHideAugmentationMutation()
    {
        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(
                    ConcreteNode(0),
                    AbstractNode(0, "none"))
                .Augment(
                    initial: _ => new MutableHistoryState { Owner = "none" },
                    next: (history, _) => history)
                .Map((concrete, history) =>
                {
                    history.Owner = "changed";
                    history.Freeze();
                    return new AbstractState(concrete.Stage, "none");
                })
                .Check(),
            Throws.TypeOf<StateFrozenException>());
    }

    [Test]
    public void MutatingAndReusingOlderFrozenAugmentationIsRejected()
    {
        var c0 = ConcreteNode(0);
        var c1 = ConcreteNode(1);
        var c2 = ConcreteNode(2);
        AddEdge(c0, c1, "first");
        AddEdge(c1, c2, "second");
        var abstraction = AbstractNode(0, "initial");
        var a1 = AbstractNode(1, "current");
        AddEdge(abstraction, a1, "first");
        AddEdge(a1, AbstractNode(2, "changed"), "second");
        MutableHistoryState older = null;

        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(c0, abstraction)
                .Augment(
                    initial: _ => older = new MutableHistoryState
                    {
                        Owner = "initial"
                    },
                    next: (history, transition) =>
                    {
                        if (transition.StepFunction.StepFunctionId == "first")
                        {
                            return new MutableHistoryState
                            {
                                Owner = "current"
                            };
                        }
                        older.Owner = "changed";
                        return older;
                    })
                .Map((concrete, history) =>
                    new AbstractState(concrete.Stage, history.Owner))
                .Check(),
            Throws.TypeOf<StateFrozenException>());
    }

    private static AugmentedFunctionalRefinementCheck<
        ConcreteState,
        AbstractState,
        HistoryState> FunctionalCheck(
            StateGraphNode concrete,
            StateGraphNode abstraction)
        => Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .Augment(
                initial: _ => new HistoryState("none"),
                next: UpdateOwner)
            .Map(Map);

    private static HistoryState UpdateOwner(
        HistoryState history,
        RefinementTransition<ConcreteState> transition)
        => transition.StepFunction.StepFunctionId == "remember-red"
            ? new HistoryState("red")
            : transition.StepFunction.StepFunctionId == "remember-blue"
                ? new HistoryState("blue")
                : new HistoryState(history.Owner);

    private static AbstractState Map(
        ConcreteState concrete,
        HistoryState history)
        => new AbstractState(concrete.Stage, history.Owner);

    private static StateGraphNode BuildConcrete()
    {
        var root = ConcreteNode(stage: 0);
        var pending = ConcreteNode(stage: 1);
        var done = ConcreteNode(stage: 2);
        AddEdge(root, pending, "remember-red");
        AddEdge(root, pending, "remember-blue");
        AddEdge(pending, done, "finish");
        return root;
    }

    private static StateGraphNode BuildAbstract(bool blueCanFinish)
    {
        var root = AbstractNode(stage: 0, owner: "none");
        var red = AbstractNode(stage: 1, owner: "red");
        var blue = AbstractNode(stage: 1, owner: "blue");
        AddEdge(root, red, "choose-red");
        AddEdge(root, blue, "choose-blue");
        AddEdge(red, AbstractNode(stage: 2, owner: "red"), "finish-red");
        if (blueCanFinish)
        {
            AddEdge(
                blue,
                AbstractNode(stage: 2, owner: "blue"),
                "finish-blue");
        }
        return root;
    }

    private static StateGraphNode BuildSingleAbstract(string owner)
    {
        var root = AbstractNode(stage: 0, owner: "none");
        AddEdge(root, AbstractNode(stage: 1, owner: owner), "remember");
        return root;
    }

    private static StateGraphNode ConcreteNode(int stage)
        => Node(new ConcreteState(stage), $"concrete-{stage}");

    private static StateGraphNode AbstractNode(int stage, string owner)
        => Node(
            new AbstractState(stage, owner),
            $"abstract-{stage}-{owner}");

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
        => source.Edges.Add(new StateGraphEdge
        {
            Target = target,
            StepFunction = new LabelStep(stepId),
            Metadata = metadata
        });
}
