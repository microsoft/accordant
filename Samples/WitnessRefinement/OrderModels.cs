namespace WitnessRefinement;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;

public enum OrderStage
{
    New,
    Dispatched,
    PaymentSettled,
    FraudSettled,
    Completed
}

/// <summary>
/// The implementation. It dispatches two independent checks and learns each
/// verdict only when that response arrives. It never records which operator
/// dispatched the order.
/// </summary>
[State]
public partial class ImplementationOrder
{
    public OrderStage Stage { get; set; }
    public string PaymentResult { get; set; }
    public string FraudResult { get; set; }
}

/// <summary>
/// The specification. It commits to the operator and to both verdicts at the
/// moment the order is dispatched.
/// </summary>
[State]
public partial class SpecificationOrder
{
    public OrderStage Stage { get; set; }
    public string Operator { get; set; }
    public string PaymentOutcome { get; set; }
    public string FraudOutcome { get; set; }
}

/// <summary>
/// Deterministic checker-local memory of a fact the concrete past already
/// decided: who dispatched the order.
/// </summary>
[State]
public partial class DispatcherAuxiliary
{
    public string Operator { get; set; }
}

/// <summary>The predicted result of the pending payment authorization.</summary>
[State]
public partial class PaymentWitness
{
    public string Verdict { get; set; }
}

/// <summary>The predicted result of the pending fraud check.</summary>
[State]
public partial class FraudWitness
{
    public string Verdict { get; set; }
}

public sealed class ImplementationWorkflow : BaseStepFunction
{
    private readonly IReadOnlyList<string> operators;
    private readonly IReadOnlyList<string> paymentResults;
    private readonly IReadOnlyList<string> fraudResults;

    public ImplementationWorkflow(
        IEnumerable<string> operators,
        IEnumerable<string> paymentResults,
        IEnumerable<string> fraudResults)
    {
        this.operators = operators.ToArray();
        this.paymentResults = paymentResults.ToArray();
        this.fraudResults = fraudResults.ToArray();
    }

    public override string StepFunctionId => "implementation-workflow";

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var order = (ImplementationOrder)state;
        switch (order.Stage)
        {
            case OrderStage.New:
                return operators
                    .Select(dispatcher => Result(
                        new ImplementationOrder
                        {
                            Stage = OrderStage.Dispatched
                        },
                        dispatcher))
                    .ToArray();

            case OrderStage.Dispatched:
                return paymentResults
                    .Select(payment => Result(new ImplementationOrder
                    {
                        Stage = OrderStage.PaymentSettled,
                        PaymentResult = payment
                    }))
                    .Concat(fraudResults.Select(fraud => Result(
                        new ImplementationOrder
                        {
                            Stage = OrderStage.FraudSettled,
                            FraudResult = fraud
                        })))
                    .ToArray();

            case OrderStage.PaymentSettled:
                return fraudResults
                    .Select(fraud => Result(new ImplementationOrder
                    {
                        Stage = OrderStage.Completed,
                        PaymentResult = order.PaymentResult,
                        FraudResult = fraud
                    }))
                    .ToArray();

            case OrderStage.FraudSettled:
                return paymentResults
                    .Select(payment => Result(new ImplementationOrder
                    {
                        Stage = OrderStage.Completed,
                        PaymentResult = payment,
                        FraudResult = order.FraudResult
                    }))
                    .ToArray();

            default:
                return null;
        }
    }

    private StepResult Result(
        ImplementationOrder state,
        string dispatcher = null)
        => new StepResult
        {
            State = state,
            StepFunctions = new IStepFunction[] { this },
            EdgeMetadata = dispatcher
        };
}

public sealed class SpecificationWorkflow : BaseStepFunction
{
    private readonly IReadOnlyList<string> operators;
    private readonly IReadOnlyList<string> paymentOutcomes;
    private readonly IReadOnlyList<string> fraudOutcomes;

    public SpecificationWorkflow(
        IEnumerable<string> operators,
        IEnumerable<string> paymentOutcomes,
        IEnumerable<string> fraudOutcomes)
    {
        this.operators = operators.ToArray();
        this.paymentOutcomes = paymentOutcomes.ToArray();
        this.fraudOutcomes = fraudOutcomes.ToArray();
    }

    public override string StepFunctionId => "specification-workflow";

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var order = (SpecificationOrder)state;
        switch (order.Stage)
        {
            case OrderStage.New:
                return (from dispatcher in operators
                        from payment in paymentOutcomes
                        from fraud in fraudOutcomes
                        select Result(new SpecificationOrder
                        {
                            Stage = OrderStage.Dispatched,
                            Operator = dispatcher,
                            PaymentOutcome = payment,
                            FraudOutcome = fraud
                        }))
                    .ToArray();

            case OrderStage.Dispatched:
                return new[]
                {
                    Result(Advance(order, OrderStage.PaymentSettled)),
                    Result(Advance(order, OrderStage.FraudSettled))
                };

            case OrderStage.PaymentSettled:
            case OrderStage.FraudSettled:
                return new[] { Result(Advance(order, OrderStage.Completed)) };

            default:
                return null;
        }
    }

    private static SpecificationOrder Advance(
        SpecificationOrder order,
        OrderStage stage)
        => new SpecificationOrder
        {
            Stage = stage,
            Operator = order.Operator,
            PaymentOutcome = order.PaymentOutcome,
            FraudOutcome = order.FraudOutcome
        };

    private StepResult Result(SpecificationOrder state)
        => new StepResult
        {
            State = state,
            StepFunctions = new IStepFunction[] { this }
        };
}
