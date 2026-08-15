namespace DurableJobs;

using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Operations;

/// <summary>The atomic domain contract used for checking and conformance.</summary>
[State]
public partial class AtomicJobState
{
    public string? JobId { get; set; }

    public string? Payload { get; set; }

    public JobStatus Status { get; set; }

    public string? Result { get; set; }

    public string? Error { get; set; }
}

public sealed class SubmitJobOperation :
    Operation<SubmitJobRequest, JobResponse, AtomicJobState>
{
    public SubmitJobOperation() : base("SubmitJob")
    {
    }

    public override ExpectedOutcomes Apply(
        SubmitJobRequest request,
        AtomicJobState state)
    {
        if (state.Status != JobStatus.Missing)
        {
            return Respond(AtomicJobContract.Response(state, request.JobId));
        }

        var response = new JobResponse(
            true,
            request.JobId,
            JobStatus.Pending,
            request.Payload,
            null,
            null);

        return Expect.That(
                actual => actual == response,
                "a new job is durably accepted as Pending")
            .ThenState(
                (_, next) =>
                {
                    next.JobId = request.JobId;
                    next.Payload = request.Payload;
                    next.Status = JobStatus.Pending;
                    next.Result = null;
                    next.Error = null;
                },
                mock: () => response);
    }

    private ExpectedOutcomes Respond(JobResponse response)
        => Expect.That(
                actual => actual == response,
                "duplicate submit returns the existing logical job")
            .ThenState((_, _) => { }, mock: () => response);
}

public sealed class GetJobOperation :
    Operation<string, JobResponse, AtomicJobState>
{
    public GetJobOperation() : base("GetJob")
    {
    }

    public override ExpectedOutcomes Apply(string jobId, AtomicJobState state)
    {
        var response = AtomicJobContract.Response(state, jobId);
        return Expect.That(
                actual => actual == response,
                "Get reports the complete current contract state")
            .ThenState((_, _) => { }, mock: () => response);
    }
}

public sealed class CancelJobOperation :
    Operation<string, JobResponse, AtomicJobState>
{
    public CancelJobOperation() : base("CancelJob")
    {
    }

    public override ExpectedOutcomes Apply(string jobId, AtomicJobState state)
    {
        if (state.Status != JobStatus.Pending || state.JobId != jobId)
        {
            var stable = AtomicJobContract.Response(state, jobId);
            return Expect.That(
                    actual => actual == stable,
                    "cancelling a missing or terminal job is idempotent")
                .ThenState((_, _) => { }, mock: () => stable);
        }

        var cancelled = new JobResponse(
            true,
            jobId,
            JobStatus.Cancelled,
            state.Payload,
            null,
            null);

        return Expect.That(
                actual => actual == cancelled,
                "cancellation atomically wins while the job is Pending")
            .ThenState(
                (_, next) =>
                {
                    next.Status = JobStatus.Cancelled;
                    next.Result = null;
                    next.Error = null;
                },
                mock: () => cancelled);
    }
}

public sealed class CompleteSuccessOperation :
    Operation<CompleteJobRequest, JobResponse, AtomicJobState>
{
    public CompleteSuccessOperation() : base("CompleteSuccess")
    {
    }

    public override ExpectedOutcomes Apply(
        CompleteJobRequest request,
        AtomicJobState state)
    {
        if (state.Status != JobStatus.Pending || state.JobId != request.JobId)
        {
            var stable = AtomicJobContract.Response(state, request.JobId);
            return Expect.That(
                    actual => actual == stable,
                    "late or duplicate success cannot change a terminal outcome")
                .ThenState((_, _) => { }, mock: () => stable);
        }

        var succeeded = new JobResponse(
            true,
            request.JobId,
            JobStatus.Succeeded,
            state.Payload,
            request.Result,
            null);

        return Expect.That(
                actual => actual == succeeded,
                "the worker atomically installs the successful result")
            .ThenState(
                (_, next) =>
                {
                    next.Status = JobStatus.Succeeded;
                    next.Result = request.Result;
                    next.Error = null;
                },
                mock: () => succeeded);
    }
}

