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

    /// <summary>Release: <c>φ R ψ</c> — dual of Until.</summary>
    public StutterSafeFormula Release(StutterSafeFormula release, StutterSafeFormula hold)
        => new StutterSafeFormula(Rltl<IStatePredicate>.Release(release.Core, hold.Core));

    /// <summary>Eventually: <c>◇φ</c> — φ holds at some future state.</summary>
    public StutterSafeFormula Eventually(StutterSafeFormula inner)
        => new StutterSafeFormula(Rltl<IStatePredicate>.Eventually(inner.Core));

    /// <summary>Always: <c>□φ</c> — φ holds at every future state.</summary>
    public StutterSafeFormula Always(StutterSafeFormula inner)
        => new StutterSafeFormula(Rltl<IStatePredicate>.Globally(inner.Core));

    /// <summary>Infinitely often: <c>□◇φ</c> — φ holds infinitely often.</summary>
    public StutterSafeFormula InfinitelyOften(StutterSafeFormula inner)
        => Always(Eventually(inner));

    /// <summary>Stabilizes: <c>◇□φ</c> — φ eventually holds forever.</summary>
    public StutterSafeFormula Stabilizes(StutterSafeFormula inner)
        => Eventually(Always(inner));

    /// <summary>Leads-to: <c>φ ~> ψ</c> = <c>□(φ → ◇ψ)</c> — whenever φ holds,
    /// ψ eventually follows.</summary>
    public StutterSafeFormula LeadsTo(
        StutterSafeFormula trigger,
        StutterSafeFormula response)
        => Always(Implies(trigger, Eventually(response)));

    #endregion

    /// <summary>
    /// Opt out of the default stutter-invariance guarantee and access the
    /// complete supported formula language.
    /// </summary>
    public UnrestrictedFormulaBuilder<TState> WithoutStutterGuarantee()
        => new UnrestrictedFormulaBuilder<TState>();

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
public sealed class UnrestrictedFormulaBuilder<TState> : FormulaBuilder<TState>
    where TState : State
{
    #region Transition observations

    /// <summary>
    /// Define an observation over a transition's source and target states.
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
            ctx => predicate((TState)ctx.From, (TState)ctx.To));
        return new TransitionObservation(new StatePredAtom(prop));
    }

    /// <summary>
    /// Define an observation over a transition's source state, action and
    /// target state.
    /// </summary>
    public TransitionObservation ObserveTransition(
        Func<TState, Transition, TState, bool> predicate,
        string name = null,
        [CallerArgumentExpression("predicate")] string expression = null)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        var diagnosticName = ResolveObservationName(name, expression);
        var prop = StateProp.OverTransition(
            diagnosticName,
            ctx => predicate(
                (TState)ctx.From,
                new Transition(ctx.Action, ctx.Metadata),
                (TState)ctx.To));
        return new TransitionObservation(new StatePredAtom(prop));
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
