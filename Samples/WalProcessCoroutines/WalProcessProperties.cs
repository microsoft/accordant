// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines;

using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;

/// <summary>
/// A small, reader-first showcase of temporal properties for the process WAL,
/// split into three deliberately different shapes so the division is visible:
///
/// <list type="bullet">
///   <item><b>SafeRegex ordering</b> — stutter-safe regular patterns over the
///     <em>changing steps</em> of a run. They read like a protocol trace
///     ("submit, then append the redo, then flush, then acknowledge") and are
///     the natural way to say <em>this happens before that within an
///     episode</em>. A pattern never sees a state-neutral control transition
///     (a process launch, a completion, a no-op): it is compiled through the
///     erasure of unchanged steps, so inserting or removing them never changes
///     a verdict.</item>
///   <item><b>Regex prefix + temporal suffix</b> — a SafeRegex trigger followed
///     by an ordinary LTL/RLTL obligation: <em>after this finite trace prefix,
///     eventually that</em>. This is the shape SafeRegex makes clearest —
///     correlating a concrete, finite, per-client episode with a liveness
///     promise — and it needs the same fairness the refinement liveness ladder
///     uses.</item>
///   <item><b>Direct LTL</b> — plain state/transition invariants and a
///     leads-to. These are exactly what LTL expresses cleanly; no regular
///     pattern is needed or clearer.</item>
/// </list>
///
/// <para>The two views agree on stutter-invariant facts. Where they differ is
/// deliberate: raw <c>Next</c> counts every physical transition (see the stutter
/// test), whereas SafeRegex and the stutter-safe temporal operators count only
/// changing steps. We do not claim raw regex or raw <c>Next</c> are
/// insensitive — only the SafeRegex wrapper and its erasure lowering are.</para>
///
/// <para>Every "action" of the implementation is observed here <em>as the state
/// change it makes</em>. The SafeRegex surface deliberately accepts state and
/// source/target observations rather than edge metadata; moreover, this model's
/// public step identity is the composite scheduler while its semantic action is
/// metadata. So "AppendRedo" is "the redo bit goes false → true",
/// "FlushCommit" is "the commit bit goes false → true", and so on. The safe
/// lowering then ignores every unchanged transition.</para>
/// </summary>
public static class WalProcessProperties
{
    /// <summary>The stutter-invariant formula builder for the process state.</summary>
    public static FormulaBuilder<WalProcessState> F { get; } =
        Formula.For<WalProcessState>();

    // -----------------------------------------------------------------
    // The implementation "actions", each observed as its state change.
    // -----------------------------------------------------------------

    /// <summary>Some client claims the free slot: <c>Pending: null → set</c>.</summary>
    public static TransitionObservation AnySubmit { get; } = F.ObserveTransition(
        (a, b) => a.Exchange.Pending == null && b.Exchange.Pending != null,
        "Submit");

    /// <summary><paramref name="client"/> claims the slot.</summary>
    public static TransitionObservation Submitted(ClientId client) => F.ObserveTransition(
        (a, b) => a.Exchange.Pending == null &&
                  b.Exchange.Pending != null && b.Exchange.Pending.Client == client,
        $"Submit({client})");

    /// <summary>The redo record becomes durable: <c>LogRedo: false → true</c>.</summary>
    public static TransitionObservation AppendedRedo { get; } = F.ObserveTransition(
        (a, b) => !a.Wal.LogRedo && b.Wal.LogRedo,
        "AppendRedo");

    /// <summary>The commit record becomes durable — the linearization point.</summary>
    public static TransitionObservation FlushedCommit { get; } = F.ObserveTransition(
        (a, b) => !a.Wal.LogCommit && b.Wal.LogCommit,
        "FlushCommit");

    /// <summary><paramref name="client"/> is told <c>Committed</c> (handler or recovery).</summary>
    public static TransitionObservation CommittedReply(ClientId client) => F.ObserveTransition(
        (a, b) => a.Exchange.ReplyOf(client) == Outcome.None &&
                  b.Exchange.ReplyOf(client) == Outcome.Committed,
        $"AckCommit({client})");

