namespace Accordant.ModelChecking.Tests.Refinement;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Hiding and projection ergonomics: <c>.Map(...)</c> is the projection,
/// <see cref="AbstractResponse.Hidden"/> is the checked declaration that a
/// concrete action is internal, and the projected trace view names what each
/// concrete transition became abstractly.
/// </summary>
[TestFixture]
public class HiddenActionRefinementTests
{
    // ---------------------------------------------------------------
    // Hidden is checked stutter.
    // ---------------------------------------------------------------

    [Test]
    public void HidingAnInternalActionIsAcceptedLikeStutter()
    {
        var (concrete, abstraction) = HidingModels();

        foreach (var response in new[]
        {
            AbstractResponse.Hidden,
            AbstractResponse.Stutter
        })
        {
            Assert.That(
                Temporal(concrete, abstraction, OnStep("internal", response))
                    .Status,
                Is.EqualTo(RefinementCheckingStatus.Refines),
                response.Description);
            Assert.That(
                Safety(concrete, abstraction, OnStep("internal", response))
                    .Status,
                Is.EqualTo(RefinementCheckingStatus.Refines),
                response.Description);
        }
    }

    [Test]
    public void HidingATransitionThatMovesTheAbstractStateIsAMismatch()
    {
        // Hiding is a claim about the abstract model, and the claim is
        // checked: there is no unchecked way to suppress a transition.
        var (concrete, abstraction) = HidingModels();

        var safety = Safety(
            concrete,
            abstraction,
            OnStep("publish", AbstractResponse.Hidden));
        var temporal = Temporal(
            concrete,
            abstraction,
            OnStep("publish", AbstractResponse.Hidden));

        Assert.That(
            safety.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            temporal.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            safety.GetTraceString(),
            Does.Contain("The declaration hides this concrete transition"));
        Assert.That(
            safety.GetTraceString(),
            Does.Contain("checked claim that the abstract model stutters"));
        Assert.That(
            temporal.GetTraceString(),
            Does.Contain("The declaration hides this concrete transition"),
            "temporal traces continue past the mismatch into a lasso");
    }

