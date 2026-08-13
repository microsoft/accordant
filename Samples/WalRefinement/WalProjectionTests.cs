namespace WalRefinement;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// What the refinement mapping projects each implementation action onto:
/// which actions the store hides, which ones it records, and what happens
/// when the declaration and the state mapping disagree.
/// </summary>
[TestFixture]
public class WalProjectionTests
{
    [Test]
    public void DurabilityPlumbingAndRecoveryAreHidden()
    {
        var declarations = DeclaredResponses();

        Assert.That(
            declarations
                .Where(entry => entry.Value.All(response => response.HidesConcreteAction))
                .Select(entry => entry.Key)
                .OrderBy(id => id),
            Is.EqualTo(new[]
            {
                "append-redo",
                "install-data-k0",
                "install-data-k1",
                "reconnect-client",
                "recover",
                "restart",
                "truncate-log"
            }),
            "everything the log does for durability is internal to the implementation");

        Assert.That(
            declarations
                .Where(entry => entry.Value.All(response => !response.HidesConcreteAction))
                .Select(entry => entry.Key)
                .OrderBy(id => id),
            Is.EqualTo(new[]
            {
                "abort-txn",
                "ack-abort",
                "ack-commit",
                "flush-commit",
                "submit-v0",
                "submit-v1"
            }));
    }

    [Test]
    public void ACrashIsHiddenExceptWhenItDoomsATransaction()
    {
        var crashes = DeclaredResponses()["crash"];

        Assert.That(
            crashes.Any(response => response.HidesConcreteAction),
            Is.True,
            "a crash with nothing in doubt is invisible to the client");
        Assert.That(
            crashes.Any(response => !response.HidesConcreteAction),
            Is.True,
            "a crash that loses the server's copy of an in-flight write is the abort");
    }

    [Test]
    public void DeclaringEveryCrashHiddenIsAMismatch()
    {
        // Hiding is a checked claim, not a way to suppress a transition.
        var result = WalRefinementCheck
            .Build(transitionMapping: WalRefinementCheck.CrashIsAlwaysHidden)
            .Check();
        var failure = result.Trace[^1];

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(failure.ConcreteStepFunction, Is.TypeOf<CrashStep>());
        Assert.That(failure.DeclaredAbstractResponse.HidesConcreteAction, Is.True);
        Assert.That(
            failure.StateConsistentAbstractTransitions
                .Select(response => response.StepFunction?.StepFunctionId),
            Is.EqualTo(new[] { "spec-abort" }),
            "the mapping already knows which store action the crash performs");
        Assert.That(
            result.GetTraceString(),
            Does.Contain("The declaration hides this concrete transition, but the mapping does not"));
    }

    [Test]
    public void DeclaringTheCommitFlushAnAbortIsAMismatch()
    {
        var result = WalRefinementCheck
            .Build(transitionMapping: WalRefinementCheck.CommitFlushIsAnAbort)
            .Check();
        var failure = result.Trace[^1];

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(failure.ConcreteStepFunction, Is.TypeOf<FlushCommitStep>());
        Assert.That(
            failure.StateConsistentAbstractTransitions
                .Select(response => response.StepFunction?.StepFunctionId),
            Is.EqualTo(new[] { "spec-commit" }));
    }

    [Test]
    public void TheProjectedTraceSeparatesHiddenActionsFromStoreActions()
    {
        var text = WalRefinementCheck
            .BuildDeclared(options: new WalOptions { RecoveryIgnoresCommitRecord = true })
            .Check()
            .GetProjectionString();

        Assert.That(text, Does.Contain("--append-redo--"));
        Assert.That(text, Does.Contain("hidden action (abstract stutter)"));
        Assert.That(text, Does.Contain("abstract step"));
        Assert.That(text, Does.Contain("no abstract projection"));
    }

    [Test]
    public void DeclarationsAreOptionalForAlignmentAndStillWorthMaking()
    {
        // Nothing in this model forces a declaration: no store action is
        // state-neutral and no two store actions perform the same change, so
        // temporal alignment is already deterministic without one. The
        // declarations are here because they turn "these actions are
        // internal" into a checked claim, as DeclaringEveryCrashHiddenIsAMismatch
        // shows.
        var undeclared = WalRefinementCheck
            .Build()
            .CheckTemporal(WalFairness.Implementation, WalFairness.StoreLiveness);
        var declared = WalRefinementCheck
            .BuildDeclared()
            .CheckTemporal(WalFairness.Implementation, WalFairness.StoreLiveness);

        Assert.That(undeclared.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(declared.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    /// <summary>
    /// Applies the declaration to every reachable implementation transition
    /// and groups the answers by step-function id.
    /// </summary>
    private static Dictionary<string, List<AbstractResponse>> DeclaredResponses()
    {
        var responses = new Dictionary<string, List<AbstractResponse>>();
        foreach (var (source, edge) in ModelGraph.Edges(WriteAheadLog.Explore(WalConfig.Default)))
        {
            var id = edge.StepFunction.StepFunctionId;
            if (!responses.TryGetValue(id, out var declared))
            {
                declared = new List<AbstractResponse>();
                responses[id] = declared;
            }

            declared.Add(WalRefinementCheck.StoreAction(
                edge.StepFunction,
                (WalState)source.State));
        }

        return responses;
    }
}
