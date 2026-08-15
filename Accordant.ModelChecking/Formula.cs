namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Accordant.ModelChecking.Symbolic;

/// <summary>
/// Entry point for building typed temporal formulas.
/// </summary>
public static class Formula
{
    /// <summary>
    /// Create a formula builder whose operations guarantee stutter-invariant
    /// formulas.
    /// </summary>
    public static FormulaBuilder<TState> For<TState>() where TState : State
        => new FormulaBuilder<TState>();
}

/// <summary>
/// Typed builder for the stutter-invariant temporal formula subset.
/// </summary>
public class FormulaBuilder<TState> where TState : State
{
    private readonly Rltl<IStatePredicate> unchanged = StutterAlphabet.Unchanged;
    private readonly Rltl<IStatePredicate> changed = StutterAlphabet.Changing;

    #region Observation factory

    /// <summary>
    /// Define an atomic observation (proposition) over the model state.
    /// The returned <see cref="Observation"/> can be used in both temporal
    /// formulas and regex patterns.
    /// </summary>
    /// <param name="predicate">State predicate — evaluated against concrete states
    /// during model checking.</param>
    /// <param name="name">Optional display name for diagnostics and counterexample traces.</param>
    /// <param name="expression">Compiler-supplied predicate expression used when
    /// <paramref name="name"/> is omitted.</param>
    public Observation Observe(
        Func<TState, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        var diagnosticName = ResolveObservationName(name, expression);
        var prop = new StateProp(diagnosticName, state => predicate((TState)state));
        return new Observation(new StatePredAtom(prop));
    }

    /// <summary>
    /// Define an atomic observation over a source and target state.
    /// Stutter-safe temporal operators ignore unchanged transitions when
    /// interpreting this observation.
    /// </summary>
    public TransitionObservation ObserveTransition(
        Func<TState, TState, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        var diagnosticName = ResolveObservationName(name, expression);
        var prop = StateProp.OverTransition(
            diagnosticName,
            context => predicate((TState)context.From, (TState)context.To));
        return new TransitionObservation(new StatePredAtom(prop));
    }

    #endregion

    #region Constants

    /// <summary>True constant — satisfied by every infinite word.</summary>
    public StutterSafeFormula True => new StutterSafeFormula(Rltl<IStatePredicate>.True());

    /// <summary>False constant — satisfied by no infinite word.</summary>
    public StutterSafeFormula False => new StutterSafeFormula(Rltl<IStatePredicate>.False());

    #endregion

    #region Boolean operators

    /// <summary>Negation <c>¬φ</c>.</summary>
    public StutterSafeFormula Not(StutterSafeFormula inner)
        => new StutterSafeFormula(RltlAlgebra.Default.Not(inner.Core));

    /// <summary>Conjunction <c>φ ∧ ψ</c>.</summary>
    public StutterSafeFormula And(StutterSafeFormula left, StutterSafeFormula right)
        => new StutterSafeFormula(RltlAlgebra.Default.And(left.Core, right.Core));

    /// <summary>Conjunction of multiple formulas.</summary>
    public StutterSafeFormula And(params StutterSafeFormula[] formulas)
        => formulas.Aggregate(True, And);

    /// <summary>Disjunction <c>φ ∨ ψ</c>.</summary>
    public StutterSafeFormula Or(StutterSafeFormula left, StutterSafeFormula right)
        => new StutterSafeFormula(RltlAlgebra.Default.Or(left.Core, right.Core));

    /// <summary>Disjunction of multiple formulas.</summary>
    public StutterSafeFormula Or(params StutterSafeFormula[] formulas)
        => formulas.Aggregate(False, Or);

    /// <summary>Implication <c>φ → ψ</c>.</summary>
    public StutterSafeFormula Implies(
        StutterSafeFormula antecedent,
        StutterSafeFormula consequent)
        => new StutterSafeFormula(
            RltlAlgebra.Default.Implies(antecedent.Core, consequent.Core));

    #endregion

    #region Temporal operators (LTL)

    /// <summary>Until: <c>φ U ψ</c> — φ holds until ψ holds (ψ eventually holds).</summary>
    public StutterSafeFormula Until(StutterSafeFormula hold, StutterSafeFormula goal)
        => new StutterSafeFormula(Rltl<IStatePredicate>.Until(hold.Core, goal.Core));

