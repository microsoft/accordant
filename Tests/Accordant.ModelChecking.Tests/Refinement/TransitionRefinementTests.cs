namespace Accordant.ModelChecking.Tests.Refinement;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Explicit transition mapping: declaring which abstract response a concrete
/// transition represents when the functional state mapping leaves several
/// state-consistent responses.
/// </summary>
[TestFixture]
public class TransitionRefinementTests
{
    // ---------------------------------------------------------------
    // Stutter against a state-neutral abstract edge.
    // ---------------------------------------------------------------

    [Test]
    public void StutterAndStateNeutralEdgeAreAmbiguousWithoutADeclaration()
    {
        var (concrete, abstraction) = ReconfiguringModels();

        Assert.That(
            () => Temporal(concrete, abstraction, declared: null),
            Throws.TypeOf<AmbiguousTemporalRefinementException>()
                .With.Property(nameof(
                    AmbiguousTemporalRefinementException.MatchCount)).EqualTo(2)
                .And.Property(nameof(
                    AmbiguousTemporalRefinementException.Responses))
                .Length.EqualTo(2));
    }

    [Test]
    public void DeclaringStutterKeepsTheAbstractConfiguration()
    {
        // Staying at a0 means the abstract model never gains the advance
        // edge, so the later concrete advance has no response.
        var (concrete, abstraction) = ReconfiguringModels();

        var result = Temporal(
            concrete,
            abstraction,
            OnStep("noop", AbstractResponse.Stutter));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
    }

    [Test]
    public void DeclaringTheStateNeutralEdgeMovesTheAbstractConfiguration()
    {
        // Taking the reconfigure edge reaches the configuration that can
        // answer the later concrete advance, even though neither response
        // changes the abstract state.
        var (concrete, abstraction) = ReconfiguringModels();

        var result = Temporal(
            concrete,
            abstraction,
            OnStep("noop", AbstractResponse.Step(
                step => step.StepFunctionId == "reconfigure")));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    // ---------------------------------------------------------------
    // Parallel abstract edges.
    // ---------------------------------------------------------------

    [Test]
    public void ParallelAbstractEdgesAreResolvedByStepFunction()
    {
        var (concrete, abstraction) = ParallelModels(metadataOnly: false);

        Assert.That(
            () => Temporal(concrete, abstraction, declared: null),
            Throws.TypeOf<AmbiguousTemporalRefinementException>());
        Assert.That(
            Temporal(
                concrete,
                abstraction,
                OnStep("do", AbstractResponse.Step(
                    step => step.StepFunctionId == "beta"))).Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            Temporal(
                concrete,
                abstraction,
                OnStep("do", AbstractResponse.Step(
                    step => step.StepFunctionId == "alpha"))).FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch),
            "alpha reaches the configuration that cannot answer the next step");
    }

