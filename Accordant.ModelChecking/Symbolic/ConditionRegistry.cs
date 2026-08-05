namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Manages an ordered set of conditions from an EBA.
    /// Each condition is assigned a unique integer index that defines the
    /// ordering invariant for transition terms (ADDs).
    /// 
    /// In the ITE structure (α ? f : g), inner ITEs must have strictly
    /// larger condition indices than outer ITEs, creating a canonical
    /// representation similar to algebraic decision diagrams.
    /// </summary>
    /// <typeparam name="TPredicate">The type of predicates in the EBA.</typeparam>
    public class ConditionRegistry<TPredicate>
    {
        /// <summary>
        /// Maximum number of propositions supported by the current
        /// metadata representation (a 64-bit free-props bitset on
        /// <c>Ere&lt;TPred&gt;</c>). See EREQ Phase-0 design lock-in
        /// (D2) in the session plan.
        /// </summary>
        public const int MaxPropositions = 64;

        private readonly List<TPredicate> _predicates = new List<TPredicate>();
        private readonly Dictionary<TPredicate, int> _indices;
        private readonly IPredicateAlgebra<TPredicate> _algebra;
        private int _solverAliasCount;

        // Proposition support (EREQ). Indices are allocated as
        // -1, -2, -3, ... so that they sort outermost under our
        // inner-larger ITE ordering.
        private readonly List<string> _propositions = new List<string>();
        private readonly Dictionary<string, int> _propositionIndices = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Creates a new condition registry.
        /// </summary>
        /// <param name="predicateComparer">
        /// Equality comparer for predicates, used to detect duplicate registrations.
        /// If null, the default comparer is used.
        /// </param>
        public ConditionRegistry(IEqualityComparer<TPredicate> predicateComparer = null)
            : this(predicateComparer, null)
        {
        }

        /// <summary>
        /// Creates a new condition registry with optional solver-aware
        /// predicate aliasing. When <paramref name="algebra"/> is non-null,
        /// <see cref="Register"/> falls back from structural lookup to
        /// <see cref="EbaExtensions.AreEquivalent{T}(IPredicateAlgebra{T}, T, T)"/>
        /// against existing entries; semantically equivalent predicates are
        /// aliased to a single index. This is the predicate-level analogue
        /// of the regex-level union-find performed by
        /// <see cref="EreCanonicalizer{TPred,TElem}"/>.
        /// <para>
        /// The aliasing is correct under any <see cref="IPredicateAlgebra{T}"/>:
        /// the default <see cref="EbaExtensions.AreEquivalent{T}"/> reduces to
        /// <c>!IsSatisfiable(Xor(a,b))</c>, which a conservative
        /// <c>IsSatisfiable=true</c> EBA simply never accepts. Precision
        /// improves with the EBA — an SMT-backed
        /// <see cref="IPredicateAlgebraEx{T}"/> can decide it precisely and
        /// collapse many condition indices.
        /// </para>
        /// </summary>
        public ConditionRegistry(
            IEqualityComparer<TPredicate> predicateComparer,
            IPredicateAlgebra<TPredicate> algebra)
        {
            _indices = new Dictionary<TPredicate, int>(predicateComparer ?? EqualityComparer<TPredicate>.Default);
            _algebra = algebra;
        }

        /// <summary>
        /// The number of registered conditions.
        /// </summary>
        public int Count => _predicates.Count;

        /// <summary>
        /// Number of solver-aware aliases recorded so far — i.e. the count
        /// of <see cref="Register"/> calls that found a structurally novel
        /// predicate but aliased it to an existing index via
        /// <see cref="EbaExtensions.AreEquivalent{T}"/>. Zero when no
        /// algebra was provided.
        /// </summary>
        public int SolverAliasCount => _solverAliasCount;

        /// <summary>
        /// Registers a condition and returns its index.
        /// If the condition is already registered, returns the existing index.
        /// </summary>
        public int Register(TPredicate predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            if (_indices.TryGetValue(predicate, out var existing))
                return existing;

            if (_algebra != null)
            {
                for (int i = 0; i < _predicates.Count; i++)
                {
                    if (_algebra.AreEquivalent(predicate, _predicates[i]))
                    {
                        _indices[predicate] = i;
                        _solverAliasCount++;
                        return i;
                    }
                }
            }

            var index = _predicates.Count;
            _predicates.Add(predicate);
            _indices[predicate] = index;
            return index;
        }

        /// <summary>
        /// Gets the predicate for a given condition index.
        /// </summary>
        public TPredicate GetPredicate(int index)
        {
            if (index < 0 || index >= _predicates.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _predicates[index];
        }

        /// <summary>
        /// Gets the index of a previously registered predicate.
        /// Returns -1 if not found.
        /// </summary>
        public int IndexOf(TPredicate predicate)
        {
            return _indices.TryGetValue(predicate, out var index) ? index : -1;
        }

        /// <summary>
        /// Returns all registered predicates in order.
        /// </summary>
        public IReadOnlyList<TPredicate> Predicates => _predicates;

        // ---------------------------------------------------------------
        // Proposition support (EREQ Phase 1, design lock-in D1).
        //
        // Propositions are a separate condition kind from predicates and
        // occupy negative indices (-1, -2, -3, ...). Their separation
        // from <see cref="TPredicate"/> means the EBA never sees them
        // as predicates, while their negative index value places them
        // outermost in transition terms under the existing
        // inner-larger ITE ordering invariant.
        // ---------------------------------------------------------------

        /// <summary>
        /// The number of registered propositions.
        /// </summary>
        public int PropositionCount => _propositions.Count;

        /// <summary>
        /// All registered proposition names, in registration order
        /// (so <c>Propositions[i]</c> has index <c>-(i+1)</c>).
        /// </summary>
        public IReadOnlyList<string> Propositions => _propositions;

        /// <summary>
        /// Returns <c>true</c> iff <paramref name="index"/> denotes a
        /// proposition (i.e. is strictly negative).
        /// </summary>
        public static bool IsProposition(int index) => index < 0;

        /// <summary>
        /// Registers a proposition by name and returns its index. If a
        /// proposition with the same name was already registered, the
        /// existing index is returned. New propositions receive
        /// monotonically decreasing negative indices starting at -1.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when registering would exceed
        /// <see cref="MaxPropositions"/>.
        /// </exception>
        public int RegisterProposition(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            if (_propositionIndices.TryGetValue(name, out var existing))
                return existing;

            if (_propositions.Count >= MaxPropositions)
                throw new InvalidOperationException(
                    $"ConditionRegistry supports at most {MaxPropositions} propositions.");

            var index = -(_propositions.Count + 1);
            _propositions.Add(name);
            _propositionIndices[name] = index;
            return index;
        }

        /// <summary>
        /// Returns the name of a previously registered proposition.
        /// </summary>
        public string GetPropositionName(int index)
        {
            if (!IsProposition(index))
                throw new ArgumentOutOfRangeException(nameof(index),
                    "Index is not a proposition (must be strictly negative).");
            var pos = -index - 1;
            if (pos >= _propositions.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _propositions[pos];
        }

        /// <summary>
        /// Returns the index of a previously registered proposition,
        /// or <c>0</c> if none is registered under that name. (Zero is
        /// safe as a not-found sentinel because all valid proposition
        /// indices are strictly negative.)
        /// </summary>
        public int IndexOfProposition(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            return _propositionIndices.TryGetValue(name, out var idx) ? idx : 0;
        }
    }
}
