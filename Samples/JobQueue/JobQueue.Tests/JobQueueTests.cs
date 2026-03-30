// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobQueue.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant;
using NUnit.Framework;
using JobQueue.Api.Contracts;
using JobQueue.Api.Controllers;

/// <summary>
/// Accordant tests for the JobQueue REST API.
/// 
/// This sample demonstrates ASYNC OPERATIONS with step functions:
/// - CreateJob returns immediately with Pending status
/// - Background processing eventually completes (or fails)
/// - GetJob polls for completion, capturing ResultPath when done
/// - Temporal properties: ResultPath is null while Pending, stable once Completed
/// 
/// Key concepts:
/// - AsyncOperation.Create for inline step function definitions
/// - Response-dependent state to capture server-generated ResultPath
/// - Modeling status transitions and stability properties
/// - PollingSetup with derivations for async operation handling
/// </summary>
[TestFixture]
public class JobQueueTests
{
    // ============================================================
    // State Definition
    // ============================================================

    /// <summary>
    /// State tracks jobs and their status.
    /// </summary>
    public class JobQueueState : JsonState
    {
        public Dictionary<string, JobState> Jobs { get; set; } = new();

        public class JobState
        {
            public JobStatus Status { get; set; }
            
            /// <summary>
            /// Path to the result - null while Pending, server-generated once Completed.
            /// Once set, this value is stable and never changes.
            /// </summary>
            public string? ResultPath { get; set; }
        }
    }

    // ============================================================
    // Operations (Class-based to support Polling)
    // ============================================================

    /// <summary>
    /// CREATE JOB operation - triggers async processing.
    /// Has Polling setup to enable automatic polling via GetJob.
    /// </summary>
    public class CreateJobOperation : Operation<string, ApiResult<Job>, JobQueueState>
    {
        public CreateJobOperation() : base("CreateJob") { }

        /// <summary>
        /// Polling setup for async job processing.
        /// Uses GetJob operation with a derivation that passes the jobId.
        /// </summary>
        public override PollingSetup Polling => new PollingSetup
        {
            Operation = "GetJob",
            WaitTimeInMs = 100,   // Poll every 100ms
            MaxRetryCount = 100   // Try up to 100 times (10 seconds max)
        };

        /// <summary>
        /// Check if job has reached terminal state (no longer Pending).
        /// Reused by both the step function and manual polling loops.
        /// </summary>
        public static bool IsTerminal(JobQueueState state, string jobId) =>
            !state.Jobs.ContainsKey(jobId) || state.Jobs[jobId].Status != JobStatus.Pending;

        public override ExpectedOutcomes Apply(string jobId, JobQueueState state)
        {
            if (state.Jobs.ContainsKey(jobId))
            {
                return Expect.That(r => r.IsConflict,
                           $"Should return 409 Conflict because job '{jobId}' already exists")
                       .SameState();
            }

            return Expect.That(
                       r => r.IsSuccess &&
                            r.Data != null &&
                            r.Data.JobId == jobId &&
                            r.Data.Status == JobStatus.Pending &&
                            r.Data.ResultPath == null,  // Must be null when pending!
                       $"Should return 200 OK with job '{jobId}' in Pending status")
                   .ThenState(nextState => nextState.Jobs[jobId] = new JobQueueState.JobState 
                   { 
                       Status = JobStatus.Pending, 
                       ResultPath = null 
                   })
                   .Triggers(AsyncOperation.Create<JobQueueState>(
                       // Step function terminates when job is no longer Pending
                       // (either completed, failed, or deleted)
                       // Reuse the same predicate defined above
                       isTerminal: s => IsTerminal(s, jobId),
                       // Non-deterministic outcomes: success or failure
                       // Note: Only Status changes here. ResultPath stays unchanged
                       // because it's server-generated. It will be captured via 
                       // response-dependent state when GetJob first observes Completed.
                       transitions: new Action<JobQueueState>[]
                       {
                           next => next.Jobs[jobId].Status = JobStatus.Completed,
                           next => next.Jobs[jobId].Status = JobStatus.Failed
                       },
                       name: $"ProcessJob({jobId})"
                   ));
        }

        public override async Task<ApiResult<Job>> ExecuteAsync(
            TestingContext context, string jobId)
        {
            var client = context.Get<JobQueueApiClient>();
            return await client.CreateJobAsync(jobId);
        }
    }

    /// <summary>
    /// GET JOB operation - used for polling async job status.
    /// Captures ResultPath when job transitions to Completed.
    /// </summary>
    public class GetJobOperation : Operation<string, ApiResult<Job>, JobQueueState>
    {
        public GetJobOperation() : base("GetJob") { }

        /// <summary>
        /// Derivation for polling: GetJob's request (jobId) is the same as CreateJob's request.
        /// </summary>
        public override IReadOnlyList<RequestDerivation> DerivedFrom => new[]
        {
            Derive.From<string, ApiResult<Job>, string>("CreateJob")
                  .As((req, resp) => req)  // Same jobId
        };