    [Test]
    public void ParallelAbstractEdgesAreResolvedByEdgeMetadata()
    {
        var (concrete, abstraction) = ParallelModels(metadataOnly: true);

        Assert.That(
            () => Temporal(concrete, abstraction, declared: null),
            Throws.TypeOf<AmbiguousTemporalRefinementException>());
        Assert.That(
            Temporal(
                concrete,
                abstraction,
                OnStep("do", AbstractResponse.Matching(
                    response => (string)response.Metadata == "beta"))).Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void TypedMatchingSeesAbstractSourceAndTarget()
    {
        var (concrete, abstraction) = ParallelModels(metadataOnly: false);

        var result = Temporal(
            concrete,
            abstraction,
            OnStep("do", AbstractResponse.Matching<AbstractState>(
                (source, step, target) =>
                    source.Value == 0 &&
                    target.Value == 1 &&
                    step.StepFunctionId == "beta")));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void TypedMatchingIgnoresAbstractStutter()
    {
        var (concrete, abstraction) = ReconfiguringModels();

        var result = Temporal(
            concrete,
            abstraction,
            OnStep("noop", AbstractResponse.Matching<AbstractState>(
                (source, step, target) =>
                    source.Value == target.Value &&
                    step.StepFunctionId == "reconfigure")));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void IndistinguishableResponsesRemainAmbiguous()
    {
        // Two abstract edges with the same step function, the same metadata
        // and equal target states differ only in configuration. No response
        // can separate them, so the checker still refuses to choose.
        var concrete = ConcreteNode(0, "c0");
        AddEdge(concrete, ConcreteNode(1, "c1"), "do");

        var a0 = AbstractNode(0, "a0");
        AddEdge(a0, AbstractNode(1, "left"), "same", "same");
        AddEdge(a0, AbstractNode(1, "right"), "same", "same");

        Assert.That(
            () => Temporal(
                concrete,
                a0,
                OnStep("do", AbstractResponse.Step(
                    step => step.StepFunctionId == "same"))),
            Throws.TypeOf<AmbiguousTemporalRefinementException>()
                .With.Property(nameof(
                    AmbiguousTemporalRefinementException.DeclaredResponse))
                .Not.Null
                .And.Message.Contains("Narrow the declared response"));
    }

    // ---------------------------------------------------------------
    // Narrowing never widens.
    // ---------------------------------------------------------------

    [Test]
    public void ADeclarationCannotSelectAStateInconsistentEdge()
    {
        var concrete = ConcreteNode(0, "c0");
        AddEdge(concrete, ConcreteNode(1, "c1"), "do");

        var a0 = AbstractNode(0, "a0");
        AddEdge(a0, AbstractNode(1, "a1"), "alpha");
        AddEdge(a0, AbstractNode(9, "a9"), "beta");

        var result = Temporal(
            concrete,
            a0,
            OnStep("do", AbstractResponse.Step(
                step => step.StepFunctionId == "beta")));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch),
            "the beta edge leads to an abstract state the mapping excluded");
    }

    [Test]
    public void UnconstrainedReproducesMapOnlyBehaviour()
    {
        var (ambiguousConcrete, ambiguousAbstract) = ReconfiguringModels();
        var refining = ConcreteNode(0, "c0");
        AddEdge(refining, ConcreteNode(1, "c1"), "do");
        var refiningAbstract = AbstractNode(0, "a0");
        AddEdge(refiningAbstract, AbstractNode(1, "a1"), "alpha");

        Assert.That(
            () => Temporal(
                ambiguousConcrete,
                ambiguousAbstract,
                _ => AbstractResponse.Unconstrained),
            Throws.TypeOf<AmbiguousTemporalRefinementException>(),
            "an unconstrained declaration keeps the ambiguity error");
        Assert.That(
            Temporal(refining, refiningAbstract, declared: null).Status,
            Is.EqualTo(
                Temporal(
                    refining,
                    refiningAbstract,
                    _ => AbstractResponse.Unconstrained).Status));
        Assert.That(
            Safety(refining, refiningAbstract, declared: null).Status,
            Is.EqualTo(
                Safety(
                    refining,
                    refiningAbstract,
                    _ => AbstractResponse.Unconstrained).Status));
    }

    // ---------------------------------------------------------------
    // Safety refinement.
    // ---------------------------------------------------------------

    [Test]
    public void SafetyKeepsBothCandidatesWithoutADeclaration()
    {
        var (concrete, abstraction) = ParallelModels(metadataOnly: false);

        Assert.That(
            Safety(concrete, abstraction, declared: null).Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            "safety carries a candidate set and never has to choose");
    }

    [Test]
    public void SafetyNarrowingTurnsAnAcceptedRunIntoAMismatch()
    {
        var (concrete, abstraction) = ParallelModels(metadataOnly: false);

        var result = Safety(
            concrete,
            abstraction,
            OnStep("do", AbstractResponse.Step(
                step => step.StepFunctionId == "alpha")));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(result.Trace[^1].AbstractCandidates, Is.Empty);
    }

    [Test]
    public void SafetyReportsTheDeclaredAndTheRejectedResponses()
    {
        var (concrete, abstraction) = ParallelModels(metadataOnly: false);

        var result = Safety(
            concrete,
            abstraction,
            OnStep("more", AbstractResponse.Step(
                step => step.StepFunctionId == "absent")));

        var failure = result.Trace[^1];
        Assert.That(failure.DeclaredAbstractResponse, Is.Not.Null);
        Assert.That(
            failure.StateConsistentAbstractTransitions
                .Select(response => response.StepFunction?.StepFunctionId),
            Does.Contain("continue"));
        Assert.That(
            result.GetTraceString(),
            Does.Contain("state-consistent abstract responses"));
    }

    // ---------------------------------------------------------------
    // Fairness.
    // ---------------------------------------------------------------

    [Test]
    public void TheDeclaredEdgeDecidesWhichObligationIsDischarged()
    {
        var (concrete, abstraction) = FairnessModels();
        var strongAlpha = Fairness.Strong(
            step => step.StepFunctionId == "alpha");

        Assert.That(
            Temporal(
                concrete,
                abstraction,
                OnStep("tick", AbstractResponse.Step(
                    step => step.StepFunctionId == "alpha")),
                abstractFairness: strongAlpha).Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            Temporal(
                concrete,
                abstraction,
                OnStep("tick", AbstractResponse.Step(
                    step => step.StepFunctionId == "beta")),
                abstractFairness: strongAlpha).FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch),
            "declaring beta starves the alpha obligation");
    }

    [Test]
    public void AStateNeutralEdgeNeverRaisesOrDischargesAnObligation()
    {
        // The reconfigure edge is a real abstract action that can now be
        // aligned explicitly, but it does not change the abstract state.
        // Accordant fairness is defined over changing edges, so neither
        // strength of constraint on it has any effect.
        var (concrete, abstraction) = ReconfiguringModels();
        var declared = OnStep("noop", AbstractResponse.Step(
            step => step.StepFunctionId == "reconfigure"));

        foreach (var fairness in new[]
        {
            Fairness.None,
            Fairness.Weak(step => step.StepFunctionId == "reconfigure"),
            Fairness.Strong(step => step.StepFunctionId == "reconfigure")
        })
        {
            Assert.That(
                Temporal(
                    concrete,
                    abstraction,
                    declared,
                    abstractFairness: fairness).Status,
                Is.EqualTo(RefinementCheckingStatus.Refines));
        }
    }

    [Test]
    public void ADeclarationDoesNotChangeConcreteEnabledness()
    {
        // Weak concrete fairness for the settling step must still exclude the
        // polling cycle, even though the declaration rejects a response the
        // polling step could otherwise have used.
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(0, "c1");
        var done = ConcreteNode(1, "done");
        AddEdge(c0, c1, "poll");
        AddEdge(c1, c0, "poll");
        AddEdge(c0, done, "settle");
        AddEdge(c1, done, "settle");

        var a0 = AbstractNode(0, "a0");
        AddEdge(a0, AbstractNode(1, "a1"), "abstract-settle");

        var result = Temporal(
            c0,
            a0,
            OnStep("poll", AbstractResponse.Stutter),
            concreteFairness: Fairness.Weak(
                step => step.StepFunctionId == "settle"),
            abstractFairness: Fairness.Weak(
                step => step.StepFunctionId == "abstract-settle"));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    // ---------------------------------------------------------------
    // Frontiers, lazy graphs and the synthetic terminal stutter.
    // ---------------------------------------------------------------

    [Test]
    public void ARejectedResponseAtAnAbstractFrontierIsInconclusive()
    {
        // The abstract frontier has unknown successors, so a declaration that
        // no known response satisfies must not become a definite mismatch.
        var concrete = ConcreteNode(0, "c0");
        AddEdge(concrete, concrete, "noop");
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AbstractCounterStep() },
            new AbstractState(0),
            maxDepth: 1,
            lazy: true);

        var result = Temporal(
            concrete,
            abstraction,
            OnStep("noop", AbstractResponse.Step(
                step => step.StepFunctionId == "never-explored")));

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
        Assert.That(abstraction.IsDepthFrontier, Is.True);
    }