    [Test]
    public void HidingNeverAdmitsAStateNeutralAbstractEdge()
    {
        // The reconfigure edge is a real abstract action that happens to
        // leave the abstract state alone. Hiding admits abstract stutter
        // only, so the abstract configuration never advances and the later
        // concrete step has no response.
        var (concrete, abstraction) = ReconfiguringModels();

        Assert.That(
            Temporal(
                concrete,
                abstraction,
                OnStep("noop", AbstractResponse.Hidden)).FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            Temporal(
                concrete,
                abstraction,
                OnStep("noop", AbstractResponse.Step(
                    step => step.StepFunctionId == "reconfigure"))).Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void OnlyStutteringResponsesReportHiding()
    {
        Assert.That(AbstractResponse.Hidden.HidesConcreteAction, Is.True);
        Assert.That(AbstractResponse.Stutter.HidesConcreteAction, Is.True);
        Assert.That(
            AbstractResponse.Unconstrained.HidesConcreteAction,
            Is.False,
            "an unconstrained transition may still move the abstract model");
        Assert.That(
            AbstractResponse.Step(_ => true).HidesConcreteAction,
            Is.False);
        Assert.That(
            AbstractResponse.Matching(response => response.IsStutter)
                .HidesConcreteAction,
            Is.False,
            "an arbitrary predicate is not a hiding declaration");
        Assert.That(
            AbstractResponse.Hidden.Description,
            Does.Contain("stutter"));
    }

    // ---------------------------------------------------------------
    // Hiding changes nothing about enabledness or fairness.
    // ---------------------------------------------------------------

    [Test]
    public void HidingDoesNotDischargeAnAbstractFairnessObligation()
    {
        // The concrete model can loop on its internal action forever. Hiding
        // that action says the abstract model stands still; it does not make
        // the loop disappear, and it discharges no abstract obligation.
        var (concrete, abstraction) = DivergingModels();
        var publishEventually = Fairness.Weak(
            step => step.StepFunctionId == "publish-abstract");

        var diverges = Temporal(
            concrete,
            abstraction,
            OnStep("internal", AbstractResponse.Hidden),
            abstractFairness: publishEventually);

        Assert.That(
            diverges.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch),
            "an infinite hidden loop is still a real concrete behavior");
        Assert.That(
            diverges.Trace.Where(item =>
                item.IsInCycle && item.ConcreteStepFunction != null),
            Is.Not.Empty.And.All.Property(
                nameof(RefinementTraceItem.ProjectionKind))
                .EqualTo(AbstractProjectionKind.HiddenAction));
        Assert.That(
            diverges.GetTraceString(),
            Does.Contain("Every concrete transition in the repeating part is hidden"));
        Assert.That(
            diverges.GetTraceString(),
            Does.Contain("never discharge an abstract fairness obligation"));
    }

    [Test]
    public void ConcreteFairnessIsWhatExcludesAHiddenLoop()
    {
        var (concrete, abstraction) = DivergingModels();

        var result = Temporal(
            concrete,
            abstraction,
            OnStep("internal", AbstractResponse.Hidden),
            concreteFairness: Fairness.Weak(
                step => step.StepFunctionId == "publish"),
            abstractFairness: Fairness.Weak(
                step => step.StepFunctionId == "publish-abstract"));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void HidingDoesNotChangeConcreteEnabledness()
    {
        // The hidden loop exists with and without the declaration, and both
        // runs reach the same verdict for the same reason.
        var (concrete, abstraction) = DivergingModels();
        var abstractFairness = Fairness.Weak(
            step => step.StepFunctionId == "publish-abstract");

        Assert.That(
            Temporal(
                concrete,
                abstraction,
                declared: null,
                abstractFairness: abstractFairness).FailureKind,
            Is.EqualTo(
                Temporal(
                    concrete,
                    abstraction,
                    OnStep("internal", AbstractResponse.Hidden),
                    abstractFairness: abstractFairness).FailureKind));
    }

    // ---------------------------------------------------------------
    // The projected trace view.
    // ---------------------------------------------------------------

    [Test]
    public void TheTemporalProjectionDistinguishesTheThreeAbstractResponses()
    {
        // One concrete run whose steps project onto a hidden action, a
        // state-neutral abstract edge, and an ordinary abstract step.
        var (concrete, abstraction) = MixedProjectionModels();

        var result = Temporal(
            concrete,
            abstraction,
            transition => transition.StepFunction.StepFunctionId switch
            {
                "internal" => AbstractResponse.Hidden,
                "reconfigure" => AbstractResponse.Step(
                    step => step.StepFunctionId == "abstract-reconfigure"),
                _ => AbstractResponse.Unconstrained
            },
            abstractFairness: Fairness.Weak(
                step => step.StepFunctionId == "abstract-advance"));
        var projection = result.GetProjectionString();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(
            Kinds(result),
            Is.EqualTo(new[]
            {
                AbstractProjectionKind.Start,
                AbstractProjectionKind.HiddenAction,
                AbstractProjectionKind.StateNeutralStep,
                AbstractProjectionKind.AbstractStep,
                AbstractProjectionKind.HiddenAction
            }));
        Assert.That(projection, Does.Contain("hidden action (abstract stutter)"));
        Assert.That(
            projection,
            Does.Contain("state-neutral abstract step abstract-reconfigure"));
        Assert.That(projection, Does.Contain("abstract step abstract-advance"));
        Assert.That(projection, Does.Contain("=>"));
        Assert.That(projection, Does.Contain("[cycle]"));
    }

    [Test]
    public void TheSafetyProjectionReportsAnUnpinnedStateNeutralPosition()
    {
        // Safety refinement carries a set of coherent abstract candidates, so
        // it reports an unchanged abstract state rather than guessing between
        // stutter and a state-neutral abstract edge.
        var (concrete, abstraction) = ReconfiguringModels();
        var afterNoop = concrete.Edges[0].Target;
        AddEdge(afterNoop, ConcreteNode(7, "c-broken"), "diverge");

        var undeclared = Safety(concrete, abstraction, declared: null);
        var declared = Safety(
            concrete,
            abstraction,
            OnStep("noop", AbstractResponse.Hidden));

        Assert.That(
            Kinds(undeclared),
            Does.Contain(AbstractProjectionKind.AbstractUnchanged));
        Assert.That(
            Kinds(undeclared).Last(),
            Is.EqualTo(AbstractProjectionKind.Unaligned));
        Assert.That(
            undeclared.GetProjectionString(),
            Does.Contain("no abstract projection"));
        Assert.That(
            Kinds(declared),
            Does.Contain(AbstractProjectionKind.HiddenAction),
            "a hiding declaration pins the unchanged position down");
    }

    [Test]
    public void ProjectingAProvedRefinementReportsTheVerdict()
    {
        var (concrete, abstraction) = HidingModels();

        var result = Temporal(
            concrete,
            abstraction,
            OnStep("internal", AbstractResponse.Hidden));

        Assert.That(result.Trace, Is.Null);
        Assert.That(
            result.GetProjectionString(),
            Is.EqualTo(result.GetTraceString()));
    }

    [Test]
    public void TheCheckerCompletionOfATerminalBehaviorIsNotHiddenDivergence()
    {
        // A terminal concrete state is completed with checker stutter, which
        // aligns with abstract stutter. It is an artifact, not a concrete
        // internal loop, so the divergence explanation must not appear.
        var concrete = ConcreteNode(0, "terminal");
        var abstraction = AbstractNode(0, "a0");
        AddEdge(abstraction, AbstractNode(1, "a1"), "abstract-advance");

        var result = Temporal(
            concrete,
            abstraction,
            declared: null,
            abstractFairness: Fairness.Weak(
                step => step.StepFunctionId == "abstract-advance"));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(
            result.GetProjectionString(),
            Does.Contain("checker completion of a terminal concrete behavior"));
        Assert.That(
            result.Trace.Any(item =>
                item.ProjectionKind ==
                    AbstractProjectionKind.CheckerCompletion),
            Is.True);
        Assert.That(
            result.Trace.Where(item =>
                item.ConcreteStepFunction != null),
            Has.All.Property(nameof(RefinementTraceItem.ProjectionKind))
                .EqualTo(AbstractProjectionKind.CheckerCompletion));
        Assert.That(
            result.GetTraceString(),
            Does.Not.Contain("repeating part is hidden"),
            "the concrete model terminates; it does not diverge");
    }

    // ---------------------------------------------------------------
    // The transition-only declaration composes with every mapping arity.
    // ---------------------------------------------------------------

    [Test]
    public void AugmentedChecksAcceptATransitionOnlyDeclaration()
    {
        var (concrete, abstraction) = HidingModels();
        var check = Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .Augment(
                initial: _ => new OwnerState("none"),
                next: (owner, _) => owner)
            .Map((state, _) => new AbstractState(state.Value));

        Assert.That(
            check
                .MapTransition(OnStep("internal", AbstractResponse.Hidden))
                .CheckTemporal()
                .Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            check
                .MapTransition(OnStep("publish", AbstractResponse.Hidden))
                .Check()
                .FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
    }

    [Test]
    public void WitnessChecksAcceptATransitionOnlyDeclaration()
    {
        var (concrete, abstraction) = HidingModels();
        var check = Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, _) => WitnessChanges.None)
            .Map((state, _) => new AbstractState(state.Value));

        Assert.That(
            check
                .MapTransition(OnStep("internal", AbstractResponse.Hidden))
                .CheckTemporal()
                .Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            check
                .MapTransition(OnStep("publish", AbstractResponse.Hidden))
                .Check()
                .FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
    }

    [Test]
    public void AugmentedWitnessChecksAcceptATransitionOnlyDeclaration()
    {
        var (concrete, abstraction) = HidingModels();
        var check = Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .Augment(
                initial: _ => new OwnerState("none"),
                next: (owner, _) => owner)
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, _) => WitnessChanges.None)
            .Map((state, _, _) => new AbstractState(state.Value));

        Assert.That(
            check
                .MapTransition(OnStep("internal", AbstractResponse.Hidden))
                .CheckTemporal()
                .Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            check
                .MapTransition(OnStep("publish", AbstractResponse.Hidden))
                .Check()
                .FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
    }

    [Test]
    public void ATransitionOnlyDeclarationAgreesWithTheWiderArity()
    {
        var (concrete, abstraction) = ReconfiguringModels();

        AugmentedFunctionalRefinementCheck<ConcreteState, AbstractState, OwnerState>
            Build() => Refinement
                .Between<ConcreteState, AbstractState>(concrete, abstraction)
                .Augment(
                    initial: _ => new OwnerState("none"),
                    next: (owner, _) => owner)
                .Map((state, _) => new AbstractState(state.Value));

        Assert.That(
            Build()
                .MapTransition(OnStep("noop", AbstractResponse.Step(
                    step => step.StepFunctionId == "reconfigure")))
                .CheckTemporal()
                .Status,
            Is.EqualTo(
                Build()
                    .MapTransition((transition, _) =>
                        transition.StepFunction.StepFunctionId == "noop"
                            ? AbstractResponse.Step(
                                step => step.StepFunctionId == "reconfigure")
                            : AbstractResponse.Unconstrained)
                    .CheckTemporal()
                    .Status));
    }

    [Test]
    public void ATransitionOnlyDeclarationStillDeclaresOnlyOnce()
    {
        var (concrete, abstraction) = HidingModels();
        var check = Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .Augment(
                initial: _ => new OwnerState("none"),
                next: (owner, _) => owner)
            .Map((state, _) => new AbstractState(state.Value))
            .MapTransition(_ => AbstractResponse.Unconstrained);

        Assert.That(
            () => check.MapTransition((_, _) => AbstractResponse.Hidden),
            Throws.InvalidOperationException.With.Message.Contains("once"));
        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(concrete, abstraction)
                .Augment(
                    initial: _ => new OwnerState("none"),
                    next: (owner, _) => owner)
                .Map((state, _) => new AbstractState(state.Value))
                .MapTransition(
                    (Func<RefinementTransition<ConcreteState>, AbstractResponse>)
                        null),
            Throws.ArgumentNullException);
    }

