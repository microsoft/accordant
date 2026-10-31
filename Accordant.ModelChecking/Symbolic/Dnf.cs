namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A positive Boolean formula B⁺(Q) in disjunctive normal form (DNF).
    /// (Section 4.1 of the paper)
    /// 
    /// Represents a disjunction of conjunctions of states:
    ///   φ = (q₁ ∧ q₂ ∧ ...) ∨ (q₃ ∧ q₄ ∧ ...) ∨ ...
    /// 
    /// Each conjunction is a <see cref="StateSet{TState}"/> (states that must ALL
    /// hold simultaneously), and the formula is satisfied if ANY conjunction is satisfied.
    /// 
    /// Maintained in canonical form: clauses form a minimal antichain
    /// (no clause is a subset of another), sorted lexicographically.
    /// This ensures structural equality ⟺ semantic equivalence.
    /// 
    /// <list type="bullet">
    ///   <item>⊤ = { {} } — one empty conjunction (always true)</item>
    ///   <item>⊥ = { } — no conjunctions (always false)</item>
    ///   <item>atom(q) = { {q} } — state q must hold</item>
    ///   <item>φ ∨ ψ = union of clauses, then minimize</item>
    ///   <item>φ ∧ ψ = { c₁ ∪ c₂ | c₁ ∈ φ, c₂ ∈ ψ }, then minimize</item>
    /// </list>
    /// </summary>
    public sealed class Dnf<TState> : IEquatable<Dnf<TState>>
    {
        private readonly StateSet<TState>[] _clauses; // sorted, minimal antichain
        private int? _hash;

        internal Dnf(StateSet<TState>[] clauses)
        {
            _clauses = clauses;
        }

        /// <summary>The clauses (conjunctions) of this DNF formula.</summary>
        public IReadOnlyList<StateSet<TState>> Clauses => _clauses;

        /// <summary>True if this is ⊤ = { {} } (one empty conjunction).</summary>
        public bool IsTrue => _clauses.Length == 1 && _clauses[0].IsEmpty;

        /// <summary>True if this is ⊥ = { } (no conjunctions).</summary>
        public bool IsFalse => _clauses.Length == 0;

        /// <summary>Number of clauses (disjuncts).</summary>
        public int ClauseCount => _clauses.Length;

        /// <summary>
        /// Returns all states mentioned in any clause.
        /// </summary>
        public IEnumerable<TState> GetAllStates()
        {
            var seen = new HashSet<TState>();
            foreach (var clause in _clauses)
                foreach (var state in clause)
                    if (seen.Add(state))
                        yield return state;
        }

        #region Equality

        public bool Equals(Dnf<TState> other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (_clauses.Length != other._clauses.Length) return false;
            for (int i = 0; i < _clauses.Length; i++)
                if (!_clauses[i].Equals(other._clauses[i]))
                    return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as Dnf<TState>);

        public override int GetHashCode()
        {
            if (_hash == null)
            {
                unchecked
                {
                    int h = 17;
                    foreach (var c in _clauses)
                        h = h * 31 + c.GetHashCode();
                    _hash = h;
                }
            }
            return _hash.Value;
        }

        public static bool operator ==(Dnf<TState> left, Dnf<TState> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(Dnf<TState> left, Dnf<TState> right)
            => !(left == right);

        #endregion

        public override string ToString()
        {
            if (IsFalse) return "⊥";
            if (IsTrue) return "⊤";
            return string.Join(" ∨ ", _clauses.Select(c =>
                c.Count == 1 ? c.First().ToString() :
                "(" + string.Join(" ∧ ", c) + ")"));
        }
    }

    /// <summary>
    /// Leaf algebra for B⁺(Q) in DNF form.
    /// Implements <see cref="ILeafAlgebra{TLeaf}"/> for <see cref="Dnf{TState}"/>.
    /// 
    /// B⁺(Q) is a positive Boolean algebra (no negation). The <see cref="Not"/>
    /// method throws <see cref="NotSupportedException"/>.
    /// 
    /// All operations maintain the minimal antichain invariant via subsumption
    /// checking: if clause c₁ ⊆ c₂, then c₂ is removed (c₁ is more general).
    /// </summary>
    public class DnfAlgebra<TState> : ILeafAlgebra<Dnf<TState>>
    {
        private readonly IComparer<TState> _stateComparer;
        private readonly Dnf<TState> _top;
        private readonly Dnf<TState> _bottom;

        public DnfAlgebra(IComparer<TState> stateComparer)
        {
            _stateComparer = stateComparer ?? throw new ArgumentNullException(nameof(stateComparer));
            _top = new Dnf<TState>(new[] { StateSet<TState>.Empty(stateComparer) });
            _bottom = new Dnf<TState>(Array.Empty<StateSet<TState>>());
        }

        /// <summary>The state comparer used for canonical ordering.</summary>
        public IComparer<TState> StateComparer => _stateComparer;

        /// <summary>⊤ = { {} } — satisfied by any state assignment.</summary>
        public Dnf<TState> Top => _top;

        /// <summary>⊥ = { } — never satisfied.</summary>
        public Dnf<TState> Bottom => _bottom;

        /// <summary>Creates an atom: { {q} } — state q must hold.</summary>
        public Dnf<TState> Atom(TState state)
            => new Dnf<TState>(new[] { StateSet<TState>.Singleton(state, _stateComparer) });

        /// <summary>Creates a single conjunction clause from a set of states.</summary>
        public Dnf<TState> Clause(IEnumerable<TState> states)
            => new Dnf<TState>(new[] { new StateSet<TState>(states, _stateComparer) });

        /// <summary>Creates a Dnf from multiple clauses, normalizing.</summary>
        public Dnf<TState> FromClauses(IEnumerable<StateSet<TState>> clauses)
            => new Dnf<TState>(Minimize(new List<StateSet<TState>>(clauses)));

        public bool IsTop(Dnf<TState> a) => a != null && a.IsTrue;
        public bool IsBottom(Dnf<TState> a) => a != null && a.IsFalse;

        /// <summary>
        /// Disjunction: φ ∨ ψ = union of clause sets, then minimize.
        /// </summary>
        public Dnf<TState> Or(Dnf<TState> a, Dnf<TState> b)
        {
            if (a.IsFalse) return b;
            if (b.IsFalse) return a;
            if (a.IsTrue || b.IsTrue) return _top;

            var all = new List<StateSet<TState>>(a.ClauseCount + b.ClauseCount);
            foreach (var c in a.Clauses) all.Add(c);
            foreach (var c in b.Clauses) all.Add(c);
            return new Dnf<TState>(Minimize(all));
        }

        /// <summary>
        /// Conjunction: φ ∧ ψ = cross-product { c₁ ∪ c₂ | c₁ ∈ φ, c₂ ∈ ψ },
        /// then minimize.
        /// </summary>
        public Dnf<TState> And(Dnf<TState> a, Dnf<TState> b)
        {
            if (a.IsFalse || b.IsFalse) return _bottom;
            if (a.IsTrue) return b;
            if (b.IsTrue) return a;

            var result = new List<StateSet<TState>>(a.ClauseCount * b.ClauseCount);
            foreach (var c1 in a.Clauses)
                foreach (var c2 in b.Clauses)
                    result.Add(c1.Union(c2));
            return new Dnf<TState>(Minimize(result));
        }

        /// <summary>
        /// B⁺(Q) does not support negation. Always throws.
        /// </summary>
        public Dnf<TState> Not(Dnf<TState> a)
            => throw new NotSupportedException(
                "Positive Boolean formulas (B⁺) do not support negation.");

        public Dnf<TState> Xor(Dnf<TState> a, Dnf<TState> b)
            => throw new NotSupportedException(
                "Positive Boolean formulas (B⁺) do not support XOR (requires negation).");

        public IEqualityComparer<Dnf<TState>> Comparer
            => EqualityComparer<Dnf<TState>>.Default;

        /// <summary>
        /// Minimizes a set of clauses to a canonical minimal antichain:
        /// removes subsumed clauses (c₁ ⊆ c₂ means c₂ is redundant) and
        /// sorts lexicographically.
        /// </summary>
        internal StateSet<TState>[] Minimize(List<StateSet<TState>> clauses)
        {
            if (clauses.Count == 0) return Array.Empty<StateSet<TState>>();
            if (clauses.Count == 1) return clauses.ToArray();

            // Sort by size for efficient subsumption: smaller clauses first
            clauses.Sort((a, b) => a.Count.CompareTo(b.Count));

            var minimal = new List<StateSet<TState>>();
            foreach (var c in clauses)
            {
                // Is c subsumed by some existing minimal clause?
                // (m ⊆ c means m is more general → c is redundant)
                bool subsumed = false;
                foreach (var m in minimal)
                {
                    if (m.IsSubsetOf(c))
                    {
                        subsumed = true;
                        break;
                    }
                }
                if (!subsumed)
                {
                    // Since we process by ascending size, c cannot subsume
                    // any existing clause of strictly smaller size.
                    // But equal-size clauses may need removal.
                    for (int i = minimal.Count - 1; i >= 0; i--)
                    {
                        if (c.IsSubsetOf(minimal[i]))
                            minimal.RemoveAt(i);
                    }
                    minimal.Add(c);
                }
            }

            // Canonical ordering: lexicographic
            minimal.Sort((a, b) => a.CompareTo(b));
            return minimal.ToArray();
        }
    }
}