    [Test]
    public void ADeclarationDoesNotExpandALazyAbstractGraph()
    {
        // The declaration filters responses the state matching already
        // enumerated, so it must never force additional lazy expansion.
        var concrete = ConcreteNode(0, "c0");
        AddEdge(concrete, concrete, "noop");

        var plainStep = new CountingAbstractCounterStep();
        var withoutDeclaration = StateGraph.ExploreStateGraph(
            new IStepFunction[] { plainStep },
            new AbstractState(0),
            maxDepth: 3,
            lazy: true);
        Temporal(concrete, withoutDeclaration, declared: null);

        var declaredStep = new CountingAbstractCounterStep();
        var withDeclaration = StateGraph.ExploreStateGraph(
            new IStepFunction[] { declaredStep },
            new AbstractState(0),
            maxDepth: 3,
            lazy: true);
        Temporal(
            concrete,
            withDeclaration,
            OnStep("noop", AbstractResponse.Matching(_ => false)));

        Assert.That(plainStep.Applications, Is.GreaterThan(0));
        Assert.That(
            declaredStep.Applications,
            Is.EqualTo(plainStep.Applications),
            "the declaration filters materialized responses only");
    }

    [Test]
    public void TheSyntheticTerminalStutterIsNeverDeclared()
    {
        var steps = new List<string>();
        var concrete = ConcreteNode(0, "c0");
        AddEdge(concrete, ConcreteNode(0, "terminal"), "noop");

        var abstraction = AbstractNode(0, "a0");

        var result = Temporal(
            concrete,
            abstraction,
            transition =>
            {
                steps.Add(transition.StepFunction.StepFunctionId);
                return AbstractResponse.Unconstrained;
            });

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(steps, Is.EqualTo(new[] { "noop" }));
    }