    // ---------------------------------------------------------------
    // Models.
    // ---------------------------------------------------------------

    /// <summary>
    /// A concrete internal step the abstract state does not record, followed
    /// by the step the abstract model does record.
    /// </summary>
    private static (StateGraphNode Concrete, StateGraphNode Abstract)
        HidingModels()
    {
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(0, "c1");
        AddEdge(c0, c1, "internal");
        AddEdge(c1, ConcreteNode(1, "c2"), "publish");

        var a0 = AbstractNode(0, "a0");
        AddEdge(a0, AbstractNode(1, "a1"), "publish-abstract");
        return (c0, a0);
    }

    /// <summary>
    /// A concrete model that can repeat its internal action forever while the
    /// abstract step stays enabled.
    /// </summary>
    private static (StateGraphNode Concrete, StateGraphNode Abstract)
        DivergingModels()
    {
        var c0 = ConcreteNode(0, "c0");
        AddEdge(c0, c0, "internal");
        AddEdge(c0, ConcreteNode(1, "done"), "publish");

        var a0 = AbstractNode(0, "a0");
        AddEdge(a0, AbstractNode(1, "a1"), "publish-abstract");
        return (c0, a0);
    }

    /// <summary>
    /// A concrete step invisible to the abstract state, and an abstract model
    /// where a state-neutral edge leads to the only configuration that can
    /// answer the later concrete advance.
    /// </summary>
    private static (StateGraphNode Concrete, StateGraphNode Abstract)
        ReconfiguringModels()
    {
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(0, "c1");
        var c2 = ConcreteNode(1, "c2");
        AddEdge(c0, c1, "noop");
        AddEdge(c1, c2, "advance");

        var a0 = AbstractNode(0, "a0");
        var a1 = AbstractNode(0, "a1");
        AddEdge(a0, a1, "reconfigure");
        AddEdge(a1, AbstractNode(1, "a2"), "advance");
        return (c0, a0);
    }

