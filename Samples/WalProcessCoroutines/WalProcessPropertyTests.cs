// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines.Tests;

using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>
/// The property showcase, in three shapes: stutter-safe SafeRegex ordering
/// within an episode, a SafeRegex prefix followed by an LTL/RLTL temporal
/// suffix, and plain direct LTL. Each holding property is paired with a
/// non-vacuity witness or a fairness/negative control so no formula passes for
/// free, and one test proves the safe lowering really erases the graph's
/// state-neutral control transitions.
/// </summary>
[TestFixture]
public class WalProcessPropertyTests
{
    private static readonly WalConfig Config = WalConfig.Default;

    private static StateGraphNode Graph() => WriteAheadLog.Explore(Config);

    // ---------------------------------------------------------------
    // 1–3. SafeRegex ordering within an episode.
    // ---------------------------------------------------------------

    [Test]
    public void SafeRegexEpisodeOrderingHolds()
    {
        var root = Graph();

        Assert.Multiple(() =>
        {
            Holds(root, WalProcessProperties.RedoBeforeCommit());

            foreach (var client in Config.Clients)
            {
                Holds(root, WalProcessProperties.CommitBeforeCommittedReply(client));
                Holds(root, WalProcessProperties.AbortOnlyAfterPrecommitCrash(client));
            }
        });
    }

    [Test]
    public void TheEpisodeOrderingPropertiesAreNonVacuousAndCatchTheReversedClaim()
    {
        var root = Graph();
        var f = WalProcessProperties.F;

        // Non-vacuity: the constrained events genuinely occur on some run, so
        // the "no bad ordering" properties above are not vacuously true. A
        // forbidden pattern that DOES match refutes Whenever(..., False).
        Occurs(root, f.ChangingStep(WalProcessProperties.FlushedCommit), "a commit is flushed");
        Occurs(root, f.ChangingStep(WalProcessProperties.AbortedReply(ClientId.Bob)),
            "Bob is told Aborted on some run");
        Occurs(root, f.ChangingStep(WalProcessProperties.PrecommitCrash(ClientId.Alice)),
            "Alice's transaction is doomed on some run");

        // Negative control: the reversed claim "every AppendRedo is preceded by
        // a FlushCommit" is deliberately wrong — redo precedes flush — so it must
        // be refuted (the checker finds the counterexample).
        var reversed = f.Whenever(
            WalProcessProperties.AnySteps
                .Then(f.ChangingStep(WalProcessProperties.AnySubmit))
                .Then(f.ChangingStep(!WalProcessProperties.FlushedCommit).Star())
                .Then(f.ChangingStep(WalProcessProperties.AppendedRedo)),
            f.False)
            .Named("CommitBeforeRedo(wrong)");
        Refuted(root, reversed);
    }

    // ---------------------------------------------------------------
    // 4. SafeRegex prefix + temporal suffix (needs fairness).
    // ---------------------------------------------------------------

    [Test]
    public void ARegexEpisodePrefixLeadsToTheRightTemporalOutcome()
    {
        var root = Graph();

        // After Submit(Alice) · … · FlushCommit(Alice), Alice is eventually
        // Committed; after Submit(Bob) · … · PrecommitCrash(Bob), Bob is
        // eventually Aborted — under the same strong recover/report fairness the
        // refinement liveness ladder uses.
        Holds(root, WalProcessProperties.CommittedEpisodeReports(ClientId.Alice),
            WalFairness.Implementation);
        Holds(root, WalProcessProperties.AbortedEpisodeReports(ClientId.Bob),
            WalFairness.Implementation);
    }

    [Test]
    public void TheTemporalSuffixesGenuinelyNeedTheImplementationFairness()
    {
        var root = Graph();

        // Without fairness the crash loop starves both promises: the regex
        // prefix still matches, but the ◇ suffix can be violated forever. This
        // is the same non-vacuity the liveness ladder shows, seen through a
        // regex-triggered obligation.
        Refuted(root, WalProcessProperties.CommittedEpisodeReports(ClientId.Alice),
            Fairness.None);
        Refuted(root, WalProcessProperties.AbortedEpisodeReports(ClientId.Bob),
            Fairness.None);
    }

