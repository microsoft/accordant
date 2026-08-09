namespace RelationalRefinement;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;

public enum OrderStage
{
    New,
    Sent,
    Authorized,
    Completed
}

[State]
public partial class ImplementationOrder
{
    public OrderStage Stage { get; set; }
    public string ResolvedGateway { get; set; }
}

[State]
public partial class SpecificationOrder
{
    public OrderStage Stage { get; set; }
    public string ChosenGateway { get; set; }
}

public sealed class ImplementationWorkflow : BaseStepFunction
{
    private readonly IReadOnlyList<string> gateways;

    public ImplementationWorkflow(params string[] gateways)
    {
        this.gateways = gateways;
    }

    public override string StepFunctionId => "implementation-workflow";

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var order = (ImplementationOrder)state;
        switch (order.Stage)
        {
            case OrderStage.New:
                return One(new ImplementationOrder
                {
                    Stage = OrderStage.Sent
                });

            case OrderStage.Sent:
                return gateways
                    .Select(gateway => Result(new ImplementationOrder
                    {
                        Stage = OrderStage.Authorized,
                        ResolvedGateway = gateway
                    }))
                    .ToArray();

            case OrderStage.Authorized:
                return One(new ImplementationOrder
                {
                    Stage = OrderStage.Completed,
                    ResolvedGateway = order.ResolvedGateway
                });

            default:
                return null;
        }
    }

    private IList<StepResult> One(ImplementationOrder state)
        => new[] { Result(state) };

    private StepResult Result(ImplementationOrder state)
        => new StepResult
        {
            State = state,
            StepFunctions = new IStepFunction[] { this }
        };
}

public sealed class SpecificationWorkflow : BaseStepFunction
{
    private readonly IReadOnlyList<string> gateways;
    private readonly ISet<string> gatewaysThatCanComplete;

    public SpecificationWorkflow(
        IEnumerable<string> gateways,
        IEnumerable<string> gatewaysThatCanComplete)
    {
        this.gateways = gateways.ToArray();
        this.gatewaysThatCanComplete =
            new HashSet<string>(gatewaysThatCanComplete);
    }

    public override string StepFunctionId => "specification-workflow";

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var order = (SpecificationOrder)state;
        switch (order.Stage)
        {
            case OrderStage.New:
                return gateways
                    .Select(gateway => Result(new SpecificationOrder
                    {
                        Stage = OrderStage.Sent,
                        ChosenGateway = gateway
                    }))
                    .ToArray();

            case OrderStage.Sent:
                return One(new SpecificationOrder
                {
                    Stage = OrderStage.Authorized,
                    ChosenGateway = order.ChosenGateway
                });

            case OrderStage.Authorized
                when gatewaysThatCanComplete.Contains(order.ChosenGateway):
                return One(new SpecificationOrder
                {
                    Stage = OrderStage.Completed,
                    ChosenGateway = order.ChosenGateway
                });

            default:
                return null;
        }
    }

    private IList<StepResult> One(SpecificationOrder state)
        => new[] { Result(state) };

    private StepResult Result(SpecificationOrder state)
        => new StepResult
        {
            State = state,
            StepFunctions = new IStepFunction[] { this }
        };
}