    [Test]
    public void SyntheticTerminalStutterDoesNotTakeAStateNeutralAction()
    {
        var declarations = 0;
        var concrete = ConcreteNode(0, "terminal");
        var abstraction = AbstractNode(0, "a0");
        AddEdge(abstraction, AbstractNode(0, "a1"), "reconfigure");

        var result = Temporal(
            concrete,
            abstraction,
            _ =>
            {
                declarations++;
                return AbstractResponse.Step(
                    step => step.StepFunctionId == "reconfigure");
            });

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(declarations, Is.Zero);
    }

    // ---------------------------------------------------------------
    // Caching, composition and declaration errors.
    // ---------------------------------------------------------------

    [Test]
    public void TheDeclarationIsEvaluatedOncePerConcreteEdgeAndProofState()
    {
        // The same concrete edge is aligned from two abstract configurations.
        var invocations = 0;
        var concrete = ConcreteNode(0, "c0");
        AddEdge(concrete, concrete, "noop");

        var a0 = AbstractNode(0, "a0");
        var a1 = AbstractNode(0, "a1");
        AddEdge(a0, a1, "reconfigure");
        AddEdge(a1, a1, "reconfigure");

        var result = Temporal(
            concrete,
            a0,
            _ =>
            {
                invocations++;
                return AbstractResponse.Step(
                    step => step.StepFunctionId == "reconfigure");
            });

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(invocations, Is.EqualTo(1));
    }

    [Test]
    public void AugmentationDecidesAnActionTheTransitionCannotName()
    {
        // Both claims reach the same concrete node, so the finishing concrete
        // edge is literally the same edge in both runs. Only the recovered
        // history can say which abstract action it is.
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(1, "c1");
        var c2 = ConcreteNode(2, "c2");
        AddEdge(c0, c1, "claim-alice");
        AddEdge(c0, c1, "claim-bob");
        AddEdge(c1, c2, "finish");

        var a0 = AbstractNode(0, "a0");
        var a1 = AbstractNode(1, "a1");
        AddEdge(a0, a1, "accept");
        AddEdge(a1, AbstractNode(2, "by-alice"), "finish-alice");
        AddEdge(a1, AbstractNode(2, "by-bob"), "finish-bob");

        var check = Refinement
            .Between<ConcreteState, AbstractState>(c0, a0)
            .Augment(
                initial: _ => new OwnerState("none"),
                next: (owner, transition) =>
                    transition.StepFunction.StepFunctionId.StartsWith("claim-")
                        ? new OwnerState(
                            transition.StepFunction.StepFunctionId.Substring(6))
                        : owner)
            .Map((concrete, _) => new AbstractState(concrete.Value));

        Assert.That(
            () => check.CheckTemporal(),
            Throws.TypeOf<AmbiguousTemporalRefinementException>());
        Assert.That(
            check
                .MapTransition((transition, owner) =>
                    transition.StepFunction.StepFunctionId == "finish"
                        ? AbstractResponse.Step(
                            step => step.StepFunctionId == "finish-" + owner.Owner)
                        : AbstractResponse.Unconstrained)
                .CheckTemporal()
                .Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void MutatingAugmentationInADeclarationIsRejected()
    {
        var concrete = ConcreteNode(0, "c0");
        AddEdge(concrete, ConcreteNode(0, "c1"), "noop");
        var abstraction = AbstractNode(0, "a0");

        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(concrete, abstraction)
                .Augment(
                    initial: _ => new MutableOwnerState { Owner = "none" },
                    next: (owner, _) => owner)
                .Map((state, _) => new AbstractState(state.Value))
                .MapTransition((_, owner) =>
                {
                    owner.Owner = "changed";
                    return AbstractResponse.Stutter;
                })
                .CheckTemporal(),
            Throws.TypeOf<StateFrozenException>());
    }

    [Test]
    public void MutatingAugmentationInAResponsePredicateIsRejected()
    {
        var concrete = ConcreteNode(0, "c0");
        AddEdge(concrete, ConcreteNode(0, "c1"), "noop");
        var abstraction = AbstractNode(0, "a0");

        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(concrete, abstraction)
                .Augment(
                    initial: _ => new MutableOwnerState { Owner = "none" },
                    next: (owner, _) => owner)
                .Map((state, _) => new AbstractState(state.Value))
                .MapTransition((_, owner) => AbstractResponse.Matching(
                    _ =>
                    {
                        owner.Owner = "changed";
                        return true;
                    }))
                .CheckTemporal(),
            Throws.TypeOf<StateFrozenException>());
    }

