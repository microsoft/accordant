namespace WorkQueueRefinement;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Safety refinement of the leased work queue against the client ledger, and
/// the diagnostics produced when one of the three mechanisms is missing or
/// wrong.
/// </summary>
[TestFixture]
public class WorkQueueRefinementTests
{
    [Test]
    public void ImplementationRefinesTheLedger()
    {
        var result = WorkQueueRefinementCheck.Build().Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void ConcreteGraphPreservesQueueInvariants()
    {
        var config = WorkQueueConfig.Default;

        foreach (var node in ExploreNodes(WorkQueue.Explore(config)))
        {
            var state = (QueueState)node.State;
            for (var task = 0; task < config.Tasks; task++)
            {
                var holders = state.WorkerTask.Count(value => value == task);
                Assert.That(holders, Is.LessThanOrEqualTo(1));
                Assert.That(
                    state.Phases[task] == TaskPhase.Leased,
                    Is.EqualTo(holders == 1));
                Assert.That(
                    state.Attempts[task],
                    Is.InRange(0, config.MaxAttempts));
                if (state.Phases[task] == TaskPhase.Ready)
                {
                    Assert.That(
                        state.Attempts[task],
                        Is.LessThan(config.MaxAttempts));
                }
            }

            Assert.That(
                state.WorkerTask,
                Has.All.Matches<int>(task =>
                    task == WorkQueue.Idle ||
                    task >= 0 && task < config.Tasks));
        }
    }

    [Test]
    public void WithoutAugmentationTheCommittedOwnerIsLost()
    {
        // The ledger committed to the first claimant. The concrete state only
        // knows the current lease holder, and an expiry erases even that.
        var result = WorkQueueRefinementCheck.BuildWithoutAugmentation().Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            result.Trace[^1].ConcreteStepFunction,
            Is.TypeOf<ExpireLeaseStep>());
        Assert.That(result.Trace[^1].AbstractCandidates, Is.Empty);
    }

    [Test]
    public void RememberingTheLatestClaimantRewritesACommittedOwner()
    {
        // A retry taken by the other worker must not change the audit field
        // the ledger already committed to.
        var result = WorkQueueRefinementCheck
            .Build(augment: WorkQueueRefinementCheck.RememberLatestClaimant)
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(result.Trace[^1].ConcreteStepFunction, Is.TypeOf<LeaseStep>());
        Assert.That(
            ((ClaimHistory)result.Trace[^1].AuxiliaryState).FirstOwner,
            Does.Contain(1),
            "the second worker overwrote the first claimant");
    }

