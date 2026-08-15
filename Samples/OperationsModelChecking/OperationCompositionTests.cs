namespace OperationsModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Operations;
using NUnit.Framework;

/// <summary>
/// Composing operation inputs with independently active hand-written step
/// functions in one graph.
///
/// <para>An <see cref="OperationModelStep"/> is an ordinary step function whose
/// outcome is a function of the state it is applied to, so nothing special is
/// needed to interleave it with a background process: the ordinary exploration
/// rules already do. The composing overload of
/// <see cref="OperationModel.Explore{TState}(TState, IEnumerable{OperationModelStep}, IEnumerable{IStepFunction}, int, bool)"/>
/// only extends the adapter's duplicate-identity validation to the whole active
/// set, because two active steps sharing an id would silently change graph node
/// identity.</para>
/// </summary>
[TestFixture]
public class OperationCompositionTests
{
    [Test]
    public void OperationsInterleaveWithIndependentlyActiveSteps()
    {
        var composed = BuildComposedGraph();
        var operationsOnly = OperationModel.Explore(
            new PipelineState(),
            new[] { EnqueueStep() });

        Assert.That(
            Reachable(composed).Count,
            Is.GreaterThan(Reachable(operationsOnly).Count),
            "the background process adds reachable states");
        Assert.That(
            Reachable(composed)
                .SelectMany(node => node.Edges)
                .Select(edge => edge.StepFunction)
                .Any(step => step is DeliverStep),
            Is.True);
        Assert.That(
            Reachable(composed)
                .SelectMany(node => node.Edges)
                .Select(edge => edge.StepFunction)
                .Any(step => step is OperationModelStep),
            Is.True);
    }

    [Test]
    public void TheComposedGraphUsesTheOrdinaryEnabledAndFairnessMachinery()
    {
        var composed = BuildComposedGraph();
        var formula = Formula.For<PipelineState>();
        var exact = formula.AllowStutterSensitiveFormulas();
        var settled = formula.Observe(
            state => state.Delivered || state.Failed,
            "Settled");

        Assert.That(
            composed.Check(exact.Enabled<DeliverStep>()).Valid,
            Is.False,
            "nothing has been requested yet");

        var accepted = composed.Edges.Single(edge =>
            ((PipelineState)edge.Target.State).Requested);
        Assert.That(
            accepted.Target.Check(exact.Enabled<DeliverStep>()).Valid,
            Is.True);

        Assert.That(
            composed.Check(formula.Eventually(settled), fairness: Fairness.None).Status,
            Is.EqualTo(PropertyCheckingStatus.Violated),
            "the caller may retry forever without ever being accepted");
        Assert.That(
            composed.Check(formula.Eventually(settled), fairness: Fairness.WeakAll).Valid,
            Is.True);
    }

    [Test]
    public void ComposingTwoStepsWithTheSameIdentityIsRejected()
    {
        var duplicate = Assert.Throws<ArgumentException>(() => OperationModel.Explore(
            new PipelineState(),
            new[] { EnqueueStep() },
            additionalSteps: new IStepFunction[] { new DeliverStep(), new DeliverStep() }));

        Assert.That(duplicate.Message, Does.Contain("is not unique in the composed model"));
    }

    [Test]
    public void AnOperationCollidingWithAnAdditionalStepIsRejected()
    {
        var collision = Assert.Throws<ArgumentException>(() => OperationModel.Explore(
            new PipelineState(),
            new[] { EnqueueStep() },
            additionalSteps: new IStepFunction[] { new EnqueueImpostorStep() }));

        Assert.That(collision.Message, Does.Contain("is not unique in the composed model"));
    }

    [Test]
    public void ANullAdditionalStepIsRejected()
    {
        var invalid = Assert.Throws<ArgumentException>(() => OperationModel.Explore(
            new PipelineState(),
            new[] { EnqueueStep() },
            additionalSteps: new IStepFunction[] { null }));

        Assert.That(invalid.Message, Does.Contain("cannot contain null"));
    }

