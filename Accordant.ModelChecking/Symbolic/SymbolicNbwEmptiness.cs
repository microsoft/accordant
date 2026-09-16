namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Standalone language-emptiness check for a <see cref="SymbolicNBW{TPredicate,TElement,TState}"/>
    /// — no model program, no product. Used by
    /// <see cref="RltlLanguageEquivalence"/> and similar formula-level
    /// checkers to decide <c>L(NBW) = ∅</c> modulo the precision of
    /// <see cref="IPredicateAlgebra{T}.IsSatisfiable"/>.
    ///
    /// <para>
    /// Algorithm: Courcoubetis–Vardi–Wolper–Yannakakis nested DFS, but the
    /// successor enumerator walks transition terms by branch instead of by
    /// concrete state. For each leaf, the accumulated path condition is
    /// checked with <c>eba.IsSatisfiable</c>; leaves with an unsatisfiable
    /// path are pruned.
    /// </para>
    ///
    /// <para>
    /// Soundness/completeness depends on <c>IsSatisfiable</c>:
    /// </para>
    /// <list type="bullet">
    ///   <item>Precise IsSatisfiable (e.g. a decision procedure): result is
    ///     sound and complete.</item>
    ///   <item>Conservative-true IsSatisfiable (e.g. <see cref="StatePropEba"/>'s
    ///     opaque-predicate default): equivalence reduces to symbolic
    ///     equivalence over independent predicate symbols — every distinct
    ///     predicate is assumed satisfiable. This is the standard semantics
    ///     for formula equivalence at the symbolic-automaton level.</item>
    /// </list>
    /// </summary>
    internal static class SymbolicNbwEmptiness
    {
        /// <summary>
        /// Returns <c>true</c> iff the NBW's language is empty modulo
        /// the precision of <c>eba.IsSatisfiable</c>.
        /// </summary>
        public static bool IsEmpty<TPredicate, TElement, TState>(
            SymbolicNBW<TPredicate, TElement, TState> nbw,
            IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
            IEqualityComparer<TState> stateComparer = null)
        {
            if (nbw == null) throw new ArgumentNullException(nameof(nbw));
            if (eba == null) throw new ArgumentNullException(nameof(eba));

            var cmp = stateComparer ?? EqualityComparer<TState>.Default;
            var visited1 = new HashSet<TState>(cmp);
            var visited2 = new HashSet<TState>(cmp);
            var registry = nbw.Registry;

            // Enumerate symbolic successors: all states reachable along any
            // satisfiable ITE branch in any of the NBW transitions for q.
            IEnumerable<TState> Successors(TState q)
            {
                var seen = new HashSet<TState>(cmp);
                foreach (var term in nbw.GetTransition(q))
                {
                    foreach (var (leaf, guard) in EnumerateLeaves(term, eba.Top, eba, registry))
                    {
                        if (!eba.IsSatisfiable(guard)) continue;
                        foreach (var s in leaf)
                            if (seen.Add(s))
                                yield return s;
                    }
                }
            }

            foreach (var init in nbw.InitialStates)
            {
                if (visited1.Contains(init)) continue;
                if (OuterDfs(init, nbw.IsAccepting, Successors, visited1, visited2))
                    return false; // accepting lasso found
            }
            return true;
        }

        // Outer DFS with iterative frame stack. On post-order of an accepting
        // state we launch the inner DFS; if it finds a cycle, language is
        // non-empty. Returns true iff an accepting lasso was found.
        private static bool OuterDfs<TState>(
            TState seed,
            Func<TState, bool> isAccepting,
            Func<TState, IEnumerable<TState>> successors,
            HashSet<TState> visited1,
            HashSet<TState> visited2)
        {
            var stack = new Stack<(TState node, IEnumerator<TState> enumerator)>();
            visited1.Add(seed);
            stack.Push((seed, successors(seed).GetEnumerator()));

            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (top.enumerator.MoveNext())
                {
                    var child = top.enumerator.Current;
                    if (visited1.Add(child))
                        stack.Push((child, successors(child).GetEnumerator()));
                }
                else
                {
                    top.enumerator.Dispose();
                    stack.Pop();
                    if (isAccepting(top.node))
                    {
                        if (InnerDfs(top.node, successors, visited2))
                            return true;
                    }
                }
            }
            return false;
        }

        // Inner DFS: from seed, look for a path back to seed.
        // visited2 is shared across all inner-DFS invocations (NDFS invariant).
        private static bool InnerDfs<TState>(
            TState seed,
            Func<TState, IEnumerable<TState>> successors,
            HashSet<TState> visited2)
        {
            if (!visited2.Add(seed)) return false;
            var stack = new Stack<IEnumerator<TState>>();
            stack.Push(successors(seed).GetEnumerator());

            var cmp = visited2.Comparer;
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (top.MoveNext())
                {
                    var child = top.Current;
                    if (cmp.Equals(child, seed))
                        return true;
                    if (visited2.Add(child))
                        stack.Push(successors(child).GetEnumerator());
                }
                else
                {
                    top.Dispose();
                    stack.Pop();
                }
            }
            return false;
        }

        // Enumerate (leaf, accumulated-path-condition) pairs by traversing an
        // ITE transition term. Prunes branches whose path is unsatisfiable.
        private static IEnumerable<(StateSet<TState> leaf, TPredicate guard)>
            EnumerateLeaves<TPredicate, TElement, TState>(
                TransitionTerm<StateSet<TState>> term,
                TPredicate path,
                IEffectiveBooleanAlgebra<TPredicate, TElement> eba,
                ConditionRegistry<TPredicate> registry)
        {
            if (term is TransitionTermLeaf<StateSet<TState>> leaf)
            {
                yield return (leaf.Value, path);
                yield break;
            }

            var ite = (TransitionTermIte<StateSet<TState>>)term;
            var cond = registry.GetPredicate(ite.ConditionIndex);
            var hiPath = eba.And(path, cond);
            if (eba.IsSatisfiable(hiPath))
                foreach (var t in EnumerateLeaves(ite.Hi, hiPath, eba, registry))
                    yield return t;

            var loPath = eba.And(path, eba.Not(cond));
            if (eba.IsSatisfiable(loPath))
                foreach (var t in EnumerateLeaves(ite.Lo, loPath, eba, registry))
                    yield return t;
        }
    }
}
