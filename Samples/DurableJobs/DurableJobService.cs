namespace DurableJobs;

/// <summary>The service worker lifecycle, mirroring <see cref="WorkerPhase"/>.</summary>
public enum WorkerLifecycle
{
    Idle,
    Running,
    Crashed,
    Stopped
}

/// <summary>A lease token returned by the deterministic worker control surface.</summary>
public sealed record WorkerLease(
    string JobId,
    string Payload,
    int Attempt,
    int Token);

/// <summary>A white-box snapshot used only to demonstrate structural mirroring.</summary>
public sealed record DurableJobDiagnostics(
    JobResponse Job,
    int QueueDepth,
    int Attempts,
    int? LeaseToken,
    WorkerLifecycle Worker,
    int Crashes);

/// <summary>
/// A real thread-safe in-memory service: durable row, dispatch queue, lease,
/// bounded retry, crash/expiry/restart controls, and atomic terminal writes.
/// </summary>
public sealed class DurableJobService
{
    private readonly object gate = new();
    private readonly object driverGate = new();
    private readonly Queue<string> dispatch = new();

    private JobRow? row;
    private WorkerLease? activeLease;
    private WorkerLifecycle worker = WorkerLifecycle.Idle;
    private int crashes;

    public JobResponse Submit(SubmitJobRequest request)
    {
        lock (gate)
        {
            if (row is null)
            {
                row = new JobRow
                {
                    JobId = request.JobId,
                    Payload = request.Payload,
                    Status = JobStatus.Pending
                };
                dispatch.Enqueue(request.JobId);
            }

            return ToResponse(row, request.JobId);
        }
    }

    public JobResponse Get(string jobId)
    {
        lock (gate)
        {
            return ToResponse(row, jobId);
        }
    }

    public JobResponse Cancel(string jobId)
    {
        lock (gate)
        {
            if (row is not null &&
                row.JobId == jobId &&
                row.Status == JobStatus.Pending)
            {
                row.Status = JobStatus.Cancelled;
                row.Result = null;
                row.Error = null;
                dispatch.Clear();
                activeLease = null;
                worker = WorkerLifecycle.Idle;
            }

            return ToResponse(row, jobId);
        }
    }

    public void EnqueueDuplicate()
    {
        lock (gate)
        {
            if (row is not null &&
                row.Status == JobStatus.Pending &&
                dispatch.Count < 2)
            {
                dispatch.Enqueue(row.JobId);
            }
        }
    }

    public void LoseDispatch()
    {
        lock (gate)
        {
            if (row is not null &&
                row.Status == JobStatus.Pending &&
                worker == WorkerLifecycle.Idle &&
                activeLease is null &&
                row.Attempts < DurableJobFixture.MaxAttempts)
            {
                dispatch.Clear();
            }
        }
    }

    public void RebuildDispatch()
    {
        lock (gate)
        {
            if (row is not null &&
                row.Status == JobStatus.Pending &&
                worker == WorkerLifecycle.Idle &&
                activeLease is null &&
                row.Attempts < DurableJobFixture.MaxAttempts &&
                dispatch.Count == 0)
            {
                dispatch.Enqueue(row.JobId);
            }
        }
    }

    public WorkerLease? ClaimNext()
    {
        lock (gate)
        {
            if (row is null ||
                row.Status != JobStatus.Pending ||
                worker != WorkerLifecycle.Idle ||
                activeLease is not null ||
                row.Attempts >= DurableJobFixture.MaxAttempts)
            {
                return null;
            }

            while (dispatch.Count > 0)
            {
                var jobId = dispatch.Dequeue();
                if (jobId != row.JobId)
                {
                    continue;
                }

                row.Attempts++;
                activeLease = new WorkerLease(
                    row.JobId,
                    row.Payload,
                    row.Attempts,
                    row.Attempts);
                worker = WorkerLifecycle.Running;
                return activeLease;
            }

            return null;
        }
    }

    public JobResponse CompleteSuccess(WorkerLease lease, string result)
    {
        lock (gate)
        {
            if (OwnsActiveLease(lease) &&
                row is not null &&
                row.Status == JobStatus.Pending)
            {
                row.Status = JobStatus.Succeeded;
                row.Result = result;
                row.Error = null;
                ClearWork();
            }

            return ToResponse(row, lease.JobId);
        }
    }