    /// <summary><paramref name="client"/> is told <c>Aborted</c> (recovery only).</summary>
    public static TransitionObservation AbortedReply(ClientId client) => F.ObserveTransition(
        (a, b) => a.Exchange.ReplyOf(client) == Outcome.None &&
                  b.Exchange.ReplyOf(client) == Outcome.Aborted,
        $"AckAbort({client})");

    /// <summary>
    /// A crash that dooms <paramref name="client"/>'s in-flight transaction: the
    /// server goes <c>Running → Down</c> while <paramref name="client"/>'s
    /// request is admitted but its commit is not yet flushed.
    /// </summary>
    public static TransitionObservation PrecommitCrash(ClientId client) => F.ObserveTransition(
        (a, b) => a.Server.Mode == ServerMode.Running && b.Server.Mode == ServerMode.Down &&
                  a.Exchange.Pending != null && a.Exchange.Pending.Client == client &&
                  !a.Wal.LogCommit,
        $"PrecommitCrash({client})");

    // -----------------------------------------------------------------
    // State observations used by the temporal suffixes and the direct LTL.
    // -----------------------------------------------------------------

    /// <summary><paramref name="client"/> holds a persistent <c>Committed</c> reply.</summary>
    public static Observation Committed(ClientId client) => F.Observe(
        s => s.Exchange.ReplyOf(client) == Outcome.Committed,
        $"Committed({client})");

    /// <summary><paramref name="client"/> holds a persistent <c>Aborted</c> reply.</summary>
    public static Observation Aborted(ClientId client) => F.Observe(
        s => s.Exchange.ReplyOf(client) == Outcome.Aborted,
        $"Aborted({client})");

    /// <summary><paramref name="client"/> holds any persistent reply.</summary>
    public static Observation HasReply(ClientId client) => F.Observe(
        s => s.Exchange.HasReply(client),
        $"HasReply({client})");

    /// <summary><paramref name="client"/>'s request occupies the slot.</summary>
    public static Observation PendingFor(ClientId client) => F.Observe(
        s => s.Exchange.Pending != null && s.Exchange.Pending.Client == client,
        $"Pending({client})");

    // -----------------------------------------------------------------
    // SafeRegex letters (single changing steps) and Σ*.
    // -----------------------------------------------------------------

    /// <summary>Zero or more arbitrary changing steps — the floating anchor.</summary>
    public static SafeRegex AnySteps => F.AnyChangingStep.Star();

    // -----------------------------------------------------------------
    // 1–3: SafeRegex ordering within a per-client episode.
    // -----------------------------------------------------------------

    /// <summary>
    /// <b>Redo before commit.</b> Within any admitted episode, the commit flush
    /// never happens before the redo record is appended: there is no run in
    /// which, after a <c>Submit</c>, a <c>FlushCommit</c> is reached with no
    /// intervening <c>AppendRedo</c>. Phrased as a forbidden trace prefix.
    /// </summary>
    public static StutterSafeFormula RedoBeforeCommit()
        => F.Whenever(
            AnySteps
                .Then(F.ChangingStep(AnySubmit))
                .Then(F.ChangingStep(!AppendedRedo).Star())
                .Then(F.ChangingStep(FlushedCommit)),
            F.False)
            .Named("RedoBeforeCommit");

    /// <summary>
    /// <b>Commit before the committed reply.</b> A client is never told
    /// <c>Committed</c> before its commit record was flushed: after
    /// <c>Submit(client)</c>, no <c>AckCommit(client)</c> is reached with no
    /// intervening <c>FlushCommit</c>.
    /// </summary>
    public static StutterSafeFormula CommitBeforeCommittedReply(ClientId client)
        => F.Whenever(
            AnySteps
                .Then(F.ChangingStep(Submitted(client)))
                .Then(F.ChangingStep(!FlushedCommit).Star())
                .Then(F.ChangingStep(CommittedReply(client))),
            F.False)
            .Named($"CommitBeforeCommittedReply({client})");