    /// <summary>
    /// One concrete run whose three steps project onto a hidden action, a
    /// state-neutral abstract edge and an ordinary abstract step, ending in a
    /// concrete loop that leaves the abstract model alone.
    /// </summary>
    private static (StateGraphNode Concrete, StateGraphNode Abstract)
        MixedProjectionModels()
    {
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(0, "c1");
        var c2 = ConcreteNode(0, "c2");
        var c3 = ConcreteNode(1, "c3");
        AddEdge(c0, c1, "internal");
        AddEdge(c1, c2, "reconfigure");
        AddEdge(c2, c3, "advance");
        AddEdge(c3, c3, "idle");

        var a0 = AbstractNode(0, "a0");
        var a1 = AbstractNode(0, "a1");
        var a2 = AbstractNode(1, "a2");
        AddEdge(a0, a1, "abstract-reconfigure");
        AddEdge(a1, a2, "abstract-advance");
        AddEdge(a2, AbstractNode(2, "a3"), "abstract-advance");
        return (c0, a0);
    }

    private static IReadOnlyList<AbstractProjectionKind> Kinds(
        RefinementCheckingResult result)
        => result.Trace
            .Select(item => item.ProjectionKind)
            .ToArray();

    private static Func<RefinementTransition<ConcreteState>, AbstractResponse>
        OnStep(string stepFunctionId, AbstractResponse response)
        => transition =>
            transition.StepFunction.StepFunctionId == stepFunctionId
                ? response
                : AbstractResponse.Unconstrained;

    private static RefinementCheckingResult Temporal(
        StateGraphNode concrete,
        StateGraphNode abstraction,
        Func<RefinementTransition<ConcreteState>, AbstractResponse> declared,
        Fairness concreteFairness = null,
        Fairness abstractFairness = null)
    {
        var check = Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .Map(state => new AbstractState(state.Value));
        return (declared == null ? check : check.MapTransition(declared))
            .CheckTemporal(concreteFairness, abstractFairness);
    }

    private static RefinementCheckingResult Safety(
        StateGraphNode concrete,
        StateGraphNode abstraction,
        Func<RefinementTransition<ConcreteState>, AbstractResponse> declared)
    {
        var check = Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .Map(state => new AbstractState(state.Value));
        return (declared == null ? check : check.MapTransition(declared)).Check();
    }

    private static StateGraphNode ConcreteNode(int value, string configuration)
        => Node(new ConcreteState(value), configuration);

    private static StateGraphNode AbstractNode(int value, string configuration)
        => Node(new AbstractState(value), configuration);

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

    // ---------------------------------------------------------------
    // States and steps.
    // ---------------------------------------------------------------

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

    private sealed class OwnerState : State
    {
        public string Owner { get; }

        public OwnerState(string owner)
        {
            Owner = owner;
        }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new OwnerState(Owner);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Owner({Owner})";

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
}
