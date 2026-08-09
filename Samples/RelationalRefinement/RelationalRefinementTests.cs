namespace RelationalRefinement;

using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

[TestFixture]
public class RelationalRefinementTests
{
    private static readonly string[] Gateways = { "card", "bank" };

    [Test]
    public void BothImplementationChoicesHaveAbstractExplanations()
    {
        var concrete = ExploreConcrete("card", "bank");
        var abstraction = ExploreAbstract(
            choices: Gateways,
            choicesThatCanComplete: Gateways);

        var result = Check(concrete, abstraction);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void EveryConcreteBranchMustHaveAnAbstractExplanation()
    {
        var concrete = ExploreConcrete("card", "bank");
        var abstraction = ExploreAbstract(
            choices: new[] { "card" },
            choicesThatCanComplete: new[] { "card" });

        var result = Check(concrete, abstraction);

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(
            result.Trace.Last().ConcreteNode.State.ToString(),
            Does.Contain("bank"));
    }

    [Test]
    public void APrunedChoiceCannotBeReusedAfterTheGatewayIsRevealed()
    {
        var concrete = ExploreConcrete("bank");
        var abstraction = ExploreAbstract(
            choices: Gateways,
            choicesThatCanComplete: new[] { "card" });

        var result = Check(concrete, abstraction);

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(
            result.Trace.Select(item => item.AbstractCandidates.Count),
            Is.EqualTo(new[] { 1, 2, 1, 0 }));
    }

    private static RefinementCheckingResult Check(
        StateGraphNode concrete,
        StateGraphNode abstraction)
        => Refinement
            .Between<ImplementationOrder, SpecificationOrder>(
                concrete,
                abstraction)
            .Corresponds((implementation, specification) =>
                implementation.Stage == specification.Stage &&
                (implementation.ResolvedGateway == null ||
                    implementation.ResolvedGateway ==
                        specification.ChosenGateway))
            .Check();

    private static StateGraphNode ExploreConcrete(params string[] gateways)
        => StateGraph.ExploreStateGraph(
            new IStepFunction[] { new ImplementationWorkflow(gateways) },
            new ImplementationOrder { Stage = OrderStage.New },
            lazy: true);

    private static StateGraphNode ExploreAbstract(
        string[] choices,
        string[] choicesThatCanComplete)
        => StateGraph.ExploreStateGraph(
            new IStepFunction[]
            {
                new SpecificationWorkflow(choices, choicesThatCanComplete)
            },
            new SpecificationOrder { Stage = OrderStage.New },
            lazy: true);
}