    [Test]
    public void ADeclarationIsNotEvaluatedAfterAlignmentHasFailed()
    {
        var declarations = new List<string>();
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(1, "c1");
        AddEdge(c0, c1, "diverge");
        AddEdge(c1, c1, "after-mismatch");
        var abstraction = AbstractNode(0, "a0");

        var result = Temporal(
            c0,
            abstraction,
            transition =>
            {
                declarations.Add(transition.StepFunction.StepFunctionId);
                return AbstractResponse.Unconstrained;
            });

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(declarations, Is.EqualTo(new[] { "diverge" }));
    }

    [Test]
    public void ThePredictionStillPendingDecidesTheAbstractAction()
    {
        // The declaration reads the proof state the transition departs from,
        // so a prediction this very transition resolves is still visible.
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(1, "c1");
        AddEdge(c0, c1, "reveal-red");

        var a0 = AbstractNode(0, "a0");
        AddEdge(a0, AbstractNode(1, "as-red"), "settle-red");
        AddEdge(a0, AbstractNode(1, "as-blue"), "settle-blue");

        var check = Refinement
            .Between<ConcreteState, AbstractState>(c0, a0)
            .WithWitness(
                initial: _ => WitnessChanges.Introduce(
                    "color",
                    new ColorWitness("red"),
                    new ColorWitness("blue")),
                next: (_, transition) =>
                    transition.StepFunction.StepFunctionId == "reveal-red"
                        ? WitnessChanges.Resolve("color", new ColorWitness("red"))
                        : WitnessChanges.None)
            .Map((concrete, _) => new AbstractState(concrete.Value));

        Assert.That(
            () => check.CheckTemporal(),
            Throws.TypeOf<AmbiguousTemporalRefinementException>());
        Assert.That(
            check
                .MapTransition((_, witnesses) => AbstractResponse.Step(
                    step => step.StepFunctionId ==
                        "settle-" + witnesses.Get<ColorWitness>("color").Color))
                .CheckTemporal()
                .Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            "the blue copy dies at the resolving edge, so only red survives");
    }

    [Test]
    public void DeclaringTheTransitionMappingTwiceIsRejected()
    {
        var check = Refinement
            .Between<ConcreteState, AbstractState>(
                ConcreteNode(0, "c0"),
                AbstractNode(0, "a0"))
            .Map(state => new AbstractState(state.Value))
            .MapTransition(_ => AbstractResponse.Unconstrained);

        Assert.That(
            () => check.MapTransition(_ => AbstractResponse.Stutter),
            Throws.InvalidOperationException.With.Message.Contains("once"));
    }

    [Test]
    public void ANullDeclarationIsRejected()
    {
        var check = Refinement
            .Between<ConcreteState, AbstractState>(
                ConcreteNode(0, "c0"),
                AbstractNode(0, "a0"))
            .Map(state => new AbstractState(state.Value));

        Assert.That(
            () => check.MapTransition(
                (Func<RefinementTransition<ConcreteState>, AbstractResponse>)null),
            Throws.ArgumentNullException);
    }