        public override ExpectedOutcomes Apply(string jobId, JobQueueState state)
        {
            if (!state.Jobs.TryGetValue(jobId, out var job))
            {
                return Expect.That(r => r.IsNotFound,
                           $"Should return 404 Not Found because job '{jobId}' doesn't exist")
                       .SameState();
            }

            if (job.Status == JobStatus.Pending)
            {
                // Still pending - ResultPath must be null
                return Expect.That(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.JobId == jobId &&
                                r.Data.Status == JobStatus.Pending &&
                                r.Data.ResultPath == null,
                           $"Should return job '{jobId}' in Pending status with no ResultPath")
                       .SameState();
            }
            else if (job.Status == JobStatus.Completed)
            {
                // Job is completed - now we need to handle ResultPath
                if (job.ResultPath == null)
                {
                    // FIRST OBSERVATION: We see Completed but haven't captured ResultPath yet.
                    // The server generates ResultPath - we don't know it ahead of time.
                    // Use response-dependent state to capture whatever the server returns.
                    return Expect.That(
                               r => r.IsSuccess &&
                                    r.Data != null &&
                                    r.Data.JobId == jobId &&
                                    r.Data.Status == JobStatus.Completed &&
                                    !string.IsNullOrEmpty(r.Data.ResultPath),
                               $"Should return job '{jobId}' as Completed with a ResultPath")
                           .ThenState(
                               (ApiResult<Job> resp, JobQueueState nextState) => {
                                   nextState.Jobs[jobId].ResultPath = resp.Data!.ResultPath;
                               },
                               mock: () => new ApiResult<Job> {
                                   Data = new Job(jobId, JobStatus.Completed, $"/api/jobs/{jobId}/result/mock123"),
                                   StatusCode = 200
                               });
                }
                else
                {
                    // SUBSEQUENT OBSERVATIONS: ResultPath was already captured.
                    // Enforce stability - it must match exactly what we captured before.
                    // This is a temporal property: once set, ResultPath never changes.
                    return Expect.That(
                               r => r.IsSuccess &&
                                    r.Data != null &&
                                    r.Data.JobId == jobId &&
                                    r.Data.Status == JobStatus.Completed &&
                                    r.Data.ResultPath == job.ResultPath,
                               $"Should return job '{jobId}' with stable ResultPath '{job.ResultPath}'")
                           .SameState();
                }
            }
            else // Failed
            {
                return Expect.That(
                           r => r.IsSuccess &&
                                r.Data != null &&
                                r.Data.JobId == jobId &&
                                r.Data.Status == JobStatus.Failed &&
                                r.Data.ResultPath == null,
                           $"Should return job '{jobId}' as Failed with no ResultPath")
                       .SameState();
            }
        }

        public override async Task<ApiResult<Job>> ExecuteAsync(
            TestingContext context, string jobId)
        {
            var client = context.Get<JobQueueApiClient>();
            return await client.GetJobAsync(jobId);
        }
    }

    /// <summary>
    /// DELETE JOB operation - removes a job from the queue.
    /// </summary>
    public class DeleteJobOperation : Operation<string, int, JobQueueState>
    {
        public DeleteJobOperation() : base("DeleteJob") { }

        public override ExpectedOutcomes Apply(string jobId, JobQueueState state)
        {
            if (!state.Jobs.ContainsKey(jobId))
            {
                return Expect.That(s => s == 404,
                           $"Should return 404 Not Found because job '{jobId}' doesn't exist")
                       .SameState();
            }

            return Expect.That(s => s == 204,
                       $"Should return 204 No Content after deleting job '{jobId}'")
                   .ThenState(nextState => nextState.Jobs.Remove(jobId));
        }

        public override async Task<int> ExecuteAsync(TestingContext context, string jobId)
        {
            var client = context.Get<JobQueueApiClient>();
            return await client.DeleteJobAsync(jobId);
        }
    }

    // ============================================================
    // Spec with class-based operations
    // ============================================================

    public class JobQueueSpec : Spec<JobQueueState>
    {
        public CreateJobOperation CreateJob { get; } = new();
        public GetJobOperation GetJob { get; } = new();
        public DeleteJobOperation DeleteJob { get; } = new();

        public JobQueueSpec()
        {
            Add(CreateJob);
            Add(GetJob);
            Add(DeleteJob);
            WithJsonPrinters();
        }
    }

    // ============================================================
    // Test 1: Auto-generated Tests with Step Functions
    // Shows the power of the framework - it handles polling automatically
    // ============================================================

