namespace WitnessRefinement;

using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// The specification commits to both verdicts when the order is dispatched.
/// The implementation reveals each verdict only when that response arrives.
/// <c>.Augment(...)</c> recovers the dispatching operator from the concrete
/// past; <c>.WithWitness(...)</c> predicts the two pending verdicts and lets
/// the concrete future validate them.
/// </summary>
[TestFixture]
public class WitnessRefinementTests
{
    private static readonly string[] Operators = { "alice", "bob" };
    private static readonly string[] PaymentResults = { "approved", "declined" };
    private static readonly string[] FraudResults = { "clear", "flagged" };

    [Test]
    public void LateVerdictsSatisfyTheEarlyAbstractCommitment()
    {
        var result = Check(
            ExploreImplementation(),
            ExploreSpecification());

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void ConcreteStateAloneCannotDetermineTheAbstractState()
    {
        // Without witnesses the mapping has to guess the pending verdicts.
        var result = Refinement
            .Between<ImplementationOrder, SpecificationOrder>(
                ExploreImplementation(),
                ExploreSpecification())
            .Augment(
                initial: _ => new DispatcherAuxiliary(),
                next: RememberDispatcher)
            .Map((implementation, auxiliary) =>
                implementation.Stage == OrderStage.New
                    ? new SpecificationOrder { Stage = OrderStage.New }
                    : new SpecificationOrder
                    {
                        Stage = implementation.Stage,
                        Operator = auxiliary.Operator,
                        PaymentOutcome =
                            implementation.PaymentResult ?? "approved",
                        FraudOutcome = implementation.FraudResult ?? "clear"
                    })
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
    }

    [Test]
    public void AnUnsupportedOutcomeProducesAWitnessCounterexample()
    {
        var result = Check(
            ExploreImplementation(),
            ExploreSpecification(paymentOutcomes: new[] { "approved" }));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));

        var lastPending = result.Trace
            .Last(item => item.Witnesses.IsPending("payment"));
        Assert.That(
            lastPending.Witnesses.Get<PaymentWitness>("payment").Verdict,
            Is.EqualTo("declined"));
        Assert.That(
            ((DispatcherAuxiliary)lastPending.AuxiliaryState).Operator,
            Is.EqualTo("alice"));
    }

    [Test]
    public void PendingVerdictsBranchAndArePrunedIndependently()
    {
        var pending = 0;
        var afterPayment = 0;

        var result = Refinement
            .Between<ImplementationOrder, SpecificationOrder>(
                ExploreImplementation(operators: new[] { "alice" }),
                ExploreSpecification(operators: new[] { "alice" }))
            .Augment(
                initial: _ => new DispatcherAuxiliary(),
                next: RememberDispatcher)
            .WithWitness(PredictVerdicts, ResolveVerdicts)
            .Map((implementation, auxiliary, witnesses) =>
            {
                if (implementation.Stage == OrderStage.Dispatched)
                {
                    pending++;
                }
                if (implementation.Stage == OrderStage.PaymentSettled)
                {
                    afterPayment++;
                }
                return Map(implementation, auxiliary, witnesses);
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            pending,
            Is.EqualTo(4),
            "two pending operations with two outcomes each give four copies");
        Assert.That(
            afterPayment,
            Is.EqualTo(4),
            "each revealed payment verdict keeps both fraud predictions");
    }

    private static RefinementCheckingResult Check(
        StateGraphNode implementation,
        StateGraphNode specification)
        => Refinement
            .Between<ImplementationOrder, SpecificationOrder>(
                implementation,
                specification)
            .Augment(
                initial: _ => new DispatcherAuxiliary(),
                next: RememberDispatcher)
            .WithWitness(PredictVerdicts, ResolveVerdicts)
            .Map(Map)
            .Check();

    private static DispatcherAuxiliary RememberDispatcher(
        DispatcherAuxiliary auxiliary,
        RefinementTransition<ImplementationOrder> transition)
        => new DispatcherAuxiliary
        {
            Operator = transition.Metadata as string ?? auxiliary.Operator
        };

    private static WitnessChanges PredictVerdicts(ImplementationOrder root)
        => WitnessChanges.None;

    private static WitnessChanges ResolveVerdicts(
        PendingWitnesses pending,
        RefinementTransition<ImplementationOrder> transition)
    {
        if (transition.Source.Stage == OrderStage.New)
        {
            return WitnessChanges
                .Introduce(
                    "payment",
                    new PaymentWitness { Verdict = "approved" },
                    new PaymentWitness { Verdict = "declined" })
                .And(WitnessChanges.Introduce(
                    "fraud",
                    new FraudWitness { Verdict = "clear" },
                    new FraudWitness { Verdict = "flagged" }));
        }

        var changes = WitnessChanges.None;
        if (transition.Source.PaymentResult == null &&
            transition.Target.PaymentResult != null)
        {
            changes = changes.And(WitnessChanges.Resolve(
                "payment",
                new PaymentWitness
                {
                    Verdict = transition.Target.PaymentResult
                }));
        }
        if (transition.Source.FraudResult == null &&
            transition.Target.FraudResult != null)
        {
            changes = changes.And(WitnessChanges.Resolve(
                "fraud",
                new FraudWitness
                {
                    Verdict = transition.Target.FraudResult
                }));
        }
        return changes;
    }

    private static SpecificationOrder Map(
        ImplementationOrder implementation,
        DispatcherAuxiliary auxiliary,
        WitnessCollection witnesses)
    {
        if (implementation.Stage == OrderStage.New)
        {
            return new SpecificationOrder { Stage = OrderStage.New };
        }

        return new SpecificationOrder
        {
            Stage = implementation.Stage,
            Operator = auxiliary.Operator,
            PaymentOutcome = implementation.PaymentResult
                ?? witnesses.Get<PaymentWitness>("payment").Verdict,
            FraudOutcome = implementation.FraudResult
                ?? witnesses.Get<FraudWitness>("fraud").Verdict
        };
    }

    private static StateGraphNode ExploreImplementation(
        string[] operators = null)
        => StateGraph.ExploreStateGraph(
            new IStepFunction[]
            {
                new ImplementationWorkflow(
                    operators ?? Operators,
                    PaymentResults,
                    FraudResults)
            },
            new ImplementationOrder { Stage = OrderStage.New },
            lazy: true);

    private static StateGraphNode ExploreSpecification(
        string[] operators = null,
        string[] paymentOutcomes = null)
        => StateGraph.ExploreStateGraph(
            new IStepFunction[]
            {
                new SpecificationWorkflow(
                    operators ?? Operators,
                    paymentOutcomes ?? PaymentResults,
                    FraudResults)
            },
            new SpecificationOrder { Stage = OrderStage.New },
            lazy: true);
}