    [Test]
    public void WithoutPredictionsTheCommittedResultMustBeGuessed()
    {
        // Augmentation cannot help: the committed result is not in the past.
        var result = WorkQueueRefinementCheck.BuildWithoutWitnesses().Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));

        var mapped = (LedgerState)result.Trace[^1].MappedAbstractState;
        Assert.That(
            mapped.Results,
            Does.Contain(LedgerResult.Cancelled),
            "the concrete run settled an entry the guess had committed to Completed");
    }

    [Test]
    public void ALedgerMissingTheDroppedResultIsRefuted()
    {
        var result = WorkQueueRefinementCheck
            .Build(ledgerOptions: new LedgerOptions
            {
                Results = new[] { LedgerResult.Completed, LedgerResult.Cancelled }
            })
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));

        var failure = result.Trace[^1];
        Assert.That(failure.ConcreteStepFunction, Is.TypeOf<LeaseStep>());
        Assert.That(
            failure.Witnesses
                .Get<OutcomeWitness>(WorkQueueRefinementCheck.OperationId(0))
                .Result,
            Is.EqualTo(LedgerResult.Dropped),
            "the copy predicting a drop has no ledger commitment to map onto");
    }

    [Test]
    public void PredictionsForBothTasksFormTheCrossProduct()
    {
        var pairs = new HashSet<(LedgerResult, LedgerResult)>();
        var maxPending = 0;

        var result = WorkQueueRefinementCheck
            .Build(mapping: (queue, history, witnesses) =>
            {
                maxPending = System.Math.Max(maxPending, witnesses.Count);
                var ledger = WorkQueueRefinementCheck
                    .MapToLedger(queue, history, witnesses);
                if (ledger.Phases[0] == LedgerPhase.Assigned &&
                    ledger.Phases[1] == LedgerPhase.Assigned)
                {
                    pairs.Add((ledger.Results[0], ledger.Results[1]));
                }
                return ledger;
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(maxPending, Is.EqualTo(2), "one prediction per in-flight task");
        Assert.That(
            pairs.Count,
            Is.EqualTo(9),
            "two pending operations with three possible results each");
    }

    [Test]
    public void PurgeCancelsThePredictionAndMergesTheCopies()
    {
        var pendingAfterPurge = false;
        var merged = 0;

        var result = WorkQueueRefinementCheck
            .Build(mapping: (queue, history, witnesses) =>
            {
                merged++;
                var ledger = WorkQueueRefinementCheck
                    .MapToLedger(queue, history, witnesses);
                if (queue.Phases[0] == TaskPhase.Purged)
                {
                    pendingAfterPurge |= witnesses.IsPending(
                        WorkQueueRefinementCheck.OperationId(0));
                }
                return ledger;
            })
            .Check();

        var retained = 0;
        var withoutCancellation = WorkQueueRefinementCheck
            .Build(
                lifecycle: WorkQueueRefinementCheck
                    .TrackOutcomesKeepingPurgedPredictions,
                mapping: (queue, history, witnesses) =>
                {
                    retained++;
                    return WorkQueueRefinementCheck
                        .MapToLedger(queue, history, witnesses);
                })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(pendingAfterPurge, Is.False);
        Assert.That(
            withoutCancellation.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            "keeping a stale prediction is a cost, not a refinement failure");
        Assert.That(
            retained,
            Is.GreaterThan(merged),
            "unresolved predictions that are never cancelled cannot merge");
        Assert.That(
            retained,
            Is.EqualTo(7129),
            "the documented stale-witness proof-space cost must stay pinned");
    }

    [Test]
    public void ReintroducingAPendingPredictionIsADefinitionError()
    {
        // The lease that looks like the first one is also the retry lease
        // reached after an expiry, where the prediction is still pending.
        Assert.That(
            () => WorkQueueRefinementCheck
                .Build(lifecycle: WorkQueueRefinementCheck
                    .TrackOutcomesReintroducingOnEveryLease)
                .Check(),
            Throws.TypeOf<WitnessDefinitionException>()
                .With.Message.Contains("already pending"));
    }

    [Test]
    public void ResolvingOutsideTheDeclaredDomainIsRejected()
    {
        Assert.That(
            () => WorkQueueRefinementCheck
                .Build(lifecycle: WorkQueueRefinementCheck
                    .TrackOutcomesResolvingOutsideTheDomain)
                .Check(),
            Throws.TypeOf<WitnessDefinitionException>()
                .With.Message.Contains("not in the introduced domain"));
    }

    [Test]
    public void TheTraceSeparatesRecoveredHistoryFromPredictedFutures()
    {
        var result = WorkQueueRefinementCheck
            .Build(augment: WorkQueueRefinementCheck.RememberLatestClaimant)
            .Check();

        var text = result.GetTraceString();

        Assert.That(text, Does.Contain("auxiliary"));
        Assert.That(text, Does.Contain("witnesses"));
        Assert.That(
            result.Trace.Any(item =>
                item.AuxiliaryState != null && item.Witnesses.Count > 0),
            Is.True,
            "history and predictions are reported side by side, never merged");
    }

    [Test]
    public void MeasuredModelAndProofSizes()
    {
        var config = WorkQueueConfig.Default;

        Assert.That(Measure(WorkQueue.Explore(config)), Is.EqualTo((292, 964)));
        Assert.That(Measure(Ledger.Explore(config)), Is.EqualTo((256, 608)));

        var configurations = 0;
        var result = WorkQueueRefinementCheck
            .Build(mapping: (queue, history, witnesses) =>
            {
                // The mapping is memoized per (concrete node, proof identity),
                // so this counts distinct refinement proof configurations.
                configurations++;
                return WorkQueueRefinementCheck
                    .MapToLedger(queue, history, witnesses);
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            configurations,
            Is.EqualTo(6417),
            "292 concrete states carry up to 3^2 predictions and the claim history");
    }

    [Test]
    [Explicit("Runs the three-task model: about 80 seconds and 4.1e5 proof configurations.")]
    public void AThirdTaskMultipliesTheProofSpace()
    {
        var config = new WorkQueueConfig(2, 3, 2);

        Assert.That(Measure(WorkQueue.Explore(config)), Is.EqualTo((4360, 19500)));
        Assert.That(Measure(Ledger.Explore(config)), Is.EqualTo((4096, 14592)));

        var configurations = 0;
        var result = WorkQueueRefinementCheck
            .Build(config, mapping: (queue, history, witnesses) =>
            {
                configurations++;
                return WorkQueueRefinementCheck
                    .MapToLedger(queue, history, witnesses);
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(configurations, Is.EqualTo(413127));
    }

    private static (int Nodes, int Edges) Measure(StateGraphNode root)
    {
        var edges = 0;
        var nodes = ExploreNodes(root).ToArray();
        foreach (var node in nodes)
        {
            edges += node.Edges.Count;
        }
        return (nodes.Length, edges);
    }

    private static IEnumerable<StateGraphNode> ExploreNodes(StateGraphNode root)
    {
        var seen = new HashSet<string> { root.GetNodeFingerprint() };
        var queue = new Queue<StateGraphNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;
            foreach (var edge in node.Edges)
            {
                if (seen.Add(edge.Target.GetNodeFingerprint()))
                {
                    queue.Enqueue(edge.Target);
                }
            }
        }
    }
}