    public StutterSafeFormula Until(
        StutterSafeFormula hold,
        TransitionObservation goal)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Until(hold.Core, Occurs(goal)));

    public StutterSafeFormula Until(
        TransitionObservation hold,
        StutterSafeFormula goal)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Until(Allowed(hold), goal.Core));

    public StutterSafeFormula Until(
        TransitionObservation hold,
        TransitionObservation goal)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Until(Allowed(hold), Occurs(goal)));

    /// <summary>Release: <c>φ R ψ</c> — dual of Until.</summary>
    public StutterSafeFormula Release(StutterSafeFormula release, StutterSafeFormula hold)
        => new StutterSafeFormula(Rltl<IStatePredicate>.Release(release.Core, hold.Core));

    public StutterSafeFormula Release(
        StutterSafeFormula release,
        TransitionObservation hold)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Release(release.Core, Allowed(hold)));

    public StutterSafeFormula Release(
        TransitionObservation release,
        StutterSafeFormula hold)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Release(Occurs(release), hold.Core));

    public StutterSafeFormula Release(
        TransitionObservation release,
        TransitionObservation hold)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Release(Occurs(release), Allowed(hold)));

    /// <summary>Eventually: <c>◇φ</c> — φ holds at some future state.</summary>
    public StutterSafeFormula Eventually(StutterSafeFormula inner)
        => new StutterSafeFormula(Rltl<IStatePredicate>.Eventually(inner.Core));

    public StutterSafeFormula Eventually(TransitionObservation inner)
        => new StutterSafeFormula(Rltl<IStatePredicate>.Eventually(Occurs(inner)));

    /// <summary>Always: <c>□φ</c> — φ holds at every future state.</summary>
    public StutterSafeFormula Always(StutterSafeFormula inner)
        => new StutterSafeFormula(Rltl<IStatePredicate>.Globally(inner.Core));

    public StutterSafeFormula Always(TransitionObservation inner)
        => new StutterSafeFormula(Rltl<IStatePredicate>.Globally(Allowed(inner)));

    /// <summary>Infinitely often: <c>□◇φ</c> — φ holds infinitely often.</summary>
    public StutterSafeFormula InfinitelyOften(StutterSafeFormula inner)
        => Always(Eventually(inner));

    public StutterSafeFormula InfinitelyOften(TransitionObservation inner)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Globally(
                Rltl<IStatePredicate>.Eventually(Occurs(inner))));

    /// <summary>Stabilizes: <c>◇□φ</c> — φ eventually holds forever.</summary>
    public StutterSafeFormula Stabilizes(StutterSafeFormula inner)
        => Eventually(Always(inner));

    public StutterSafeFormula Stabilizes(TransitionObservation inner)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Eventually(
                Rltl<IStatePredicate>.Globally(Allowed(inner))));

    /// <summary>Leads-to: <c>φ ~> ψ</c> = <c>□(φ → ◇ψ)</c> — whenever φ holds,
    /// ψ eventually follows.</summary>
    public StutterSafeFormula LeadsTo(
        StutterSafeFormula trigger,
        StutterSafeFormula response)
        => Always(Implies(trigger, Eventually(response)));

    public StutterSafeFormula LeadsTo(
        StutterSafeFormula trigger,
        TransitionObservation response)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Globally(
                RltlAlgebra.Default.Implies(
                    trigger.Core,
                    Rltl<IStatePredicate>.Eventually(Occurs(response)))));

    public StutterSafeFormula LeadsTo(
        TransitionObservation trigger,
        StutterSafeFormula response)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Globally(
                RltlAlgebra.Default.Implies(
                    Occurs(trigger),
                    Rltl<IStatePredicate>.Eventually(response.Core))));

    public StutterSafeFormula LeadsTo(
        TransitionObservation trigger,
        TransitionObservation response)
        => new StutterSafeFormula(
            Rltl<IStatePredicate>.Globally(
                RltlAlgebra.Default.Implies(
                    Occurs(trigger),
                    Rltl<IStatePredicate>.Eventually(Occurs(response)))));

    #endregion

    #region Stutter-safe regular patterns (SafeRegex)

    /// <summary>
    /// One state-changing step whose <em>source</em> state satisfies
    /// <paramref name="observation"/>.
    ///
    /// <para>Steps that leave the state semantically unchanged are invisible to
    /// the resulting pattern: they neither match nor break it. See
    /// <see cref="SafeRegex"/> for the erasure lifting.</para>
    /// </summary>
    public SafeRegex ChangingStep(Observation observation)
    {
        if (observation == null) throw new ArgumentNullException(nameof(observation));
        return SafeRegex.Step(
            observation.PredicateCore,
            $"⟨{observation}⟩");
    }

    /// <summary>
    /// One state-changing step <c>s → s'</c> satisfying
    /// <paramref name="observation"/>.
    ///
    /// <para>Because the step is required to be changing, an observation that
    /// only holds of unchanged steps yields a pattern that never matches.</para>
    /// </summary>
    public SafeRegex ChangingStep(TransitionObservation observation)
    {
        if (observation == null) throw new ArgumentNullException(nameof(observation));
        return SafeRegex.Step(
            observation.PredicateCore,
            $"⟨{observation}⟩");
    }

    /// <summary>Exactly one state-changing step, unconstrained.</summary>
    public SafeRegex AnyChangingStep => SafeRegex.AnyStep;

    /// <summary>
    /// The empty visible word <c>ε</c> — no changing step at all. Any number of
    /// unchanged steps still matches, because they are invisible.
    /// </summary>
    public SafeRegex NoChangingSteps => SafeRegex.NoSteps;

    /// <summary>The empty language <c>∅</c> — no behaviour matches.</summary>
    public SafeRegex NeverMatches => SafeRegex.Never;

    /// <summary>
    /// Stutter-safe sequential prefix <c>R ; φ</c> — <em>some</em> prefix of the
    /// behaviour matches <paramref name="pattern"/> and the remaining suffix
    /// satisfies <paramref name="formula"/>.
    ///
    /// <para>The split point is the position just after the last changing step
    /// consumed by the pattern, up to invisible unchanged steps. There is no
    /// overlapping variant here: overlapping prefix
    /// (<see cref="StutterSensitiveFormulaBuilder{TState}.OvlPrefix"/>),
    /// overlapping match
    /// (<see cref="StutterSensitiveFormulaBuilder{TState}.Match"/>), and fusion
    /// share one physical transition between pattern and formula, which inserted
    /// unchanged steps can move.</para>
    /// </summary>
    public StutterSafeFormula After(SafeRegex pattern, StutterSafeFormula formula)
    {
        if (pattern == null) throw new ArgumentNullException(nameof(pattern));
        if (formula == null) throw new ArgumentNullException(nameof(formula));
        return new StutterSafeFormula(
            Rltl<IStatePredicate>.SeqPrefix(pattern.Lower(), formula.Core));
    }

    /// <summary>
    /// Stutter-safe universal trigger <c>R ⊳ φ</c> — <em>every</em> prefix
    /// matching <paramref name="pattern"/> is followed by a suffix satisfying
    /// <paramref name="formula"/>. The safety dual of
    /// <see cref="After(SafeRegex, StutterSafeFormula)"/>.
    /// </summary>
    public StutterSafeFormula Whenever(SafeRegex pattern, StutterSafeFormula formula)
    {
        if (pattern == null) throw new ArgumentNullException(nameof(pattern));
        if (formula == null) throw new ArgumentNullException(nameof(formula));
        return new StutterSafeFormula(
            Rltl<IStatePredicate>.Trigger(pattern.Lower(), formula.Core));
    }

    #endregion

    /// <summary>
    /// Opt out of the default stutter-invariance guarantee and access the
    /// complete supported formula language.
    /// </summary>
    public StutterSensitiveFormulaBuilder<TState> AllowStutterSensitiveFormulas()
        => new StutterSensitiveFormulaBuilder<TState>();

    private Rltl<IStatePredicate> Allowed(TransitionObservation observation)
        => RltlAlgebra.Default.Or(unchanged, observation.RltlCore);

    private Rltl<IStatePredicate> Occurs(TransitionObservation observation)
        => RltlAlgebra.Default.And(changed, observation.RltlCore);

    protected static string ResolveObservationName(string name, string expression)
    {
        var resolved = name ?? expression;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new ArgumentException(
                "An observation name is required when its expression cannot be inferred.",
                nameof(name));
        }

        return resolved;
    }
}

