namespace AugmentedRefinement;

using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

[TestFixture]
public class AugmentedRefinementTests
{
    [Test]
    public void CheckerLocalOwnerExplainsMergedConcreteState()
    {
        var result = BuildCheck(includeBobAbstractly: true).Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void MissingOwnerSpecificAbstractBranchFails()
    {
        var result = BuildCheck(includeBobAbstractly: false).Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            ((OwnershipAuxiliary)result.Trace[^1].AuxiliaryState).Owner,
            Is.EqualTo("bob"));
    }

    private static AugmentedFunctionalRefinementCheck<
        ConcreteResource,
        AbstractResource,
        OwnershipAuxiliary> BuildCheck(bool includeBobAbstractly)
    {
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[]
            {
                new ClaimConcrete("alice"),
                new ClaimConcrete("bob"),
                new CompleteConcrete()
            },
            new ConcreteResource { Stage = ConcreteStage.Unclaimed },
            lazy: true);
        var abstractSteps = new System.Collections.Generic.List<IStepFunction>
        {
            new ClaimAbstract("alice"),
            new CompleteAbstract()
        };
        if (includeBobAbstractly)
        {
            abstractSteps.Add(new ClaimAbstract("bob"));
        }
        var abstraction = StateGraph.ExploreStateGraph(
            abstractSteps,
            new AbstractResource
            {
                Stage = AbstractStage.Unclaimed,
                Owner = "none"
            },
            lazy: true);

        return Refinement
            .Between<ConcreteResource, AbstractResource>(
                concrete,
                abstraction)
            .Augment(
                initial: _ => new OwnershipAuxiliary { Owner = "none" },
                next: (auxiliary, transition) =>
                {
                    var owner = transition.StepFunction is ClaimConcrete claim
                        ? claim.Owner
                        : auxiliary.Owner;
                    return new OwnershipAuxiliary { Owner = owner };
                })
            .Map((concreteState, auxiliary) => new AbstractResource
            {
                Stage = concreteState.Stage == ConcreteStage.Unclaimed
                    ? AbstractStage.Unclaimed
                    : concreteState.Stage == ConcreteStage.Active
                        ? AbstractStage.Owned
                        : AbstractStage.Completed,
                Owner = auxiliary.Owner
            });
    }
}
