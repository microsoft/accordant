namespace Accordant.ModelChecking.Tests.Refinement;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

[TestFixture]
public class RelationalRefinementTests
{
    private sealed class ConcreteState : State
    {
        public int Stage { get; }
        public string RevealedChoice { get; }

        public ConcreteState(int stage, string revealedChoice = null)
        {
            Stage = stage;
            RevealedChoice = revealedChoice;
        }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new ConcreteState(Stage, RevealedChoice);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Concrete(Stage={Stage},Choice={RevealedChoice ?? "?"})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AbstractState : State
    {
        public int Stage { get; }
        public string Choice { get; }

        public AbstractState(int stage, string choice = null)
        {
            Stage = stage;
            Choice = choice;
        }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new AbstractState(Stage, Choice);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Abstract(Stage={Stage},Choice={Choice ?? "-"})";

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

    private sealed class AbstractFrontierStep : IStepFunction
    {
        public string StepFunctionId => "abstract-frontier";

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
            => new[]
            {
                new StepResult
                {
                    State = new AbstractState(1, "future"),
                    StepFunctions = new IStepFunction[] { this }
                }
            };
    }

    private sealed class DelayedChoiceConcreteStep : IStepFunction
    {
        public string StepFunctionId => "delayed-choice-concrete";
        public int ApplyCount { get; private set; }

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            ApplyCount++;
            var current = (ConcreteState)state;
            ConcreteState next;
            switch (current.Stage)
            {
                case 0:
                    next = new ConcreteState(stage: 1);
                    break;
                case 1:
                    next = new ConcreteState(stage: 2, revealedChoice: "blue");
                    break;
                case 2:
                    next = new ConcreteState(stage: 3, revealedChoice: "blue");
                    break;
                default:
                    return null;
            }

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

    private sealed class NondeterministicAbstractChoiceStep : IStepFunction
    {
        public string StepFunctionId => "nondeterministic-abstract-choice";
        public int ApplyCount { get; private set; }

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            ApplyCount++;
            var current = (AbstractState)state;
            if (current.Stage == 0)
            {
                return new[]
                {
                    Next(new AbstractState(stage: 1, choice: "red")),
                    Next(new AbstractState(stage: 1, choice: "blue"))
                };
            }

            if (current.Stage >= 3)
            {
                return null;
            }

            return new[]
            {
                Next(new AbstractState(current.Stage + 1, current.Choice))
            };
        }

        private StepResult Next(AbstractState state)
            => new StepResult
            {
                State = state,
                StepFunctions = new IStepFunction[] { this }
            };
    }

    [Test]
    public void InitialStatesMustCorrespond()
    {
        var result = Check(
            ConcreteNode(stage: 0),
            AbstractNode(stage: 1),
            (concrete, abstraction) => concrete.Stage == abstraction.Stage);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.InitialStateMismatch));
        Assert.That(result.Trace, Has.Count.EqualTo(1));
        Assert.That(result.Trace[0].MappedAbstractState, Is.Null);
    }

    [Test]
    public void DelayedConcreteChoicePrunesAbstractCandidatesLater()
    {
        var concrete = BuildDelayedChoiceConcrete(includeDone: true);
        var abstraction = BuildDelayedChoiceAbstract(blueCanFinish: true);

        var result = Check(concrete, abstraction, DelayedChoiceCorrespondence);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void LazyExecutableModels_ResolveDelayedNondeterministicChoice()
    {
        var concreteStep = new DelayedChoiceConcreteStep();
        var abstractStep = new NondeterministicAbstractChoiceStep();
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[] { concreteStep },
            new ConcreteState(stage: 0),
            lazy: true);
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { abstractStep },
            new AbstractState(stage: 0),
            lazy: true);