public sealed class CompleteFailureOperation :
    Operation<FailJobRequest, JobResponse, AtomicJobState>
{
    public CompleteFailureOperation() : base("CompleteFailure")
    {
    }

    public override ExpectedOutcomes Apply(
        FailJobRequest request,
        AtomicJobState state)
    {
        if (state.Status != JobStatus.Pending || state.JobId != request.JobId)
        {
            var stable = AtomicJobContract.Response(state, request.JobId);
            return Expect.That(
                    actual => actual == stable,
                    "late or duplicate failure cannot change a terminal outcome")
                .ThenState((_, _) => { }, mock: () => stable);
        }

        var failed = new JobResponse(
            true,
            request.JobId,
            JobStatus.Failed,
            state.Payload,
            null,
            request.Error);

        return Expect.That(
                actual => actual == failed,
                "the worker atomically installs the terminal error")
            .ThenState(
                (_, next) =>
                {
                    next.Status = JobStatus.Failed;
                    next.Result = null;
                    next.Error = request.Error;
                },
                mock: () => failed);
    }
}

/// <summary>The one contract definition and its five atomic operations.</summary>
public sealed class AtomicJobContract : Spec<AtomicJobState>
{
    public AtomicJobContract()
    {
        Add(SubmitJob);
        Add(GetJob);
        Add(CancelJob);
        Add(CompleteSuccess);
        Add(CompleteFailure);
        WithJsonPrinters();
    }

    public SubmitJobOperation SubmitJob { get; } = new();

    public GetJobOperation GetJob { get; } = new();

    public CancelJobOperation CancelJob { get; } = new();

    public CompleteSuccessOperation CompleteSuccess { get; } = new();

    public CompleteFailureOperation CompleteFailure { get; } = new();

    public static AtomicJobState InitialState()
        => new() { Status = JobStatus.Missing };

    public static JobResponse Response(AtomicJobState state, string requestedJobId)
        => state.Status == JobStatus.Missing || state.JobId != requestedJobId
            ? JobResponse.Missing(requestedJobId)
            : new JobResponse(
                true,
                state.JobId!,
                state.Status,
                state.Payload,
                state.Result,
                state.Error);
}

/// <summary>Compiles the contract operations to an ordinary model-checking graph.</summary>
public static class AtomicJobModel
{
    public const string SubmitAction = "submit";
    public const string GetAction = "get";
    public const string CancelAction = "cancel";
    public const string CompleteSuccessAction = "complete-success";
    public const string CompleteFailureAction = "complete-failure";

    public static IReadOnlyList<OperationModelStep> Steps()
    {
        var contract = new AtomicJobContract();
        return
        [
            new OperationModelStep(
                contract.SubmitJob.With(DurableJobFixture.Submit, SubmitAction),
                repeat: true),
            new OperationModelStep(
                contract.GetJob.With(DurableJobFixture.JobId, GetAction),
                repeat: true),
            new OperationModelStep(
                contract.CancelJob.With(DurableJobFixture.JobId, CancelAction),
                repeat: true),
            new OperationModelStep(
                contract.CompleteSuccess.With(
                    DurableJobFixture.Complete,
                    CompleteSuccessAction),
                repeat: true),
            new OperationModelStep(
                contract.CompleteFailure.With(
                    DurableJobFixture.Fail,
                    CompleteFailureAction),
                repeat: true)
        ];
    }

    public static StateGraphNode Explore()
        => OperationModel.Explore(AtomicJobContract.InitialState(), Steps());

    public static string StepId(string action) => $"operation:{action}";

    public static Func<IStepFunction, bool> Any(params string[] actions)
        => step => actions.Any(action => step.StepFunctionId == StepId(action));

    public static Fairness CompletionFairness { get; } =
        Fairness.Weak(Any(CompleteSuccessAction, CompleteFailureAction));
}
