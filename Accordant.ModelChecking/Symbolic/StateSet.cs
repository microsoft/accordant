namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// An immutable sorted set of states with value-based equality.
    /// Used as successor sets P(Q) in NBW transitions and as
    /// conjunction clauses in <see cref="Dnf{TState}"/> (B⁺(Q) representation).
    /// 
    /// States are ordered by the provided <see cref="IComparer{TState}"/>.
    /// Equality is structural: two sets are equal iff they contain the
    /// same elements (per the comparer).
    /// </summary>
    public sealed class StateSet<TState> : IEquatable<StateSet<TState>>,
        IComparable<StateSet<TState>>, IReadOnlyCollection<TState>
    {
        private readonly TState[] _states; // sorted by _comparer, no duplicates
        private readonly IComparer<TState> _comparer;
        private int? _hash;

        /// <summary>
        /// Creates a state set from the given states, sorting and deduplicating.
        /// </summary>
        /// <param name="states">The states to include.</param>
        /// <param name="comparer">
        /// Comparer for ordering. Must be consistent with GetHashCode of TState:
        /// if Compare(a,b)==0 then a.GetHashCode()==b.GetHashCode().
        /// </param>
        public StateSet(IEnumerable<TState> states, IComparer<TState> comparer)
        {
            _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            var list = new List<TState>(states);
            list.Sort(comparer);
            // Deduplicate (sorted, so duplicates are adjacent)
            int write = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (i == 0 || comparer.Compare(list[i], list[i - 1]) != 0)
                    list[write++] = list[i];
            }
            if (write < list.Count)
                list.RemoveRange(write, list.Count - write);
            _states = list.ToArray();
        }

        /// <summary>
        /// Internal constructor for pre-sorted, pre-deduplicated arrays.
        /// </summary>
        internal StateSet(TState[] sortedUniqueStates, IComparer<TState> comparer)
        {
            _states = sortedUniqueStates ?? throw new ArgumentNullException(nameof(sortedUniqueStates));
            _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        }

        /// <summary>Creates an empty state set.</summary>
        public static StateSet<TState> Empty(IComparer<TState> comparer)
            => new StateSet<TState>(Array.Empty<TState>(), comparer);

        /// <summary>Creates a singleton state set.</summary>
        public static StateSet<TState> Singleton(TState state, IComparer<TState> comparer)
            => new StateSet<TState>(new[] { state }, comparer);

        /// <summary>The comparer used for ordering states.</summary>
        public IComparer<TState> Comparer => _comparer;

        /// <summary>Number of states in the set.</summary>
        public int Count => _states.Length;

        /// <summary>True if the set is empty.</summary>
        public bool IsEmpty => _states.Length == 0;

        /// <summary>Gets the state at the given index (0-based, sorted order).</summary>
        public TState this[int index] => _states[index];

        /// <summary>Returns true if the set contains the given state.</summary>
        public bool Contains(TState state)
            => Array.BinarySearch(_states, state, _comparer) >= 0;

        /// <summary>Returns the union of this set with another.</summary>
        public StateSet<TState> Union(StateSet<TState> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (_states.Length == 0) return other;
            if (other._states.Length == 0) return this;

            var result = new List<TState>(_states.Length + other._states.Length);
            int i = 0, j = 0;
            while (i < _states.Length && j < other._states.Length)
            {
                int cmp = _comparer.Compare(_states[i], other._states[j]);
                if (cmp < 0) result.Add(_states[i++]);
                else if (cmp > 0) result.Add(other._states[j++]);
                else { result.Add(_states[i++]); j++; }
            }
            while (i < _states.Length) result.Add(_states[i++]);
            while (j < other._states.Length) result.Add(other._states[j++]);
            return new StateSet<TState>(result.ToArray(), _comparer);
        }

        /// <summary>Returns the intersection of this set with another.</summary>
        public StateSet<TState> Intersect(StateSet<TState> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var result = new List<TState>(Math.Min(_states.Length, other._states.Length));
            int i = 0, j = 0;
            while (i < _states.Length && j < other._states.Length)
            {
                int cmp = _comparer.Compare(_states[i], other._states[j]);
                if (cmp < 0) i++;
                else if (cmp > 0) j++;
                else { result.Add(_states[i++]); j++; }
            }
            return new StateSet<TState>(result.ToArray(), _comparer);
        }

        /// <summary>Returns this set minus the other set.</summary>
        public StateSet<TState> Except(StateSet<TState> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var result = new List<TState>();
            int j = 0;
            for (int i = 0; i < _states.Length; i++)
            {
                while (j < other._states.Length && _comparer.Compare(other._states[j], _states[i]) < 0)
                    j++;
                if (j >= other._states.Length || _comparer.Compare(other._states[j], _states[i]) != 0)
                    result.Add(_states[i]);
            }
            return new StateSet<TState>(result.ToArray(), _comparer);
        }

        /// <summary>Returns true if this set is a subset of the other.</summary>
        public bool IsSubsetOf(StateSet<TState> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (_states.Length > other._states.Length) return false;
            int j = 0;
            for (int i = 0; i < _states.Length; i++)
            {
                while (j < other._states.Length && _comparer.Compare(other._states[j], _states[i]) < 0)
                    j++;
                if (j >= other._states.Length || _comparer.Compare(other._states[j], _states[i]) != 0)
                    return false;
                j++;
            }
            return true;
        }

        /// <summary>Returns true if this set is a proper subset of the other.</summary>
        public bool IsProperSubsetOf(StateSet<TState> other)
            => other != null && _states.Length < other._states.Length && IsSubsetOf(other);

        #region Equality and Comparison

        /// <summary>
        /// Structural equality: two sets are equal iff they contain the same
        /// elements in the same order (per the comparer).
        /// </summary>
        public bool Equals(StateSet<TState> other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (_states.Length != other._states.Length) return false;
            for (int i = 0; i < _states.Length; i++)
                if (_comparer.Compare(_states[i], other._states[i]) != 0)
                    return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as StateSet<TState>);

        public override int GetHashCode()
        {
            if (_hash == null)
            {
                unchecked
                {
                    int h = 17;
                    foreach (var s in _states)
                        h = h * 31 + (s != null ? s.GetHashCode() : 0);
                    _hash = h;
                }
            }
            return _hash.Value;
        }

        /// <summary>
        /// Lexicographic comparison: shorter sets first, then element-wise.
        /// Used for canonical ordering in <see cref="Dnf{TState}"/>.
        /// </summary>
        public int CompareTo(StateSet<TState> other)
        {
            if (other == null) return 1;
            int lenCmp = _states.Length.CompareTo(other._states.Length);
            if (lenCmp != 0) return lenCmp;
            for (int i = 0; i < _states.Length; i++)
            {
                int cmp = _comparer.Compare(_states[i], other._states[i]);
                if (cmp != 0) return cmp;
            }
            return 0;
        }

        public static bool operator ==(StateSet<TState> left, StateSet<TState> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(StateSet<TState> left, StateSet<TState> right)
            => !(left == right);

        #endregion

        #region IReadOnlyCollection

        public IEnumerator<TState> GetEnumerator()
            => ((IEnumerable<TState>)_states).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _states.GetEnumerator();

        #endregion

        public override string ToString()
            => "{" + string.Join(", ", _states) + "}";
    }
}