    // ---------------------------------------------------------------
    // Direct LTL.
    // ---------------------------------------------------------------

    [Test]
    public void DirectLtlInvariantsHold()
    {
        var root = Graph();

        Assert.Multiple(() =>
        {
            Holds(root, WalProcessProperties.CommitImpliesRedo());
            Holds(root, WalProcessProperties.RepliesAreFinal());

            foreach (var client in Config.Clients)
            {
                Holds(root, WalProcessProperties.PendingLeadsToReply(client),
                    WalFairness.Implementation);
            }
        });
    }

    [Test]
    public void ThePendingLeadsToReplyNeedsFairness()
    {
        var root = Graph();

        // The leads-to is a liveness property: with no fairness the crash loop
        // is a fair-for-free counterexample.
        Refuted(root, WalProcessProperties.PendingLeadsToReply(ClientId.Alice),
            Fairness.None);
    }

    // ---------------------------------------------------------------
    // The stutter-safe lowering, directly, on this graph.
    // ---------------------------------------------------------------

    [Test]
    public void StateNeutralControlTransitionsAreErasedBySafeLoweringButNotByRawNext()
    {
        var root = Graph();
        var f = WalProcessProperties.F;

        // (a) This graph really is full of state-neutral control transitions:
        // handler launches and process completions leave the domain state
        // unchanged. They are exactly the steps a SafeRegex must not count.
        var controlUnchanged = ModelGraph.Reachable(root)
            .SelectMany(node => node.Edges.Select(edge => (node, edge)))
            .Count(x =>
                x.node.State.StringRepresentation() ==
                    x.edge.Target.State.StringRepresentation() &&
                ModelGraph.Transition(x.edge).IsControl);
        Assert.That(controlUnchanged, Is.GreaterThan(0),
            "the graph must exercise erasure with real state-neutral control edges");

        // (b) The safe ordering property holds even though a handler-launch
        // control transition sits physically between Submit and AppendRedo. The
        // SafeRegex After/Whenever compile through the erasure of unchanged
        // steps, so those control edges never change the verdict.
        Holds(root, WalProcessProperties.RedoBeforeCommit());

        // (c) The insensitivity is the safe wrapper's, not raw temporal logic's.
        // Raw Next counts the physical handler-launch step, so "the step right
        // after Alice's submit already appended the redo" is FALSE — the launch
        // (a state-neutral step) sits in between. We do not claim raw Next is
        // stutter-insensitive; only the SafeRegex lowering is.
        var sensitive = f.AllowStutterSensitiveFormulas();
        TemporalFormula preRedoAlice = f.Observe(
            s => s.Exchange.Pending != null &&
                 s.Exchange.Pending.Client == ClientId.Alice &&
                 !s.Wal.LogRedo &&
                 s.Server.Mode == ServerMode.Running,
            "PreRedo(Alice)");
        TemporalFormula redoDone = f.Observe(s => s.Wal.LogRedo, "RedoDone");

        var rawNextProperty = sensitive.Always(
            sensitive.Implies(preRedoAlice, sensitive.Next(redoDone)))
            .Named("RawNext: redo in the very next physical step");
        Assert.That(root.Check(rawNextProperty).Valid, Is.False,
            "raw Next counts the state-neutral handler-launch transition");
    }

    // ---------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------

    private static void Holds(StateGraphNode root, StutterSafeFormula formula, Fairness fairness = null)
    {
        var result = root.Check(formula, fairness: fairness);
        Assert.That(result.Valid, Is.True, () => $"{formula.Name}: {result.GetTraceString()}");
    }

    private static void Refuted(StateGraphNode root, StutterSafeFormula formula, Fairness fairness = null)
    {
        var result = root.Check(formula, fairness: fairness);
        Assert.That(result.Valid, Is.False, $"{formula.Name} was expected to be refuted");
    }

    private static void Occurs(StateGraphNode root, SafeRegex pattern, string because)
    {
        // Whenever(Σ*·pattern, False) is valid iff the pattern occurs on no run;
        // a witness that it does occur is exactly its refutation.
        var f = WalProcessProperties.F;
        var never = f.Whenever(WalProcessProperties.AnySteps.Then(pattern), f.False);
        Assert.That(root.Check(never).Valid, Is.False, because);
    }
}
