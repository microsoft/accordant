namespace TemporalRefinement;

using System.Collections.Generic;
using Microsoft.Accordant;

public enum WorkerStage
{
    Idle,
    Queued,
    ProcessingA,
    ProcessingB,
    Completed
}

public enum JobStage
{
    Idle,
    Pending,
    Completed
}

[State]
public partial class WorkerState
{
    public WorkerStage Stage { get; set; }
}

[State]
public partial class JobState
{
    public JobStage Stage { get; set; }
}

public abstract class WorkerStep : BaseStepFunction
{
    protected abstract WorkerState Next(WorkerState state);

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var next = Next((WorkerState)state);
        return next == null
            ? null
            : new[]
            {
                new StepResult
                {
                    State = next,
                    StepFunctions = new IStepFunction[] { this }
                }
            };
    }
}

public sealed class SubmitWork : WorkerStep
{
    public override string StepFunctionId => "submit-work";

    protected override WorkerState Next(WorkerState state)
        => state.Stage == WorkerStage.Idle
            ? new WorkerState { Stage = WorkerStage.Queued }
            : null;
}

public sealed class StartWork : WorkerStep
{
    public override string StepFunctionId => "start-work";

    protected override WorkerState Next(WorkerState state)
        => state.Stage == WorkerStage.Queued
            ? new WorkerState { Stage = WorkerStage.ProcessingA }
            : null;
}

public sealed class PollWork : WorkerStep
{
    public override string StepFunctionId => "poll-work";

    protected override WorkerState Next(WorkerState state)
        => state.Stage == WorkerStage.ProcessingA
            ? new WorkerState { Stage = WorkerStage.ProcessingB }
            : state.Stage == WorkerStage.ProcessingB
                ? new WorkerState { Stage = WorkerStage.ProcessingA }
                : null;
}

public sealed class CompleteWork : WorkerStep
{
    public override string StepFunctionId => "complete-work";

    protected override WorkerState Next(WorkerState state)
        => state.Stage == WorkerStage.ProcessingA ||
            state.Stage == WorkerStage.ProcessingB
            ? new WorkerState { Stage = WorkerStage.Completed }
            : null;
}

public abstract class JobStep : BaseStepFunction
{
    protected abstract JobState Next(JobState state);

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var next = Next((JobState)state);
        return next == null
            ? null
            : new[]
            {
                new StepResult
                {
                    State = next,
                    StepFunctions = new IStepFunction[] { this }
                }
            };
    }
}

public sealed class SubmitJob : JobStep
{
    public override string StepFunctionId => "submit-job";

    protected override JobState Next(JobState state)
        => state.Stage == JobStage.Idle
            ? new JobState { Stage = JobStage.Pending }
            : null;
}

public sealed class CompleteJob : JobStep
{
    public override string StepFunctionId => "complete-job";

    protected override JobState Next(JobState state)
        => state.Stage == JobStage.Pending
            ? new JobState { Stage = JobStage.Completed }
            : null;
}