        var result = Check(concrete, abstraction, DelayedChoiceCorrespondence);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(concreteStep.ApplyCount, Is.EqualTo(4));
        Assert.That(
            abstractStep.ApplyCount,
            Is.EqualTo(4),
            "the red stage-2 branch should be pruned before it is expanded");
    }

    [Test]
    public void CandidateCannotSwitchToIncompatibleEarlierChoice()
    {
        var concrete = BuildDelayedChoiceConcrete(includeDone: true);
        var abstraction = BuildDelayedChoiceAbstract(blueCanFinish: false);

        var result = Check(concrete, abstraction, DelayedChoiceCorrespondence);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(result.FailureKind, Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(result.Trace, Has.Count.EqualTo(4));
        Assert.That(result.Trace[1].AbstractCandidates, Has.Count.EqualTo(2));
        Assert.That(result.Trace[2].AbstractCandidates, Has.Count.EqualTo(1));
        Assert.That(result.Trace[3].AbstractCandidates, Is.Empty);
    }

    [Test]
    public void RelationCanTreatConcreteImplementationStepAsAbstractStutter()
    {
        var c0 = ConcreteNode(stage: 0, configuration: "ready");
        var c1 = ConcreteNode(stage: 0, configuration: "prepared");
        var c2 = ConcreteNode(stage: 1, revealedChoice: "blue");
        AddEdge(c0, c1, "prepare");
        AddEdge(c1, c2, "commit");

        var a0 = AbstractNode(stage: 0);
        var a1 = AbstractNode(stage: 1, choice: "blue");
        AddEdge(a0, a1, "commit");

        var result = Check(
            c0,
            a0,
            (concrete, abstraction) =>
                concrete.Stage == abstraction.Stage &&
                (concrete.RevealedChoice == null ||
                    concrete.RevealedChoice == abstraction.Choice));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void EqualStateConfigurationsRemainPathCoherent()
    {
        var c0 = ConcreteNode(stage: 0);
        var c1 = ConcreteNode(stage: 1);
        var c2 = ConcreteNode(stage: 2, revealedChoice: "blue");
        AddEdge(c0, c1, "choose");
        AddEdge(c1, c2, "reveal");

        var a0 = AbstractNode(stage: 0);
        var dead = AbstractNode(stage: 1, choice: "red", configuration: "dead");
        AddEdge(a0, dead, "dead-choice");

        var detour = AbstractNode(stage: 9, configuration: "detour");
        var live = AbstractNode(stage: 1, choice: "blue", configuration: "live");
        var done = AbstractNode(stage: 2, choice: "blue");
        AddEdge(a0, detour, "detour");
        AddEdge(detour, live, "live-choice");
        AddEdge(live, done, "finish");

        var result = Check(
            c0,
            a0,
            (concrete, abstraction) =>
                concrete.Stage == abstraction.Stage &&
                (concrete.RevealedChoice == null ||
                    concrete.RevealedChoice == abstraction.Choice));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
    }

    [Test]
    public void AbstractFrontierMakesMissingRelationalMatchInconclusive()
    {
        var c0 = ConcreteNode(stage: 0);
        var c1 = ConcreteNode(stage: 1, revealedChoice: "blue");
        AddEdge(c0, c1, "advance");

        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AbstractFrontierStep() },
            new AbstractState(stage: 0),
            maxDepth: 1);

        var result = Check(
            c0,
            abstraction,
            (concrete, abstractState) =>
                concrete.Stage == abstractState.Stage &&
                (concrete.RevealedChoice == null ||
                    concrete.RevealedChoice == abstractState.Choice));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
    }

    [Test]
    public void CorrespondenceIsEvaluatedOncePerConfigurationPair()
    {
        var c0 = ConcreteNode(stage: 0);
        var c1 = ConcreteNode(stage: 1, revealedChoice: "blue");
        AddEdge(c0, c1, "advance");

        var a0 = AbstractNode(stage: 0);
        var a1 = AbstractNode(stage: 1, choice: "blue");
        AddEdge(a0, a1, "advance");

        var calls = 0;
        var result = Check(
            c0,
            a0,
            (concrete, abstraction) =>
            {
                calls++;
                return concrete.Stage == abstraction.Stage &&
                    (concrete.RevealedChoice == null ||
                        concrete.RevealedChoice == abstraction.Choice);
            });

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(calls, Is.EqualTo(3));
    }

    [Test]
    public void NullCorrespondenceIsRejected()
    {
        var builder = Refinement.Between<ConcreteState, AbstractState>(
            ConcreteNode(stage: 0),
            AbstractNode(stage: 0));

        Assert.That(
            () => builder.Corresponds(null),
            Throws.ArgumentNullException);
    }

    [Test]
    public void RelationalDiagnosticOmitsFunctionalMappedState()
    {
        var c0 = ConcreteNode(stage: 0);
        var c1 = ConcreteNode(stage: 1);
        AddEdge(c0, c1, "missing");

        var result = Check(
            c0,
            AbstractNode(stage: 0),
            (concrete, abstraction) => concrete.Stage == abstraction.Stage);
        var text = result.GetTraceString();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(result.Trace.All(item => item.MappedAbstractState == null), Is.True);
        Assert.That(text, Does.Not.Contain("mapped abstract"));
        Assert.That(text, Does.Contain("--missing-->"));
    }

    private static bool DelayedChoiceCorrespondence(
        ConcreteState concrete,
        AbstractState abstraction)
    {
        if (concrete.Stage != abstraction.Stage)
        {
            return false;
        }

        return concrete.RevealedChoice == null ||
            concrete.RevealedChoice == abstraction.Choice;
    }

    private static StateGraphNode BuildDelayedChoiceConcrete(bool includeDone)
    {
        var root = ConcreteNode(stage: 0);
        var pending = ConcreteNode(stage: 1);
        var revealed = ConcreteNode(stage: 2, revealedChoice: "blue");
        AddEdge(root, pending, "request");
        AddEdge(pending, revealed, "reveal-blue");
        if (includeDone)
        {
            AddEdge(
                revealed,
                ConcreteNode(stage: 3, revealedChoice: "blue"),
                "finish");
        }
        return root;
    }

    private static StateGraphNode BuildDelayedChoiceAbstract(bool blueCanFinish)
    {
        var root = AbstractNode(stage: 0);
        var redPending = AbstractNode(stage: 1, choice: "red");
        var bluePending = AbstractNode(stage: 1, choice: "blue");
        var redRevealed = AbstractNode(stage: 2, choice: "red");
        var blueRevealed = AbstractNode(stage: 2, choice: "blue");
        AddEdge(root, redPending, "choose-red");
        AddEdge(root, bluePending, "choose-blue");
        AddEdge(redPending, redRevealed, "reveal-red");
        AddEdge(bluePending, blueRevealed, "reveal-blue");
        AddEdge(
            redRevealed,
            AbstractNode(stage: 3, choice: "red"),
            "finish-red");
        if (blueCanFinish)
        {
            AddEdge(
                blueRevealed,
                AbstractNode(stage: 3, choice: "blue"),
                "finish-blue");
        }
        return root;
    }

    private static RefinementCheckingResult Check(
        StateGraphNode concreteRoot,
        StateGraphNode abstractRoot,
        Func<ConcreteState, AbstractState, bool> correspondence)
        => Refinement
            .Between<ConcreteState, AbstractState>(concreteRoot, abstractRoot)
            .Corresponds(correspondence)
            .Check();

    private static StateGraphNode ConcreteNode(
        int stage,
        string revealedChoice = null,
        string configuration = null)
        => Node(
            new ConcreteState(stage, revealedChoice),
            configuration ?? $"c-{stage}-{revealedChoice}");

    private static StateGraphNode AbstractNode(
        int stage,
        string choice = null,
        string configuration = null)
        => Node(
            new AbstractState(stage, choice),
            configuration ?? $"a-{stage}-{choice}");

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
    {
        source.Edges.Add(new StateGraphEdge
        {
            Target = target,
            StepFunction = new LabelStep(stepId)
        });
    }
}
