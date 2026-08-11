namespace Microsoft.Accordant.ModelChecking
{
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// The canonical split of the transition alphabet into <em>unchanged</em>
    /// and <em>changing</em> steps.
    ///
    /// <para>A letter presented to a proposition is a transition
    /// <c>s --(a)--&gt; s'</c> (see <see cref="TransitionContext"/>). The letter
    /// is <em>unchanged</em> when <c>s</c> and <c>s'</c> are semantically equal
    /// under <see cref="StateSemantics.Equal"/> — the same test used by
    /// fairness, <c>ENABLED</c>, and the stutter-safe temporal operators — and
    /// <em>changing</em> otherwise. Terminal stutter self-loops are unchanged
    /// by construction.</para>
    ///
    /// <para>Every stutter-safe construct in the SDK is defined against this one
    /// predicate so that "unchanged" means exactly the same thing in temporal
    /// operators, in <see cref="SafeRegex"/> patterns, and in fairness.</para>
    /// </summary>
    internal static class StutterAlphabet
    {
        /// <summary>The canonical <c>Unchanged(s, s')</c> proposition.</summary>
        internal static readonly StateProp UnchangedProp = StateProp.OverTransition(
            "Unchanged",
            context => StateSemantics.Equal(context.From, context.To));

        /// <summary>The canonical unchanged-step predicate.</summary>
        internal static readonly IStatePredicate UnchangedPredicate =
            new StatePredAtom(UnchangedProp);

        /// <summary>
        /// The changing-step predicate — the complement of
        /// <see cref="UnchangedPredicate"/>.
        /// </summary>
        internal static readonly IStatePredicate ChangingPredicate =
            new StatePredNot(UnchangedPredicate);

        /// <summary>The unchanged-step predicate as a temporal atom.</summary>
        internal static readonly Rltl<IStatePredicate> Unchanged =
            Rltl<IStatePredicate>.Atom(UnchangedPredicate);

        /// <summary>The changing-step predicate as a temporal atom.</summary>
        internal static readonly Rltl<IStatePredicate> Changing =
            RltlAlgebra.Default.Not(Unchanged);

        /// <summary>The single-letter language <c>U</c> of unchanged steps.</summary>
        internal static readonly Ere<IStatePredicate> UnchangedStep =
            Ere<IStatePredicate>.Atom(UnchangedPredicate);

        /// <summary>
        /// <c>U*</c> — every finite word made only of unchanged steps. This is
        /// the inverse image of the empty visible word under erasure of
        /// unchanged steps.
        /// </summary>
        internal static readonly Ere<IStatePredicate> UnchangedSteps =
            Ere<IStatePredicate>.Star(UnchangedStep);

        /// <summary>
        /// The single-letter language of changing steps additionally satisfying
        /// <paramref name="observation"/> — the <c>Changed ∧ A</c> letter of the
        /// stutter lift. <c>null</c> denotes no extra constraint.
        /// </summary>
        internal static Ere<IStatePredicate> ChangingStep(IStatePredicate observation)
            => Ere<IStatePredicate>.Atom(ChangingWith(observation));

        internal static IStatePredicate ChangingWith(IStatePredicate observation)
        {
            if (observation == null || observation is StatePredTrue)
                return ChangingPredicate;
            return new StatePredAnd(ChangingPredicate, observation);
        }
    }
}