    [Test]
    public void ANullResponseFromTheDeclarationIsRejected()
    {
        var concrete = ConcreteNode(0, "c0");
        AddEdge(concrete, ConcreteNode(1, "c1"), "do");
        var a0 = AbstractNode(0, "a0");
        AddEdge(a0, AbstractNode(1, "a1"), "alpha");

        Assert.That(
            () => Temporal(concrete, a0, _ => null),
            Throws.InvalidOperationException
                .With.Message.Contains(nameof(AbstractResponse.Unconstrained)));
    }

    // ---------------------------------------------------------------
    // Diagnostics.
    // ---------------------------------------------------------------

    [Test]
    public void TheTemporalTraceReportsDeclaredAndAlignedResponses()
    {
        var (concrete, abstraction) = ReconfiguringModels();

        var result = Temporal(
            concrete,
            abstraction,
            OnStep("noop", AbstractResponse.Stutter));
        var text = result.GetTraceString();

        Assert.That(
            result.Trace.Any(item =>
                item.DeclaredAbstractResponse == AbstractResponse.Stutter),
            Is.True);
        Assert.That(
            result.Trace.Any(item =>
                item.AlignedAbstractTransition != null &&
                item.AlignedAbstractTransition.IsStutter),
            Is.True);
        Assert.That(text, Does.Contain("declared stutter"));
        Assert.That(text, Does.Contain("abstract stutter"));
    }

    [Test]
    public void AStateNeutralEdgeIsNotStutterInTheTypedView()
    {
        AbstractTransition observed = null;
        var (concrete, abstraction) = ReconfiguringModels();

        Temporal(
            concrete,
            abstraction,
            OnStep("noop", AbstractResponse.Matching(response =>
            {
                if (!response.IsStutter)
                {
                    observed = response;
                }
                return !response.IsStutter;
            })));

        Assert.That(observed, Is.Not.Null);
        Assert.That(observed.IsStutter, Is.False);
        Assert.That(
            observed.ChangesState,
            Is.False,
            "a state-neutral abstract edge is a real action that does not " +
            "change the abstract state");
        Assert.That(observed.StepFunction.StepFunctionId, Is.EqualTo("reconfigure"));
    }

    // ---------------------------------------------------------------
    // Models.
    // ---------------------------------------------------------------

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
    /// Two abstract edges with equal target states, reachable by one concrete
    /// step, leading to configurations with different futures.
    /// </summary>
    private static (StateGraphNode Concrete, StateGraphNode Abstract)
        ParallelModels(bool metadataOnly)
    {
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(1, "c1");
        AddEdge(c0, c1, "do");
        AddEdge(c1, ConcreteNode(2, "c2"), "more");

        var a0 = AbstractNode(0, "a0");
        var viaAlpha = AbstractNode(1, "via-alpha");
        var viaBeta = AbstractNode(1, "via-beta");
        AddEdge(a0, viaAlpha, metadataOnly ? "act" : "alpha", "alpha");
        AddEdge(a0, viaBeta, metadataOnly ? "act" : "beta", "beta");
        AddEdge(viaBeta, AbstractNode(2, "a2"), "continue");
        return (c0, a0);
    }

    /// <summary>
    /// A concrete cycle whose abstract responses are two changing edges with
    /// equal target states but different step functions.
    /// </summary>
    private static (StateGraphNode Concrete, StateGraphNode Abstract)
        FairnessModels()
    {
        var c0 = ConcreteNode(0, "c0");
        var c1 = ConcreteNode(1, "c1");
        AddEdge(c0, c1, "tick");
        AddEdge(c1, c0, "tock");

        var a0 = AbstractNode(0, "a0");
        var viaAlpha = AbstractNode(1, "via-alpha");
        var viaBeta = AbstractNode(1, "via-beta");
        AddEdge(a0, viaAlpha, "alpha");
        AddEdge(a0, viaBeta, "beta");
        AddEdge(viaAlpha, a0, "tock");
        AddEdge(viaBeta, a0, "tock");
        return (c0, a0);
    }

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

    private sealed class ColorWitness : State
    {
        public string Color { get; }

        public ColorWitness(string color)
        {
            Color = color;
        }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new ColorWitness(Color);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Color({Color})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class MutableOwnerState : State
    {
        public string Owner { get; set; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new MutableOwnerState { Owner = Owner };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"MutableOwner({Owner})";

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

    private sealed class CountingAbstractCounterStep : IStepFunction
    {
        public int Applications { get; private set; }

        public string StepFunctionId => "abstract-counter";

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            Applications++;
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
}
