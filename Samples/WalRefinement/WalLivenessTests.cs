namespace WalRefinement;

using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Temporal refinement of the write-ahead log under explicit fairness.
///
/// <para>Every counterexample here is a lasso whose repeating part is made of
/// crashes, restarts and recovery — actions the abstraction hides. Hiding an
/// action declares that the store stands still; it never removes the concrete
/// behavior, so an infinite crash loop stays a real behavior of the
/// implementation until concrete fairness excludes it.</para>
/// </summary>
[TestFixture]
public class WalLivenessTests
{
    [Test]
    public void AnInfiniteCrashLoopIsARealBehavior()
    {
        // Nothing in the model says the process ever stays up. The store owes
        // the client an outcome, the implementation crashes and restarts
        // forever instead, and none of that is abstract progress.
        var result = Check(Fairness.None);

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(CycleSteps(result), Does.Contain("crash").And.Contains("restart"));
        Assert.That(
            result.Trace
                .Where(item => item.IsInCycle && item.ConcreteStepFunction != null)
                .Skip(1)
                .Select(item => item.ProjectionKind),
            Has.All.EqualTo(AbstractProjectionKind.HiddenAction),
            "the whole repeating part is hidden, so the store stutters forever");
        Assert.That(
            result.GetTraceString(),
            Does.Contain("Every concrete transition in the repeating part is hidden"));
        Assert.That(
            result.GetTraceString(),
            Does.Contain("Hidden actions never discharge an abstract fairness obligation"));
    }

    [Test]
    public void WeakRecoveryFairnessDoesNotSurviveACrashDuringRecovery()
    {
        // Recovery analysis is enabled only while the process is up, so a
        // process that crashes on every attempt never has it continuously
        // enabled. Weak fairness is powerless here.
        var result = Check(WalFairness.WithWeakRecovery);

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(CycleSteps(result), Does.Contain("crash").And.Contains("restart"));
        Assert.That(
            CycleSteps(result),
            Does.Not.Contain("recover"),
            "the process crashes before recovery analysis ever runs");
    }

    [Test]
    public void WeakAcknowledgementFairnessIsNotEnoughEither()
    {
        // Recovery now completes, and the outcome is known after every restart
        // — but a crash disables the acknowledgement again before it is taken.
        var result = Check(WalFairness.WithWeakReports);

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(
            CycleSteps(result),
            Does.Contain("recover").And.Contains("crash"),
            "the outcome is recovered and then lost again to the next crash");
        Assert.That(
            result.Trace.Any(item =>
                item.IsInCycle &&
                ((WalState)item.ConcreteNode.State).Client == ClientPhase.Waiting),
            Is.True,
            "the client waits forever for an outcome the store already decided");
    }

