namespace DurableJobs;

/// <summary>The fixed finite workload used throughout the case study.</summary>
public static class DurableJobFixture
{
    public const string JobId = "job-1";
    public const string OtherJobId = "job-2";
    public const string Payload = "alpha";
    public const string Result = "ALPHA";
    public const string Error = "worker-error";
    public const int MaxAttempts = 2;
    public const int MaxCrashes = 1;

    public static SubmitJobRequest Submit { get; } = new(JobId, Payload);
    public static CompleteJobRequest Complete { get; } = new(JobId, Result);
    public static FailJobRequest Fail { get; } = new(JobId, Error);
    public static CompleteJobRequest CompleteOther { get; } = new(OtherJobId, Result);
    public static FailJobRequest FailOther { get; } = new(OtherJobId, Error);
}

/// <summary>The client-visible lifecycle of the one bounded job.</summary>
public enum JobStatus
{
    Missing,
    Pending,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record SubmitJobRequest(string JobId, string Payload);

public sealed record CompleteJobRequest(string JobId, string Result);

public sealed record FailJobRequest(string JobId, string Error);

/// <summary>A domain/API response. Missing is represented explicitly rather than by null.</summary>
public sealed record JobResponse(
    bool Found,
    string JobId,
    JobStatus Status,
    string? Payload,
    string? Result,
    string? Error)
{
    public bool IsTerminal =>
        Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled;

    public static JobResponse Missing(string jobId)
        => new(false, jobId, JobStatus.Missing, null, null, null);
}
