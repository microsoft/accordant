// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests;

using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;
using ProcessModelChecking;

/// <summary>Backend checks over the focused structured-process sample.</summary>
[TestFixture]
public class StructuredProcessSampleTests
{
    [Test]
    public void TheSampleHasStableExactGraphMeasurements()
    {
        var first = StructuredProcessSample.Explore();
        var second = StructuredProcessSample.Explore();
        var report = ProcessGraphDiagnostics.Describe(first);

        Assert.Multiple(() =>
        {
            Assert.That(first.GetNodeFingerprint(), Is.EqualTo(second.GetNodeFingerprint()));
            Assert.That(report.Complete, Is.True);
            Assert.That(report.ConfigurationCount, Is.EqualTo(6));
            Assert.That(report.TransitionCount, Is.EqualTo(7));
            Assert.That(report.DomainStateCount, Is.EqualTo(6));
        });
    }

    [Test]
    public void DiagnosticsExposeStructuredFramesAndStatelessRoles()
    {
        var report = ProcessGraphDiagnostics.Describe(
            StructuredProcessSample.Explore());

        Assert.That(
            report.ContinuationFormsByRole[StructuredProcessSample.WorkerRole],
            Has.Some.Contains("foreveriteration:attempt-loop"));
        Assert.That(
            report.ContinuationFormsByRole[StructuredProcessSample.WorkerRole],
            Has.Some.Contains("call:execute-attempt"));
        Assert.That(
            report.ContinuationFormsByRole[StructuredProcessSample.SubmitRole],
            Is.EqualTo(new[] { "<stateless recurring action>" }));
        Assert.That(
            report.ContinuationFormsByRole[StructuredProcessSample.AcknowledgeRole],
            Is.EqualTo(new[] { "<stateless recurring action>" }));
    }

    [Test]
    public void ChooseStepBranchesAndMutatesWithoutANeutralChoiceState()
    {
        var root = StructuredProcessSample.Explore();
        var pending = root.Edges.Single().Target;
        var claimed = pending.Edges.Single().Target;
        var outcomes = claimed.Edges.ToArray();

        Assert.That(outcomes, Has.Length.EqualTo(2));
        Assert.That(
            outcomes.Select(edge => (ProcessTransition)edge.Metadata)
                .Select(transition => transition.CheckpointKind),
            Is.All.EqualTo(ModelCheckpointKind.ChooseStep));
        Assert.That(
            outcomes.Select(edge => (ProcessTransition)edge.Metadata)
                .Select(transition => transition.Value),
            Is.EquivalentTo(new object[] { WorkOutcome.Retry, WorkOutcome.Success }));
        Assert.That(
            outcomes.Select(edge => ((WorkState)edge.Target.State).Outcome),
            Is.EquivalentTo(new[] { WorkOutcome.Retry, WorkOutcome.Success }));
        Assert.That(
            outcomes.All(edge =>
                edge.Target.State.StringRepresentation() !=
                claimed.State.StringRepresentation()),
            Is.True);
    }

    [Test]
    public void StrongActionFairnessCanConstrainTheExternalOutcome()
    {
        var root = StructuredProcessSample.Explore();
        var formula = Formula.For<WorkState>();
        var eventuallyCompleted = formula.Eventually(formula.Observe(
            state => state.Phase == WorkPhase.Completed,
            "Completed"));
        var gatewayEventuallySucceeds =
            Fairness.StrongAction<ProcessTransition>(transition =>
                transition.SemanticAction is WorkAction.Execute &&
                transition.Value is WorkOutcome.Success);

        Assert.That(
            root.Check(eventuallyCompleted, fairness: Fairness.None).Status,
            Is.EqualTo(PropertyCheckingStatus.Violated));
        Assert.That(
            root.Check(eventuallyCompleted, fairness: gatewayEventuallySucceeds).Valid,
            Is.True);
    }

    [Test]
    public void EverySemanticEdgeCarriesRoleActionAndSubjectMetadata()
    {
        var transitions = Reachable(StructuredProcessSample.Explore())
            .SelectMany(node => node.Edges)
            .Select(edge => (ProcessTransition)edge.Metadata)
            .ToArray();

        Assert.That(transitions, Is.Not.Empty);
        Assert.That(transitions.Select(item => item.ProcessRole), Has.None.Null);
        Assert.That(transitions.Select(item => item.SemanticAction), Has.None.Null);
        Assert.That(transitions.Select(item => item.Subject), Is.All.EqualTo("job"));
        Assert.That(
            transitions.Select(item => (WorkAction)item.SemanticAction).Distinct(),
            Is.EquivalentTo(new[]
            {
                WorkAction.Submit,
                WorkAction.Claim,
                WorkAction.Execute,
                WorkAction.Settle,
                WorkAction.Acknowledge
            }));
    }

    private static System.Collections.Generic.IEnumerable<StateGraphNode> Reachable(
        StateGraphNode root)
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        var pending = new System.Collections.Generic.Stack<StateGraphNode>();
        pending.Push(root);

        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node.GetNodeFingerprint()))
            {
                continue;
            }

            yield return node;
            foreach (var edge in node.Edges)
            {
                pending.Push(edge.Target);
            }
        }
    }
}