/// <summary>
/// Formula builder for the complete supported language. Formulas created here
/// may still be stutter-invariant, but the SDK does not guarantee that they are.
/// </summary>
public sealed class StutterSensitiveFormulaBuilder<TState> : FormulaBuilder<TState>
    where TState : State
{
    #region Transition observations

    /// <summary>
    /// Define an observation over a transition's source and target states.
    /// </summary>
    public new TemporalFormula ObserveTransition(
        Func<TState, TState, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        var diagnosticName = ResolveObservationName(name, expression);
        var prop = StateProp.OverTransition(
            diagnosticName,
            ctx => predicate((TState)ctx.From, (TState)ctx.To));
        return new TemporalFormula(
            Rltl<IStatePredicate>.Atom(new StatePredAtom(prop)));
    }

    /// <summary>
    /// Define a stutter-sensitive observation over the action and metadata on
    /// one physical transition.
    ///
    /// <para>The observation is evaluated literally on changing model edges,
    /// state-neutral model edges, and the synthetic terminal stutter. It
    /// therefore returns <see cref="TemporalFormula"/>, not
    /// <see cref="StutterSafeFormula"/>.</para>
    /// </summary>
    public TemporalFormula ObserveAction(
        Func<Transition, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return ActionFormula(
            ActionPredicate.ForAction(predicate),
            ResolveObservationName(name, expression));
    }

    /// <summary>
    /// Define a stutter-sensitive observation over a transition's source
    /// state, action/metadata view, and target state.
    /// </summary>
    public TemporalFormula ObserveAction(
        Func<TState, Transition, TState, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return ActionFormula(
            ActionPredicate.ForAction(predicate),
            ResolveObservationName(name, expression));
    }

    /// <summary>
    /// Define a stutter-sensitive observation over typed edge metadata.
    /// Letters whose metadata is not <typeparamref name="TMetadata"/> do not
    /// match.
    /// </summary>
    public TemporalFormula ObserveAction<TMetadata>(
        Func<TMetadata, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return ActionFormula(
            ActionPredicate.ForMetadata(predicate),
            ResolveObservationName(name, expression));
    }

    /// <summary>
    /// Define a stutter-sensitive observation over a source state, typed edge
    /// metadata, and target state. Letters whose metadata is not
    /// <typeparamref name="TMetadata"/> do not match.
    /// </summary>
    public TemporalFormula ObserveAction<TMetadata>(
        Func<TState, TMetadata, TState, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return ActionFormula(
            ActionPredicate.ForMetadata(predicate),
            ResolveObservationName(name, expression));
    }

    private static TemporalFormula ActionFormula(
        Func<TransitionContext, bool> predicate,
        string name)
    {
        var prop = StateProp.OverTransition(name, predicate);
        return new TemporalFormula(
            Rltl<IStatePredicate>.Atom(new StatePredAtom(prop)));
    }

    #endregion

    #region Unrestricted operators

    public TemporalFormula Not(TemporalFormula inner)
        => new TemporalFormula(RltlAlgebra.Default.Not(inner.Core));

    public TemporalFormula And(TemporalFormula left, TemporalFormula right)
        => new TemporalFormula(RltlAlgebra.Default.And(left.Core, right.Core));

    public TemporalFormula Or(TemporalFormula left, TemporalFormula right)
        => new TemporalFormula(RltlAlgebra.Default.Or(left.Core, right.Core));

    public TemporalFormula Implies(TemporalFormula antecedent, TemporalFormula consequent)
        => new TemporalFormula(
            RltlAlgebra.Default.Implies(antecedent.Core, consequent.Core));

    public TemporalFormula Next(TemporalFormula inner)
        => new TemporalFormula(Rltl<IStatePredicate>.Next(inner.Core));

    public TemporalFormula Until(TemporalFormula hold, TemporalFormula goal)
        => new TemporalFormula(Rltl<IStatePredicate>.Until(hold.Core, goal.Core));

    public TemporalFormula Release(TemporalFormula release, TemporalFormula hold)
        => new TemporalFormula(Rltl<IStatePredicate>.Release(release.Core, hold.Core));

    public TemporalFormula Eventually(TemporalFormula inner)
        => new TemporalFormula(Rltl<IStatePredicate>.Eventually(inner.Core));

    public TemporalFormula Always(TemporalFormula inner)
        => new TemporalFormula(Rltl<IStatePredicate>.Globally(inner.Core));

    public TemporalFormula InfinitelyOften(TemporalFormula inner)
        => Always(Eventually(inner));

    public TemporalFormula Stabilizes(TemporalFormula inner)
        => Eventually(Always(inner));

    public TemporalFormula LeadsTo(TemporalFormula trigger, TemporalFormula response)
        => Always(Implies(trigger, Eventually(response)));

    #endregion

    #region Node-level enabledness

    /// <summary>
    /// <c>ENABLED A</c> — holds at a state-graph node iff at least one
    /// <em>changing</em> outgoing model edge of that node is produced by an
    /// action satisfying <paramref name="selector"/>.
    ///
    /// <para>State-neutral edges do not count, matching the compatibility
    /// fairness APIs: an action that leaves the state unchanged is neither
    /// enabled nor taken. A terminal node therefore enables nothing. Use
    /// <see cref="EnabledAction(Func{Transition, bool}, string, string)"/> for
    /// an explicitly selected semantic state-neutral edge.</para>
    ///
    /// <para>Enabledness is read from the node, not from the state alone: a
    /// node is a (state, active step-function set) pair, so two nodes with
    /// equal states can enable different actions. That is exactly why
    /// <c>Enabled</c> is stutter-sensitive and lives on this builder.</para>
    ///
    /// <para>At an unexpanded or depth-truncated frontier the node's outgoing
    /// edges are unknown, so a check whose verdict depends on one is reported
    /// as <see cref="PropertyCheckingStatus.InconclusiveBound"/> rather than
    /// silently reading "no edges" as "nothing enabled".</para>
    /// </summary>
    public TemporalFormula Enabled(
        Func<IStepFunction, bool> selector,
        string name = null,
        [CallerArgumentExpression("selector")] string expression = null)
        => EnabledFormula(
            ActionPredicate.ForStep(selector),
            ResolveEnabledName(name, expression));

    /// <summary>
    /// <c>ENABLED A</c> for every action of step-function type
    /// <typeparamref name="TStep"/>. See
    /// <see cref="Enabled(Func{IStepFunction, bool}, string, string)"/>.
    /// </summary>
    public TemporalFormula Enabled<TStep>(string name = null)
        where TStep : IStepFunction
        => EnabledFormula(
            ActionPredicate.ForStepType<TStep>(),
            ResolveEnabledName(name, typeof(TStep).Name));

    /// <summary>
    /// <c>ENABLED A</c> for the changing edges whose source and target states
    /// satisfy <paramref name="relation"/>. See
    /// <see cref="Enabled(Func{IStepFunction, bool}, string, string)"/>.
    /// </summary>
    public TemporalFormula Enabled(
        Func<TState, TState, bool> relation,
        string name = null,
        [CallerArgumentExpression("relation")] string expression = null)
        => EnabledFormula(
            ActionPredicate.ForRelation<TState>(relation),
            ResolveEnabledName(name, expression));

    /// <summary>
    /// <c>ENABLED A</c> for the changing edges satisfying the full edge
    /// predicate <paramref name="predicate"/>. See
    /// <see cref="Enabled(Func{IStepFunction, bool}, string, string)"/>.
    /// </summary>
    public TemporalFormula Enabled(
        Func<TState, IStepFunction, TState, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
        => EnabledFormula(
            ActionPredicate.ForEdge<TState>(predicate),
            ResolveEnabledName(name, expression));

    /// <summary>
    /// <c>ENABLED A</c> for the changing edges satisfying
    /// <paramref name="observation"/> — the same transition observation
    /// accepted by <see cref="Fairness.Weak(TransitionObservation)"/>. See
    /// <see cref="Enabled(Func{IStepFunction, bool}, string, string)"/>.
    /// </summary>
    public TemporalFormula Enabled(
        TransitionObservation observation,
        string name = null)
        => EnabledFormula(
            ActionPredicate.ForObservation(observation),
            ResolveEnabledName(name, observation?.ToString()));

    /// <summary>
    /// Metadata/action-aware enabledness. Holds at an exact graph
    /// configuration iff at least one outgoing model edge satisfies
    /// <paramref name="predicate"/>. A selected state-neutral edge counts.
    ///
    /// <para>The synthetic terminal stutter is not a model edge, so a terminal
    /// node enables nothing. This remains stutter-sensitive because two graph
    /// configurations with equal domain states may offer different edges.</para>
    /// </summary>
    public TemporalFormula EnabledAction(
        Func<Transition, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
        => EnabledActionFormula(
            ActionPredicate.ForAction(predicate),
            ResolveEnabledActionName(name, expression));

    /// <summary>
    /// Metadata/action-aware enabledness over source state, action/metadata,
    /// and target state. A selected state-neutral model edge counts.
    /// </summary>
    public TemporalFormula EnabledAction(
        Func<TState, Transition, TState, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
        => EnabledActionFormula(
            ActionPredicate.ForAction(predicate),
            ResolveEnabledActionName(name, expression));

    /// <summary>
    /// Metadata/action-aware enabledness over typed edge metadata. Edges whose
    /// metadata is not <typeparamref name="TMetadata"/> do not match.
    /// </summary>
    public TemporalFormula EnabledAction<TMetadata>(
        Func<TMetadata, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
        => EnabledActionFormula(
            ActionPredicate.ForMetadata(predicate),
            ResolveEnabledActionName(name, expression));

    /// <summary>
    /// Metadata/action-aware enabledness over source state, typed edge
    /// metadata, and target state.
    /// </summary>
    public TemporalFormula EnabledAction<TMetadata>(
        Func<TState, TMetadata, TState, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
        => EnabledActionFormula(
            ActionPredicate.ForMetadata(predicate),
            ResolveEnabledActionName(name, expression));

    private static TemporalFormula EnabledFormula(
        Func<TransitionContext, bool> action, string name)
        => new TemporalFormula(
            Rltl<IStatePredicate>.Atom(
                new StatePredAtom(StateProp.Enabled(name, action))));

    private static TemporalFormula EnabledActionFormula(
        Func<TransitionContext, bool> action, string name)
        => new TemporalFormula(
            Rltl<IStatePredicate>.Atom(
                new StatePredAtom(StateProp.EnabledAction(name, action))));

    private static string ResolveEnabledName(string name, string expression)
        => name ?? $"Enabled({ResolveObservationName(null, expression)})";

    private static string ResolveEnabledActionName(string name, string expression)
        => name ?? $"EnabledAction({ResolveObservationName(null, expression)})";

    #endregion

    #region Regex-prefix operators (RLTL)

    /// <summary>
    /// Sequential prefix: <c>R ; φ</c> — there exists a prefix matching R,
    /// after which φ holds.
    /// </summary>
    public TemporalFormula SeqPrefix(RegexPattern r, TemporalFormula phi)
        => new TemporalFormula(Rltl<IStatePredicate>.SeqPrefix(r.Core, phi.Core));

    /// <summary>
    /// Overlapping prefix: <c>R : φ</c> — like SeqPrefix but the last
    /// letter of the match overlaps with the first letter of the suffix.
    /// </summary>
    public TemporalFormula OvlPrefix(RegexPattern r, TemporalFormula phi)
        => new TemporalFormula(Rltl<IStatePredicate>.OvlPrefix(r.Core, phi.Core));

    /// <summary>
    /// Trigger: <c>R ⊳ φ</c> — for every prefix matching R, the suffix
    /// satisfies φ. The universal (safety) dual of SeqPrefix.
    /// </summary>
    public TemporalFormula Trigger(RegexPattern r, TemporalFormula phi)
        => new TemporalFormula(Rltl<IStatePredicate>.Trigger(r.Core, phi.Core));

    /// <summary>
    /// Match: <c>R ⊳⊳ φ</c> — overlapping universal variant of Trigger.
    /// </summary>
    public TemporalFormula Match(RegexPattern r, TemporalFormula phi)
        => new TemporalFormula(Rltl<IStatePredicate>.Match(r.Core, phi.Core));

    #endregion
}