    [Test]
    public void OmittingAdditionalStepsMatchesTheOriginalOverload()
    {
        var withoutComposition = OperationModel.Explore(
            new PipelineState(),
            new[] { EnqueueStep() });
        var withEmptyComposition = OperationModel.Explore(
            new PipelineState(),
            new[] { EnqueueStep() },
            additionalSteps: Array.Empty<IStepFunction>());

        Assert.That(
            withEmptyComposition.GetNodeFingerprint(),
            Is.EqualTo(withoutComposition.GetNodeFingerprint()));
        Assert.That(
            Reachable(withEmptyComposition).Count,
            Is.EqualTo(Reachable(withoutComposition).Count));
    }

    private static StateGraphNode BuildComposedGraph()
        => OperationModel.Explore(
            new PipelineState(),
            new[] { EnqueueStep() },
            additionalSteps: new IStepFunction[] { new DeliverStep() });

    private static OperationModelStep EnqueueStep()
        => new OperationModelStep(
            new EnqueueOperation().With(new EnqueueRequest(), "enqueue"),
            state => !((PipelineState)state).Requested);

    private static List<StateGraphNode> Reachable(StateGraphNode root)
    {
        var seen = new Dictionary<string, StateGraphNode>(StringComparer.Ordinal);
        var pending = new Stack<StateGraphNode>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (seen.ContainsKey(node.GetNodeFingerprint()))
            {
                continue;
            }

            seen[node.GetNodeFingerprint()] = node;
            foreach (var edge in node.Edges)
            {
                pending.Push(edge.Target);
            }
        }

        return seen.Values.ToList();
    }

    private enum EnqueueResponse
    {
        Accepted,
        Rejected
    }

    private sealed class EnqueueRequest
    {
        public override string ToString() => "enqueue";
    }

    private sealed class EnqueueOperation :
        Operation<EnqueueRequest, EnqueueResponse, PipelineState>
    {
        public EnqueueOperation() : base("Enqueue")
        {
        }

        public override ExpectedOutcomes Apply(
            EnqueueRequest request,
            PipelineState state)
            => Expect.OneOf(
                Expect.That(
                        response => response == EnqueueResponse.Accepted,
                        "accepted")
                    .ThenState(
                        (_, next) => next.Requested = true,
                        mock: () => EnqueueResponse.Accepted),
                Expect.That(
                        response => response == EnqueueResponse.Rejected,
                        "rejected")
                    .ThenState(
                        (_, _) => { },
                        mock: () => EnqueueResponse.Rejected));
    }

    /// <summary>The independently active background process.</summary>
    private sealed class DeliverStep : BaseStepFunction
    {
        public override string StepFunctionId => "deliver";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var pipeline = (PipelineState)state;
            if (!pipeline.Requested || pipeline.Delivered || pipeline.Failed)
            {
                return null;
            }

            return new[]
            {
                Next(pipeline, next => next.Delivered = true),
                Next(pipeline, next => next.Failed = true)
            };
        }

        private StepResult Next(PipelineState source, Action<PipelineState> change)
        {
            var next = (PipelineState)source.Clone();
            change(next);
            return new StepResult
            {
                State = next,
                StepFunctions = new IStepFunction[] { this }
            };
        }
    }

    /// <summary>A step that claims the bound operation input's identity.</summary>
    private sealed class EnqueueImpostorStep : BaseStepFunction
    {
        public override string StepFunctionId => "operation:enqueue";

        protected override IList<StepResult> ApplyInternal(IState state) => null;
    }

    private sealed class PipelineState : State
    {
        public bool Requested { get; set; }

        public bool Delivered { get; set; }

        public bool Failed { get; set; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new PipelineState
            {
                Requested = Requested,
                Delivered = Delivered,
                Failed = Failed
            };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Pipeline({Requested},{Delivered},{Failed})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }
}
