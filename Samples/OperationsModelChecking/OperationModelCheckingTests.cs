namespace OperationsModelChecking;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Operations;
using NUnit.Framework;

[TestFixture]
public class OperationModelCheckingTests
{
    [Test]
    public void ResponseDependentOutcomesBecomeOrdinaryGraphBranches()
    {
        var root = BuildOperationGraph();
        var transitions = root.Edges
            .Select(edge => (OperationModelTransition)edge.Metadata)
            .ToArray();

        Assert.That(root.Edges, Has.Count.EqualTo(2));
        Assert.That(
            root.Edges.Select(edge => ((JobState)edge.Target.State).Status),
            Is.EquivalentTo(new[] { JobStatus.Accepted, JobStatus.Rejected }));
        Assert.That(
            transitions.Select(transition => transition.Response),
            Is.EquivalentTo(new object[]
            {
                SubmitResponse.Accepted,
                SubmitResponse.Rejected
            }));
        Assert.That(
            transitions.Select(transition => transition.OperationName),
            Is.All.EqualTo("submit-job-7"));
    }

    [Test]
    public void OperationGraphsUseStableActionIdentities()
    {
        var first = BuildOperationGraph();
        var second = BuildOperationGraph();

        Assert.That(
            first.GetNodeFingerprint(),
            Is.EqualTo(second.GetNodeFingerprint()));
        Assert.That(
            first.Edges.Select(edge => edge.StepFunction.StepFunctionId),
            Is.EqualTo(second.Edges.Select(edge =>
                edge.StepFunction.StepFunctionId)));
    }

    [Test]
    public void MutableOperationInputCannotChangeCapturedGraphIdentity()
    {
        var operation = new SubmitOperation();
        var input = operation.With(7, "submit-job-7");
        var step = new OperationModelStep(
            input,
            state => ((JobState)state).Status == JobStatus.Pending);

        input.Name = "changed-after-binding";
        var root = OperationModel.Explore(
            new JobState { Status = JobStatus.Pending },
            new[] { step });

        Assert.That(
            step.StepFunctionId,
            Is.EqualTo("operation:submit-job-7"));
        Assert.That(
            root.Edges.Select(edge =>
                ((OperationModelTransition)edge.Metadata).OperationName),
            Is.All.EqualTo("submit-job-7"));
    }

    [Test]
    public void ExistingPropertyEnabledAndFairnessMachineryApplies()
    {
        var root = BuildOperationGraph();
        var safe = Formula.For<JobState>();
        var exact = safe.AllowStutterSensitiveFormulas();
        var resolved = safe.Observe(
            state => state.Status != JobStatus.Pending,
            "Resolved");

        Assert.That(root.Check(safe.Eventually(resolved)).Valid, Is.True);
        Assert.That(
            root.Check(exact.Enabled<OperationModelStep>()).Valid,
            Is.True);
        Assert.That(
            root.Check(
                safe.Eventually(resolved),
                fairness: Fairness.Weak<OperationModelStep>()).Valid,
            Is.True);
        Assert.That(
            root.Edges.All(edge =>
                edge.Target.Check(
                    exact.Not(exact.Enabled<OperationModelStep>())).Valid ==
                true),
            Is.True);
    }

    [Test]
    public void OperationGraphParticipatesInFunctionalRefinement()
    {
        var concrete = BuildOperationGraph();
        var abstraction = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AbstractResolveStep() },
            new AbstractJobState { Status = JobStatus.Pending });

        var result = Refinement
            .Between<JobState, AbstractJobState>(concrete, abstraction)
            .Map(state => new AbstractJobState { Status = state.Status })
            .MapTransition(_ => AbstractResponse.Step<AbstractResolveStep>())
            .CheckTemporal();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    private static StateGraphNode BuildOperationGraph()
    {
        var operation = new SubmitOperation();
        var input = operation.With(7, "submit-job-7");
        return OperationModel.Explore(
            new JobState { Status = JobStatus.Pending },
            new[]
            {
                new OperationModelStep(
                    input,
                    state => ((JobState)state).Status == JobStatus.Pending,
                    repeat: true)
            });
    }

    private enum JobStatus
    {
        Pending,
        Accepted,
        Rejected
    }

    private enum SubmitResponse
    {
        Accepted,
        Rejected
    }

    private sealed class SubmitOperation :
        Operation<int, SubmitResponse, JobState>
    {
        public SubmitOperation() : base("Submit")
        {
        }

        public override ExpectedOutcomes Apply(int request, JobState state)
            => Expect.OneOf(
                Expect.That(
                        response => response == SubmitResponse.Accepted,
                        "accepted")
                    .ThenState(
                        (response, next) => next.Status = JobStatus.Accepted,
                        mock: () => SubmitResponse.Accepted),
                Expect.That(
                        response => response == SubmitResponse.Rejected,
                        "rejected")
                    .ThenState(
                        (response, next) => next.Status = JobStatus.Rejected,
                        mock: () => SubmitResponse.Rejected));
    }

    private sealed class AbstractResolveStep : BaseStepFunction
    {
        public override string StepFunctionId => "resolve";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var current = (AbstractJobState)state;
            if (current.Status != JobStatus.Pending)
            {
                return null;
            }

            return new[]
            {
                Next(JobStatus.Accepted),
                Next(JobStatus.Rejected)
            };
        }

        private static StepResult Next(JobStatus status)
            => new StepResult
            {
                State = new AbstractJobState { Status = status }
            };
    }

    private sealed class JobState : State
    {
        public JobStatus Status { get; set; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new JobState { Status = Status };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Job({Status})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AbstractJobState : State
    {
        public JobStatus Status { get; set; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new AbstractJobState { Status = Status };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"AbstractJob({Status})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }
}