    [Test]
    public void StrongRecoveryAndAcknowledgementFairnessRefine()
    {
        // Strong fairness only asks for an action that is enabled infinitely
        // often to be taken infinitely often, which is exactly what a crash
        // loop guarantees.
        Assert.That(
            Check(WalFairness.Implementation).Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void NoFairnessIsNeededToLeaveTheInDoubtWindow()
    {
        // The in-doubt window contains no cycle: from ServerPhase.Active the
        // model can only commit, abort or crash, and a crash dooms the
        // transaction. An infinite Accordant behavior is an infinite path, so
        // there is no "the server sits there forever" behavior to exclude and
        // the decision steps need no fairness assumption.
        Assert.That(
            Check(WalFairness.Implementation + WalFairness.Decides).Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            "assuming it anyway is sound but adds nothing");

        var inDoubt = ModelGraph
            .Nodes(WriteAheadLog.Explore(WalConfig.Default))
            .Where(node => StoreRefinement.PhaseOf((WalState)node.State) == TxnPhase.Pending)
            .ToArray();
        var staysInDoubt = inDoubt
            .SelectMany(node => node.Edges.Select(edge => (Source: node, Edge: edge)))
            .Where(step => StoreRefinement.PhaseOf((WalState)step.Edge.Target.State) ==
                TxnPhase.Pending)
            .ToArray();

        Assert.That(inDoubt, Is.Not.Empty);
        Assert.That(
            staysInDoubt.Select(step => WalStep.ActionOf(step.Edge.StepFunction)),
            Has.All.EqualTo(WalAction.AppendRedo));
        Assert.That(
            staysInDoubt,
            Has.All.Matches<(StateGraphNode Source, StateGraphEdge Edge)>(step =>
                !((WalState)step.Source.State).LogRedo &&
                ((WalState)step.Edge.Target.State).LogRedo),
            "the in-doubt window is a two-step chain, so it contains no cycle to escape");
    }

    [Test]
    public void NoFairnessIsNeededToRestart()
    {
        // Every down state has exactly one outgoing edge, restart. No infinite
        // path can remain down, so weak restart fairness is semantically
        // redundant rather than the reason restart occurs — which also shows
        // that weak fairness is evaluated over a complete cycle, not over the
        // interval during which an action happens to be enabled.
        var down = ModelGraph
            .Nodes(WriteAheadLog.Explore(WalConfig.Default))
            .Where(node => ((WalState)node.State).Server == ServerPhase.Down)
            .ToArray();

        Assert.That(down, Is.Not.Empty);
        Assert.That(
            down.SelectMany(node => node.Edges).Select(edge => WalStep.ActionOf(edge.StepFunction)),
            Has.All.EqualTo(WalAction.Restart));
        Assert.That(
            Check(Fairness.None + WalFairness.Restarts).FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch),
            "weak restart fairness excludes no crash loop in this graph");
    }

    [Test]
    public void EveryObligationComesFromTheAbstractFairnessAssumption()
    {
        // Without an abstract obligation there is nothing for a crash loop to
        // violate: refinement of a specification that promises no progress is a
        // safety property.
        var result = StoreRefinement.Build().CheckTemporal(Fairness.None, Fairness.None);

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void DeclarationsAreOptionalForAlignmentAndStillWorthMaking()
    {
        // Nothing in this model forces a declaration: no store action is
        // state-neutral and no two store actions perform the same change, so
        // temporal alignment is already deterministic without one. The
        // declarations are here because they turn "these actions are internal"
        // into a checked claim, as DeclaringEveryCrashHiddenIsAMismatch shows.
        var undeclared = StoreRefinement
            .StateOnly()
            .CheckTemporal(WalFairness.Implementation, WalFairness.StoreLiveness);

        Assert.That(undeclared.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            Check(WalFairness.Implementation).Status,
            Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void TheClientAlwaysLearnsTheOutcomeUnderImplementationFairness()
    {
        // The same fairness bundle, checked directly on the implementation as a
        // temporal property rather than through the refinement.
        var wal = Formula.For<WalState>();
        var root = WriteAheadLog.Explore(WalConfig.Default);
        var waiting = wal.Observe(s => s.Client == ClientPhase.Waiting, "Waiting");
        var told = wal.Observe(s => s.Reported != Outcome.None, "Told");
        var eventuallyTold = wal.LeadsTo(waiting, told);

        var fair = root.Check(eventuallyTold, fairness: WalFairness.Implementation);
        var unfair = root.Check(eventuallyTold, fairness: Fairness.None);

        Assert.That(fair.Valid, Is.True, fair.GetTraceString());
        Assert.That(unfair.Valid, Is.False, "crashing forever starves the client");
    }

    private static RefinementCheckingResult Check(Fairness concreteFairness)
        => StoreRefinement
            .Build()
            .CheckTemporal(concreteFairness, WalFairness.StoreLiveness);

    private static string[] CycleSteps(RefinementCheckingResult result)
        => result.Trace
            .Where(item => item.IsInCycle)
            .Select(item => item.ConcreteStepFunction?.StepFunctionId ?? string.Empty)
            .ToArray();
}
