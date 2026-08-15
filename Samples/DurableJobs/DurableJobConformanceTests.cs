namespace DurableJobs.Tests;

using DurableJobs;
using Microsoft.Accordant;
using NUnit.Framework;

[TestFixture]
public class DurableJobConformanceTests
{
    [Test]
    public async Task GeneratedSequentialCasesUseTheContractAsOracle()
    {
        var contract = BoundContract();
        var service = new DurableJobService();
        var initial = AtomicJobContract.InitialState();
        var inputs = ContractInputs(contract);
        var testCases = contract.GenerateTests(
            initial,
            inputs,
            new TestGenerationOptions
            {
                MaxDepth = 4
            });

        var context = contract.CreateTestingContext();
        context.Register(service);

        var results = await contract.RunTests(
            context,
            initial,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = _ =>
                {
                    service.Reset();
                    return Task.CompletedTask;
                }
            });

        Assert.That(testCases, Has.Count.EqualTo(41));
        Assert.That(
            results.All(result => result.Success),
            Is.True,
            results.FirstOrDefault(result => !result.Success)?.LastFailureMessage);
        TestContext.WriteLine($"generated sequential conformance cases: {testCases.Count}");
    }

    [Test]
    public async Task GeneratedConcurrentCasesAreLinearizable()
    {
        var contract = BoundContract();
        var service = new DurableJobService();
        var initial = AtomicJobContract.InitialState();
        var inputs = new InputSet
        {
            contract.SubmitJob.With(DurableJobFixture.Submit, "submit"),
            contract.CancelJob.With(DurableJobFixture.JobId, "cancel"),
            contract.CompleteSuccess.With(DurableJobFixture.Complete, "complete")
        };
        var testCases = contract.GenerateConcurrentTests(
            initial,
            inputs,
            new TestGenerationOptions
            {
                MaxDepth = 3,
                ConcurrentTestCaseAlgorithm =
                    ConcurrentTestCaseAlgorithms
                        .CreateDefaultConcurrentTestCaseGenerator(2)
            });

        var context = contract.CreateTestingContext();
        context.Register(service);

        var results = await contract.RunTests(
            context,
            initial,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = _ =>
                {
                    service.Reset();
                    return Task.CompletedTask;
                }
            });

        Assert.That(testCases, Has.Count.EqualTo(11));
        Assert.That(
            testCases
                .SelectMany(testCase => testCase.Segments)
                .Where(segment => segment.OperationCalls.Count > 1)
                .All(segment => segment.OperationCalls.Count <= 2),
            Is.True,
            "the configured generator must cap concurrent segments at two calls");
        Assert.That(
            results.All(result => result.Success),
            Is.True,
            results.FirstOrDefault(result => !result.Success)?.LastFailureMessage);
        TestContext.WriteLine($"generated concurrent conformance cases: {testCases.Count}");
    }

    [Test]
    public async Task CancellationVersusCompletionHasExactlyOneAtomicWinner()
    {
        var contract = new AtomicJobContract();
        var service = new DurableJobService();
        var submitResponse = service.Submit(DurableJobFixture.Submit);
        var submitted = contract.Allows(
            contract.SubmitJob,
            DurableJobFixture.Submit,
            submitResponse,
            AtomicJobContract.InitialState());

        Assert.That(submitted.IsValid, Is.True, submitted.Message);

        var lease = service.ClaimNext();
        Assert.That(lease, Is.Not.Null);

        using var start = new Barrier(3);
        var cancelTask = Task.Run(() =>
        {
            start.SignalAndWait();
            return service.Cancel(DurableJobFixture.JobId);
        });
        var completeTask = Task.Run(() =>
        {
            start.SignalAndWait();
            return service.CompleteSuccess(
                lease!,
                DurableJobFixture.Result);
        });

        start.SignalAndWait();
        await Task.WhenAll(cancelTask, completeTask);

        var cancelResponse = await cancelTask;
        var completeResponse = await completeTask;
        var final = service.Get(DurableJobFixture.JobId);

        var cancelThenComplete = AllowsCancelThenComplete(
            contract,
            submitted.UpdatedStateProfile,
            cancelResponse,
            completeResponse);
        var completeThenCancel = AllowsCompleteThenCancel(
            contract,
            submitted.UpdatedStateProfile,
            completeResponse,
            cancelResponse);

        Assert.That(cancelThenComplete ^ completeThenCancel, Is.True);
        Assert.That(
            final.Status,
            Is.AnyOf(JobStatus.Cancelled, JobStatus.Succeeded));
        Assert.That(cancelResponse.Status, Is.EqualTo(final.Status));
        Assert.That(completeResponse.Status, Is.EqualTo(final.Status));
    }

    [Test]
    public void TheImplementationExercisesDispatchLeaseCrashRetryAndDuplicates()
    {
        var service = new DurableJobService();

        service.Submit(DurableJobFixture.Submit);
        service.EnqueueDuplicate();
        service.EnqueueDuplicate();
        var first = service.ClaimNext();

        Assert.That(first, Is.Not.Null);
        Assert.That(service.CrashWorker(first!), Is.True);
        Assert.That(
            service.ExpireLease(DurableJobFixture.JobId).Status,
            Is.EqualTo(JobStatus.Pending));
        Assert.That(
            service.Diagnostics(DurableJobFixture.JobId).Worker,
            Is.EqualTo(WorkerLifecycle.Stopped));

        service.RestartWorker();
        var second = service.ClaimNext();
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Attempt, Is.EqualTo(2));

        var failed = service.FailAttempt(second, DurableJobFixture.Error);
        var diagnostics = service.Diagnostics(DurableJobFixture.JobId);

        Assert.That(failed.Status, Is.EqualTo(JobStatus.Failed));
        Assert.That(diagnostics.QueueDepth, Is.Zero);
        Assert.That(diagnostics.LeaseToken, Is.Null);
        Assert.That(diagnostics.Attempts, Is.EqualTo(2));
        Assert.That(diagnostics.Crashes, Is.EqualTo(1));
    }

    [Test]
    public void ADeletedDispatchIsRebuiltFromTheDurablePendingRow()
    {
        var service = new DurableJobService();
        service.Submit(DurableJobFixture.Submit);
        service.LoseDispatch();

        Assert.That(
            service.Diagnostics(DurableJobFixture.JobId).QueueDepth,
            Is.Zero);

        service.RebuildDispatch();
        var lease = service.ClaimNext();
        var completed = service.CompleteSuccess(
            lease!,
            DurableJobFixture.Result);

        Assert.That(completed.Status, Is.EqualTo(JobStatus.Succeeded));
    }

    [Test]
    public void AnotherJobIdCannotObserveOrMutateTheStoredJob()
    {
        var service = new DurableJobService();
        service.Submit(DurableJobFixture.Submit);

        var duplicateOther = service.Submit(
            new SubmitJobRequest(
                DurableJobFixture.OtherJobId,
                DurableJobFixture.Payload));
        var getOther = service.Get(DurableJobFixture.OtherJobId);
        var cancelOther = service.Cancel(DurableJobFixture.OtherJobId);
        var completeOther = service.DriveSuccess(DurableJobFixture.CompleteOther);
        var failOther = service.DriveFailure(DurableJobFixture.FailOther);
        var stored = service.Get(DurableJobFixture.JobId);

        Assert.Multiple(() =>
        {
            Assert.That(duplicateOther, Is.EqualTo(
                JobResponse.Missing(DurableJobFixture.OtherJobId)));
            Assert.That(getOther, Is.EqualTo(
                JobResponse.Missing(DurableJobFixture.OtherJobId)));
            Assert.That(cancelOther, Is.EqualTo(
                JobResponse.Missing(DurableJobFixture.OtherJobId)));
            Assert.That(completeOther, Is.EqualTo(
                JobResponse.Missing(DurableJobFixture.OtherJobId)));
            Assert.That(failOther, Is.EqualTo(
                JobResponse.Missing(DurableJobFixture.OtherJobId)));
            Assert.That(stored.Status, Is.EqualTo(JobStatus.Pending));
            Assert.That(stored.JobId, Is.EqualTo(DurableJobFixture.JobId));
        });
    }

    [Test]
    public void CancellationInvalidatesTheLeaseAndLateWorkerCallsCannotResurrect()
    {
        var service = new DurableJobService();
        service.Submit(DurableJobFixture.Submit);
        service.EnqueueDuplicate();
        var lease = service.ClaimNext();

        Assert.That(lease, Is.Not.Null);

        var cancelled = service.Cancel(DurableJobFixture.JobId);
        var lateSuccess = service.CompleteSuccess(
            lease!,
            DurableJobFixture.Result);
        var lateFailure = service.FailAttempt(
            lease!,
            DurableJobFixture.Error);
        var diagnostics = service.Diagnostics(DurableJobFixture.JobId);

        Assert.Multiple(() =>
        {
            Assert.That(cancelled.Status, Is.EqualTo(JobStatus.Cancelled));
            Assert.That(lateSuccess, Is.EqualTo(cancelled));
            Assert.That(lateFailure, Is.EqualTo(cancelled));
            Assert.That(diagnostics.Job, Is.EqualTo(cancelled));
            Assert.That(diagnostics.QueueDepth, Is.Zero);
            Assert.That(diagnostics.LeaseToken, Is.Null);
            Assert.That(
                diagnostics.Worker,
                Is.EqualTo(WorkerLifecycle.Idle));
        });
    }

    private static AtomicJobContract BoundContract()
    {
        var contract = new AtomicJobContract();
        contract.ExecuteWith<DurableJobService>()
            .Bind(contract.SubmitJob, (service, request) => service.Submit(request))
            .Bind(contract.GetJob, (service, jobId) => service.Get(jobId))
            .Bind(contract.CancelJob, (service, jobId) => service.Cancel(jobId))
            .Bind(
                contract.CompleteSuccess,
                (service, request) => service.DriveSuccess(request))
            .Bind(
                contract.CompleteFailure,
                (service, request) => service.DriveFailure(request));
        return contract;
    }

    private static InputSet ContractInputs(AtomicJobContract contract)
        => new()
        {
            contract.SubmitJob.With(DurableJobFixture.Submit, "submit"),
            contract.GetJob.With(DurableJobFixture.JobId, "get"),
            contract.GetJob.With(DurableJobFixture.OtherJobId, "get-other"),
            contract.CancelJob.With(DurableJobFixture.JobId, "cancel"),
            contract.CancelJob.With(DurableJobFixture.OtherJobId, "cancel-other"),
            contract.CompleteSuccess.With(DurableJobFixture.Complete, "complete"),
            contract.CompleteSuccess.With(
                DurableJobFixture.CompleteOther,
                "complete-other"),
            contract.CompleteFailure.With(DurableJobFixture.Fail, "fail"),
            contract.CompleteFailure.With(
                DurableJobFixture.FailOther,
                "fail-other")
        };

    private static bool AllowsCancelThenComplete(
        AtomicJobContract contract,
        StateProfile start,
        JobResponse cancelResponse,
        JobResponse completeResponse)
    {
        var cancel = contract.Allows(
            contract.CancelJob,
            DurableJobFixture.JobId,
            cancelResponse,
            start);
        if (!cancel.IsValid)
        {
            return false;
        }

        return contract.Allows(
            contract.CompleteSuccess,
            DurableJobFixture.Complete,
            completeResponse,
            cancel.UpdatedStateProfile).IsValid;
    }

    private static bool AllowsCompleteThenCancel(
        AtomicJobContract contract,
        StateProfile start,
        JobResponse completeResponse,
        JobResponse cancelResponse)
    {
        var complete = contract.Allows(
            contract.CompleteSuccess,
            DurableJobFixture.Complete,
            completeResponse,
            start);
        if (!complete.IsValid)
        {
            return false;
        }

        return contract.Allows(
            contract.CancelJob,
            DurableJobFixture.JobId,
            cancelResponse,
            complete.UpdatedStateProfile).IsValid;
    }
}
