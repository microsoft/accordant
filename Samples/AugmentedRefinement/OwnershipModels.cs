namespace AugmentedRefinement;

using System.Collections.Generic;
using Microsoft.Accordant;

public enum ConcreteStage
{
    Unclaimed,
    Active,
    Completed
}

public enum AbstractStage
{
    Unclaimed,
    Owned,
    Completed
}

[State]
public partial class ConcreteResource
{
    public ConcreteStage Stage { get; set; }
}

[State]
public partial class AbstractResource
{
    public AbstractStage Stage { get; set; }
    public string Owner { get; set; }
}

[State]
public partial class OwnershipAuxiliary
{
    public string Owner { get; set; }
}

public sealed class ClaimConcrete : BaseStepFunction
{
    public ClaimConcrete(string owner)
    {
        Owner = owner;
    }

    public string Owner { get; }
    public override string StepFunctionId => $"claim-concrete-{Owner}";

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var resource = (ConcreteResource)state;
        if (resource.Stage != ConcreteStage.Unclaimed)
        {
            return null;
        }

        return new[]
        {
            new StepResult
            {
                State = new ConcreteResource { Stage = ConcreteStage.Active },
                StepFunctions = new IStepFunction[] { this }
            }
        };
    }
}

public sealed class CompleteConcrete : BaseStepFunction
{
    public override string StepFunctionId => "complete-concrete";

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var resource = (ConcreteResource)state;
        if (resource.Stage != ConcreteStage.Active)
        {
            return null;
        }

        return new[]
        {
            new StepResult
            {
                State = new ConcreteResource { Stage = ConcreteStage.Completed },
                StepFunctions = new IStepFunction[] { this }
            }
        };
    }
}

public sealed class ClaimAbstract : BaseStepFunction
{
    public ClaimAbstract(string owner)
    {
        Owner = owner;
    }

    public string Owner { get; }
    public override string StepFunctionId => $"claim-abstract-{Owner}";

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var resource = (AbstractResource)state;
        if (resource.Stage != AbstractStage.Unclaimed)
        {
            return null;
        }

        return new[]
        {
            new StepResult
            {
                State = new AbstractResource
                {
                    Stage = AbstractStage.Owned,
                    Owner = Owner
                },
                StepFunctions = new IStepFunction[] { this }
            }
        };
    }
}

public sealed class CompleteAbstract : BaseStepFunction
{
    public override string StepFunctionId => "complete-abstract";

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var resource = (AbstractResource)state;
        if (resource.Stage != AbstractStage.Owned)
        {
            return null;
        }

        return new[]
        {
            new StepResult
            {
                State = new AbstractResource
                {
                    Stage = AbstractStage.Completed,
                    Owner = resource.Owner
                },
                StepFunctions = new IStepFunction[] { this }
            }
        };
    }
}
