namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A single position in an EREQ witness: the predicate guard that
    /// constrains the letter at this position, paired with the
    /// proposition valuation chosen for that letter (per Phase-2 D1:
    /// propositions are negative-indexed in the
    /// <see cref="ConditionRegistry{TPredicate}"/>). For non-EREQ
    /// regexes <see cref="Propositions"/> is empty.
    /// </summary>
    public sealed class EreWitnessStep<TPred>
    {
        public TPred Predicate { get; }
        public IReadOnlyDictionary<int, bool> Propositions { get; }

        public EreWitnessStep(TPred predicate, IReadOnlyDictionary<int, bool> propositions)
        {
            Predicate = predicate;
            Propositions = propositions ?? EmptyDict;
        }

        private static readonly IReadOnlyDictionary<int, bool> EmptyDict =
            new Dictionary<int, bool>(0);
    }

    /// <summary>
    /// Helpers for turning a reversed symbolic witness (a
    /// <see cref="ConsList{TPred}"/> of path-condition predicates, head =
    /// last symbol) into a concrete forward sequence of elements.
    ///
    /// <para>The EBA exposes <see cref="IEffectiveBooleanAlgebra{TPred,TElem}.Models"/>
    /// (a test) but not a "pick a satisfying element" primitive — that is
    /// domain-specific. Callers therefore supply a <c>chooseModel</c>
    /// delegate that returns some element satisfying a given predicate.</para>
    /// </summary>
    public static class EreWitness
    {
        /// <summary>
        /// Reverses <paramref name="witnessReverse"/> into forward order
        /// (first symbol first). Allocates a single array.
        /// </summary>
        public static IReadOnlyList<TPred> ToForward<TPred>(
            ConsList<TPred> witnessReverse)
        {
            if (witnessReverse == null) return Array.Empty<TPred>();
            var arr = new TPred[witnessReverse.Count];
            int i = arr.Length - 1;
            for (var n = witnessReverse; !n.IsEmpty; n = n.Tail) arr[i--] = n.Head;
            return arr;
        }

        /// <summary>
        /// Materialises a concrete element word from the reversed symbolic
        /// witness, using <paramref name="chooseModel"/> to instantiate each
        /// predicate. The result is in forward order.
        /// </summary>
        public static IReadOnlyList<TElem> Materialise<TPred, TElem>(
            ConsList<TPred> witnessReverse, Func<TPred, TElem> chooseModel)
        {
            if (chooseModel == null) throw new ArgumentNullException(nameof(chooseModel));
            var preds = ToForward(witnessReverse);
            var arr = new TElem[preds.Count];
            for (int i = 0; i < preds.Count; i++) arr[i] = chooseModel(preds[i]);
            return arr;
        }

        /// <summary>
        /// Materialises a concrete element word using the EBA's
        /// <see cref="IEffectiveBooleanAlgebraEx{TP,TE}.TryGetModel"/>
        /// capability when available, falling back to
        /// <paramref name="chooseModel"/> for predicates the EBA cannot
        /// model. The <paramref name="chooseModel"/> parameter may be
        /// <c>null</c>, in which case predicates the EBA cannot model
        /// produce <c>default(TElem)</c>.
        /// </summary>
        public static IReadOnlyList<TElem> Materialise<TPred, TElem>(
            ConsList<TPred> witnessReverse,
            IEffectiveBooleanAlgebra<TPred, TElem> algebra,
            Func<TPred, TElem> chooseModel = null)
        {
            if (algebra == null) throw new ArgumentNullException(nameof(algebra));
            var preds = ToForward(witnessReverse);
            var arr = new TElem[preds.Count];
            for (int i = 0; i < preds.Count; i++)
            {
                if (algebra.TryGetModel(preds[i], out var elem))
                    arr[i] = elem;
                else if (chooseModel != null)
                    arr[i] = chooseModel(preds[i]);
                else
                    arr[i] = default;
            }
            return arr;
        }

        /// <summary>
        /// EREQ Phase-4 reverse helper for the quantified-witness API:
        /// turns a reversed list of <see cref="EreWitnessStep{TPred}"/>
        /// (head = last symbol) into forward order.
        /// </summary>
        public static IReadOnlyList<EreWitnessStep<TPred>> ToForward<TPred>(
            ConsList<EreWitnessStep<TPred>> witnessReverse)
        {
            if (witnessReverse == null) return Array.Empty<EreWitnessStep<TPred>>();
            var arr = new EreWitnessStep<TPred>[witnessReverse.Count];
            int i = arr.Length - 1;
            for (var n = witnessReverse; !n.IsEmpty; n = n.Tail) arr[i--] = n.Head;
            return arr;
        }

        /// <summary>
        /// EREQ Phase-4 materialiser: turns a reversed quantified witness
        /// into a forward list of (concrete-element, proposition-valuation)
        /// pairs. The element is picked from the position's predicate via
        /// the EBA's <see cref="IEffectiveBooleanAlgebra{TP,TE}.TryGetModel"/>
        /// (or <paramref name="chooseModel"/> as fallback); the proposition
        /// valuation is forwarded unchanged.
        /// </summary>
        public static IReadOnlyList<(TElem letter, IReadOnlyDictionary<int, bool> propVals)>
            Materialise<TPred, TElem>(
                ConsList<EreWitnessStep<TPred>> witnessReverse,
                IEffectiveBooleanAlgebra<TPred, TElem> algebra,
                Func<TPred, TElem> chooseModel = null)
        {
            if (algebra == null) throw new ArgumentNullException(nameof(algebra));
            var steps = ToForward(witnessReverse);
            var arr = new (TElem, IReadOnlyDictionary<int, bool>)[steps.Count];
            for (int i = 0; i < steps.Count; i++)
            {
                TElem e;
                if (algebra.TryGetModel(steps[i].Predicate, out var modelled))
                    e = modelled;
                else if (chooseModel != null)
                    e = chooseModel(steps[i].Predicate);
                else
                    e = default;
                arr[i] = (e, steps[i].Propositions);
            }
            return arr;
        }
    }
}