    /// <summary>
    /// <b>Abort only on the crash path.</b> A client is told <c>Aborted</c> only
    /// after a crash doomed its in-flight transaction: after
    /// <c>Submit(client)</c>, no <c>AckAbort(client)</c> is reached with no
    /// intervening <c>PrecommitCrash(client)</c>. Together with
    /// <see cref="CommitBeforeCommittedReply"/> this pins the episode to one of
    /// two shapes — the committed path or the crash/recovery abort path.
    /// </summary>
    public static StutterSafeFormula AbortOnlyAfterPrecommitCrash(ClientId client)
        => F.Whenever(
            AnySteps
                .Then(F.ChangingStep(Submitted(client)))
                .Then(F.ChangingStep(!PrecommitCrash(client)).Star())
                .Then(F.ChangingStep(AbortedReply(client))),
            F.False)
            .Named($"AbortOnlyAfterPrecommitCrash({client})");

    // -----------------------------------------------------------------
    // 4: regex prefix + temporal suffix (needs fairness).
    // -----------------------------------------------------------------

    /// <summary>
    /// <b>Committed episode.</b> After the finite trace
    /// <c>… · Submit(client) · … · FlushCommit</c> (the flush belonging to
    /// <paramref name="client"/>'s admitted request), <paramref name="client"/>
    /// eventually holds a <c>Committed</c> reply. The correlation is finite and
    /// explicit; the suffix is an ordinary <c>◇</c> obligation that needs the
    /// implementation fairness.
    /// </summary>
    public static StutterSafeFormula CommittedEpisodeReports(ClientId client)
        => F.Whenever(
            AnySteps
                .Then(F.ChangingStep(Submitted(client)))
                .Then(AnySteps)
                .Then(F.ChangingStep(
                    FlushedCommit &
                    F.ObserveTransition(
                        (a, b) => b.Exchange.Pending != null &&
                                  b.Exchange.Pending.Client == client,
                        $"→Pending({client})"))),
            F.Eventually(Committed(client)))
            .Named($"CommittedEpisodeReports({client})");

    /// <summary>
    /// <b>Aborted episode.</b> After the finite trace
    /// <c>… · Submit(client) · … · PrecommitCrash(client)</c>,
    /// <paramref name="client"/> eventually holds an <c>Aborted</c> reply —
    /// recovery replaces the killed handler and reports the abort. Needs the
    /// implementation fairness.
    /// </summary>
    public static StutterSafeFormula AbortedEpisodeReports(ClientId client)
        => F.Whenever(
            AnySteps
                .Then(F.ChangingStep(Submitted(client)))
                .Then(AnySteps)
                .Then(F.ChangingStep(PrecommitCrash(client))),
            F.Eventually(Aborted(client)))
            .Named($"AbortedEpisodeReports({client})");

    // -----------------------------------------------------------------
    // Direct LTL: plain invariants and a leads-to.
    // -----------------------------------------------------------------

    /// <summary><c>□ (LogCommit ⇒ LogRedo)</c> — a durable commit implies a durable redo.</summary>
    public static StutterSafeFormula CommitImpliesRedo()
        => F.Always(F.Observe(
            s => !s.Wal.LogCommit || s.Wal.LogRedo,
            "LogCommit⇒LogRedo"))
            .Named("CommitImpliesRedo");

    /// <summary>
    /// <c>□ (a persistent reply never changes)</c> — once a client has been told
    /// an outcome it stays that outcome forever (clients are one-shot).
    /// </summary>
    public static StutterSafeFormula RepliesAreFinal()
        => F.Always(F.ObserveTransition(
            (a, b) =>
            {
                foreach (var client in WalConfig.Default.Clients)
                {
                    var before = a.Exchange.ReplyOf(client);
                    if (before != Outcome.None && b.Exchange.ReplyOf(client) != before)
                    {
                        return false;
                    }
                }

                return true;
            },
            "RepliesAreFinal"))
            .Named("RepliesAreFinal");

    /// <summary>
    /// <c>□ (Pending(client) ⇒ ◇ HasReply(client))</c> — an admitted request is
    /// eventually answered. Needs the implementation fairness.
    /// </summary>
    public static StutterSafeFormula PendingLeadsToReply(ClientId client)
        => F.LeadsTo(PendingFor(client), HasReply(client))
            .Named($"PendingLeadsToReply({client})");
}
