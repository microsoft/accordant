namespace DurableJobs;

using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>The direct projection from process design to atomic contract.</summary>
public static class DurableJobRefinement
{
    public static FunctionalRefinementCheck<DurableJobDesignState, AtomicJobState> Build(
        DesignOptions? options = null,
        bool lazy = false)
        => Refinement
            .Between<DurableJobDesignState, AtomicJobState>(
                DurableJobDesign.Explore(options, lazy),
                AtomicJobModel.Explore())
            .Map(ToContract)
            .MapTransition(Declarations);

    public static AtomicJobState ToContract(DurableJobDesignState design)
        => new()
        {
            JobId = design.JobId,
            Payload = design.Payload,
            Status = design.Status,
            Result = design.Result,
            Error = design.Error
        };

    public static AbstractResponse Declarations(
        RefinementTransition<DurableJobDesignState> transition)
    {
        if (transition.Metadata is not ProcessTransition process)
        {
            throw new InvalidOperationException(
                "The detailed graph must retain ProcessTransition metadata.");
        }

        return Declare(process, transition.Source);
    }

    public static AbstractResponse Declare(
        ProcessTransition process,
        DurableJobDesignState source)
    {
        if (process.IsControl)
        {
            return process.Control switch
            {
                ProcessControlKind.Crash or
                ProcessControlKind.Restart or
                ProcessControlKind.Launch or
                ProcessControlKind.Completion => AbstractResponse.Hidden,
                _ => AbstractResponse.Unconstrained
            };
        }

        if (process.SemanticAction is not DesignAction action)
        {
            throw new InvalidOperationException(
                $"Process transition '{process}' has no durable-job action.");
        }

        return action switch
        {
            DesignAction.Submit => Performs(AtomicJobModel.SubmitAction),
            DesignAction.Get => Performs(AtomicJobModel.GetAction),
            DesignAction.Cancel => Performs(AtomicJobModel.CancelAction),
            DesignAction.CompleteSuccess => Performs(
                AtomicJobModel.CompleteSuccessAction),
            DesignAction.FailAttempt
                when source.Attempts == DurableJobFixture.MaxAttempts
                => Performs(AtomicJobModel.CompleteFailureAction),
            DesignAction.ExpireLease
                when source.Attempts == DurableJobFixture.MaxAttempts
                => Performs(AtomicJobModel.CompleteFailureAction),

            DesignAction.EnqueueDuplicate or
            DesignAction.LoseDispatch or
            DesignAction.RebuildDispatch or
            DesignAction.ClaimNext or
            DesignAction.RecordSuccess or
            DesignAction.RecordFailure or
            DesignAction.FailAttempt or
            DesignAction.AbandonAttempt or
            DesignAction.ExpireLease or
            DesignAction.LoseAcceptedJob => AbstractResponse.Hidden,

            _ => AbstractResponse.Unconstrained
        };
    }

    private static AbstractResponse Performs(string action)
        => AbstractResponse.Step(
            step => step.StepFunctionId == AtomicJobModel.StepId(action),
            AtomicJobModel.StepId(action));
}