    public JobResponse FailAttempt(WorkerLease lease, string error)
    {
        lock (gate)
        {
            if (!OwnsActiveLease(lease) ||
                row is null ||
                row.Status != JobStatus.Pending)
            {
                return ToResponse(row, lease.JobId);
            }

            if (row.Attempts < DurableJobFixture.MaxAttempts)
            {
                activeLease = null;
                worker = WorkerLifecycle.Idle;
                EnsureDispatch();
            }
            else
            {
                row.Status = JobStatus.Failed;
                row.Result = null;
                row.Error = error;
                ClearWork();
            }

            return ToResponse(row, lease.JobId);
        }
    }

    public bool CrashWorker(WorkerLease lease)
    {
        lock (gate)
        {
            if (!OwnsActiveLease(lease) ||
                row is null ||
                row.Status != JobStatus.Pending ||
                crashes >= DurableJobFixture.MaxCrashes)
            {
                return false;
            }

            crashes++;
            worker = WorkerLifecycle.Crashed;
            return true;
        }
    }

    public JobResponse ExpireLease(string jobId)
    {
        lock (gate)
        {
            if (row is not null &&
                row.JobId == jobId &&
                row.Status == JobStatus.Pending &&
                worker == WorkerLifecycle.Crashed &&
                activeLease is not null)
            {
                activeLease = null;
                worker = WorkerLifecycle.Stopped;

                if (row.Attempts == DurableJobFixture.MaxAttempts)
                {
                    row.Status = JobStatus.Failed;
                    row.Result = null;
                    row.Error = DurableJobFixture.Error;
                    dispatch.Clear();
                }
                else
                {
                    EnsureDispatch();
                }
            }

            return ToResponse(row, jobId);
        }
    }

    public void RestartWorker()
    {
        lock (gate)
        {
            if (worker == WorkerLifecycle.Stopped)
            {
                worker = WorkerLifecycle.Idle;
            }
        }
    }

    /// <summary>Drives one real claim and successful worker completion.</summary>
    public JobResponse DriveSuccess(CompleteJobRequest request)
    {
        lock (driverGate)
        {
            if (!Get(request.JobId).Found)
            {
                return JobResponse.Missing(request.JobId);
            }

            var lease = ClaimNext();
            return lease is null
                ? Get(request.JobId)
                : CompleteSuccess(lease, request.Result);
        }
    }

    /// <summary>Drives bounded retry attempts until the real service reaches failure.</summary>
    public JobResponse DriveFailure(FailJobRequest request)
    {
        lock (driverGate)
        {
            if (!Get(request.JobId).Found)
            {
                return JobResponse.Missing(request.JobId);
            }

            while (true)
            {
                var lease = ClaimNext();
                if (lease is null)
                {
                    return Get(request.JobId);
                }

                var response = FailAttempt(lease, request.Error);
                if (response.Status != JobStatus.Pending)
                {
                    return response;
                }
            }
        }
    }

    public DurableJobDiagnostics Diagnostics(string jobId)
    {
        lock (gate)
        {
            return new DurableJobDiagnostics(
                ToResponse(row, jobId),
                dispatch.Count,
                row?.Attempts ?? 0,
                activeLease?.Token,
                worker,
                crashes);
        }
    }

    public void Reset()
    {
        lock (driverGate)
        {
            lock (gate)
            {
                row = null;
                activeLease = null;
                dispatch.Clear();
                worker = WorkerLifecycle.Idle;
                crashes = 0;
            }
        }
    }

    private bool OwnsActiveLease(WorkerLease lease)
        => activeLease == lease &&
            worker == WorkerLifecycle.Running;

    private void EnsureDispatch()
    {
        if (row is not null &&
            row.Status == JobStatus.Pending &&
            dispatch.Count == 0)
        {
            dispatch.Enqueue(row.JobId);
        }
    }

    private void ClearWork()
    {
        dispatch.Clear();
        activeLease = null;
        worker = WorkerLifecycle.Idle;
    }

    private static JobResponse ToResponse(JobRow? current, string requestedJobId)
        => current is null || current.JobId != requestedJobId
            ? JobResponse.Missing(requestedJobId)
            : new JobResponse(
                true,
                current.JobId,
                current.Status,
                current.Payload,
                current.Result,
                current.Error);

    private sealed class JobRow
    {
        public required string JobId { get; init; }

        public required string Payload { get; init; }

        public JobStatus Status { get; set; }

        public string? Result { get; set; }

        public string? Error { get; set; }

        public int Attempts { get; set; }
    }
}
