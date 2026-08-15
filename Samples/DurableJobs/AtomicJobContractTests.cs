namespace DurableJobs.Tests;

using DurableJobs;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Operations;
using NUnit.Framework;

[TestFixture]
public class AtomicJobContractTests
{
    [Test]
    public void TheAtomicContractIsCompleteSmallAndFinite()
    {
        var root = AtomicJobModel.Explore();
        var size = ModelGraph.Measure(root);

        Assert.That(ModelGraph.IsComplete(root), Is.True);
        Assert.That(size, Is.EqualTo((5, 25)));
        TestContext.WriteLine($"atomic contract: {size.Nodes} nodes, {size.Edges} edges");
    }

    [Test]
    public void SubmitGetAndCancelHaveThePromisedAtomicBehavior()
    {
        var root = AtomicJobModel.Explore();

        foreach (var (source, edge) in ModelGraph.Edges(root))
        {
            var before = (AtomicJobState)source.State;
            var after = (AtomicJobState)edge.Target.State;
            var transition = (OperationModelTransition)edge.Metadata;

            if (transition.OperationName == AtomicJobModel.GetAction)
            {
                Assert.That(
                    transition.Response,
                    Is.EqualTo(AtomicJobContract.Response(
                        before,
                        DurableJobFixture.JobId)));
                Assert.That(after, Is.EqualTo(before));
            }

            if (transition.OperationName == AtomicJobModel.SubmitAction &&
                before.Status != JobStatus.Missing)
            {
                Assert.That(after, Is.EqualTo(before));
                Assert.That(
                    transition.Response,
                    Is.EqualTo(AtomicJobContract.Response(
                        before,
                        DurableJobFixture.JobId)));
            }

            if (transition.OperationName == AtomicJobModel.CancelAction &&
                before.Status == JobStatus.Pending)
            {
                Assert.That(after.Status, Is.EqualTo(JobStatus.Cancelled));
            }
        }
    }

    [Test]
    public void CompletionCancellationAndTerminalStabilityAreAtomic()
    {
        var root = AtomicJobModel.Explore();
        var terminalEdges = 0;

        foreach (var (source, edge) in ModelGraph.Edges(root))
        {
            var before = (AtomicJobState)source.State;
            var after = (AtomicJobState)edge.Target.State;
            var transition = (OperationModelTransition)edge.Metadata;

            if (before.Status == JobStatus.Pending &&
                transition.OperationName is
                    AtomicJobModel.CancelAction or
                    AtomicJobModel.CompleteSuccessAction or
                    AtomicJobModel.CompleteFailureAction)
            {
                Assert.That(
                    after.Status,
                    Is.AnyOf(
                        JobStatus.Cancelled,
                        JobStatus.Succeeded,
                        JobStatus.Failed));
            }

            if (before.Status is
                JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled)
            {
                terminalEdges++;
                Assert.That(after.Status, Is.EqualTo(before.Status));
                Assert.That(after.Result, Is.EqualTo(before.Result));
                Assert.That(after.Error, Is.EqualTo(before.Error));
            }
        }

        Assert.That(terminalEdges, Is.GreaterThan(0));
    }

    [Test]
    public void OperationsForAnotherJobCannotObserveOrMutateTheStoredJob()
    {
        var contract = new AtomicJobContract();
        var submit = contract.Allows(
            contract.SubmitJob,
            DurableJobFixture.Submit,
            new JobResponse(
                true,
                DurableJobFixture.JobId,
                JobStatus.Pending,
                DurableJobFixture.Payload,
                null,
                null),
            AtomicJobContract.InitialState());

        Assert.That(submit.IsValid, Is.True, submit.Message);

        var state = submit.UpdatedStateProfile;
        foreach (var (operation, request) in new (IOperation Operation, object Request)[]
        {
            (contract.GetJob, DurableJobFixture.OtherJobId),
            (contract.CancelJob, DurableJobFixture.OtherJobId),
            (contract.CompleteSuccess, DurableJobFixture.CompleteOther),
            (contract.CompleteFailure, DurableJobFixture.FailOther)
        })
        {
            var result = contract.Allows(
                operation,
                request,
                JobResponse.Missing(DurableJobFixture.OtherJobId),
                state);

            Assert.That(result.IsValid, Is.True, result.Message);
            var actual = (AtomicJobState)result.UpdatedStateProfile.SingleState();
            var expected = (AtomicJobState)state.SingleState();
            Assert.Multiple(() =>
            {
                Assert.That(actual.JobId, Is.EqualTo(expected.JobId));
                Assert.That(actual.Payload, Is.EqualTo(expected.Payload));
                Assert.That(actual.Status, Is.EqualTo(expected.Status));
                Assert.That(actual.Result, Is.EqualTo(expected.Result));
                Assert.That(actual.Error, Is.EqualTo(expected.Error));
            });
        }
    }

    [Test]
    public void PendingEventuallyBecomesTerminalUnderCompletionFairness()
    {
        var root = AtomicJobModel.Explore();
        var formula = Formula.For<AtomicJobState>();
        var pending = formula.Observe(
            state => state.Status == JobStatus.Pending,
            "Pending");
        var terminal = formula.Observe(
            state => state.Status is
                JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled,
            "Terminal");
        var resolves = formula.LeadsTo(pending, terminal);

        Assert.That(
            root.Check(resolves, fairness: Fairness.None).Status,
            Is.EqualTo(PropertyCheckingStatus.Violated),
            "Get and duplicate Submit can stutter forever without an assumption");
        Assert.That(
            root.Check(resolves, fairness: AtomicJobModel.CompletionFairness).Valid,
            Is.True);
    }
}