    [Test]
    public async Task SequentialTests_WithAsyncJobProcessing()
    {
        // Fast processing for tests, no failures
        JobsController.ProcessingDelayMs = 10;
        JobsController.FailureProbability = 0;

        using var factory = new JobQueueServiceFactory();
        var spec = new JobQueueSpec();
        var initialState = new JobQueueState();

        var inputs = new InputSet()
        {
            spec.GetJob.With("unknown", "Get unknown job"),
            spec.GetJob.With("job1", "Get job1"),
            spec.DeleteJob.With("job1", "Delete job1"),
            spec.CreateJob.With("job1", "Create job1"),  // Polling is configured on the operation
        };

        var testCases = spec.GenerateTests(
            initialState,
            inputs,
            new TestGenerationOptions
            {
                MaxDepth = 4,
                StateConstraint = state =>
                {
                    var s = (JobQueueState)state;
                    return s.Jobs.Count <= 1;  // Keep state space bounded
                }
            });

        var context = spec.CreateTestingContext();

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async info =>
                {
                    // Register the API client
                    info.Context.Register(new JobQueueApiClient(factory.CreateTestClient()));
                    // Delete is always safe - worker checks if job exists before updating
                    var client = info.Context.Get<JobQueueApiClient>();
                    await client.DeleteJobAsync("job1");
                }
            });

        var failures = results.Where(r => !r.Success).ToList();
        Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
    }

    // ============================================================
    // Test 2: Manual Test - Shows what's happening under the hood
    // Demonstrates explicit polling with spec validation
    // ============================================================

    [Test]
    public async Task ManualTest_AsyncJobFlow()
    {
        // Set fast processing for tests
        JobsController.ProcessingDelayMs = 50;
        JobsController.FailureProbability = 0; // Always succeed

        using var factory = new JobQueueServiceFactory();
        var spec = new JobQueueSpec();
        var initialState = new JobQueueState();

        var context = spec.CreateTestingContext();
        context.Register(new JobQueueApiClient(factory.CreateTestClient()));
        var stateProfile = new StateProfile(initialState);

        // Cleanup any prior state
        await spec.DeleteJob.ExecuteAsync(context, "job1");

        // Helper: execute operation and validate through spec
        async Task<TResp> Allows<TReq, TResp>(
            Operation<TReq, TResp, JobQueueState> op, TReq request)
        {
            var response = await op.ExecuteAsync(context, request);
            var (isValid, message, nextProfile) = spec.Allows(op, request, response, stateProfile);
            Assert.IsTrue(isValid, message);
            stateProfile = nextProfile;
            return response;
        }

        // Helper: check if job is terminal in all possible states
        bool IsTerminal(string jobId) =>
            stateProfile.StatesAndStepFunctions
                .All(ssf => CreateJobOperation.IsTerminal((JobQueueState)ssf.State, jobId));

        // 1. Create job - spec validates it returns Pending with null ResultPath
        await Allows(spec.CreateJob, "job1");

        // 2. Poll until complete - each poll goes through spec validation
        //    Use Polling setup from CreateJob operation for bounds (liveness check)
        var polling = spec.CreateJob.Polling;

        for (int i = 0; i < polling.MaxRetryCount; i++)
        {
            await Allows(spec.GetJob, "job1");
            if (IsTerminal("job1")) break;
            await Task.Delay(polling.WaitTimeInMs);
        }

        // Liveness check - job should not be stuck in Pending forever
        Assert.IsTrue(IsTerminal("job1"),
            $"Liveness violation: job still Pending after {polling.MaxRetryCount} retries");

        // 3. Get again - spec validates ResultPath is stable
        await Allows(spec.GetJob, "job1");

        // 4. Delete the job
        await Allows(spec.DeleteJob, "job1");
    }

    // ============================================================
    // Test 3: Manual Test with Failures (validates Failed path)
    // ============================================================

    [Test]
    public async Task ManualTest_JobCanFail()
    {
        // Force failures
        JobsController.ProcessingDelayMs = 50;
        JobsController.FailureProbability = 100; // Always fail

        try
        {
            using var factory = new JobQueueServiceFactory();
            var spec = new JobQueueSpec();
            var initialState = new JobQueueState();

            var context = spec.CreateTestingContext();
            context.Register(new JobQueueApiClient(factory.CreateTestClient()));
            var stateProfile = new StateProfile(initialState);

            // Helper: execute and validate through spec
            async Task<TResp> Allows<TReq, TResp>(
                Operation<TReq, TResp, JobQueueState> op, TReq request)
            {
                var response = await op.ExecuteAsync(context, request);
                var (isValid, message, nextProfile) = spec.Allows(op, request, response, stateProfile);
                Assert.IsTrue(isValid, message);
                stateProfile = nextProfile;
                return response;
            }

            // Helper: check if job is terminal.
            // Due to non-determinism, the system can be in multiple possible states,
            // so we check that the job is terminal in ALL of them.
            bool IsTerminal(string jobId) =>
                stateProfile.StatesAndStepFunctions
                    .All(ssf => CreateJobOperation.IsTerminal((JobQueueState)ssf.State, jobId));

            // Create job
            await Allows(spec.CreateJob, "failing-job");

            // Poll until complete
            var polling = spec.CreateJob.Polling;
            for (int i = 0; i < polling.MaxRetryCount; i++)
            {
                await Allows(spec.GetJob, "failing-job");
                if (IsTerminal("failing-job")) break;
                await Task.Delay(polling.WaitTimeInMs);
            }

            // Liveness check
            Assert.IsTrue(IsTerminal("failing-job"),
                "Liveness violation: job still Pending");
        }
        finally
        {
            JobsController.FailureProbability = 0; // Reset
        }
    }
}
