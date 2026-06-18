namespace Microsoft.Accordant.ModelChecking
{
    using System;

    /// <summary>
    /// Flat-array disjoint-set (union-find) data structure over non-negative
    /// integer keys, with path compression and union-by-rank.
    /// </summary>
    /// <remarks>
    /// <para>The store grows on demand (doubling) so callers may use any
    /// non-negative <see cref="int"/> as a key without pre-sizing. Until a
    /// key is referenced it is implicitly a singleton class representing
    /// itself; <see cref="Find(int)"/> materializes the slot lazily.</para>
    ///
    /// <para>This is a domain-agnostic utility — designed for hash-consed
    /// term Ids (e.g., <c>Ere&lt;TPred&gt;.Id</c>) but usable for any
    /// integer keying. See <c>EreEquivalenceChecker</c> for the
    /// bisimulation use case described in
    /// <c>C:\git\ere\cav26\paper.tex</c>, §4 and §6.</para>
    ///
    /// <para><b>Not thread-safe.</b></para>
    /// </remarks>
    public sealed class IntUnionFind
    {
        private int[] _parent;
        private byte[] _rank;
        private int _capacity;

        /// <summary>
        /// Creates an empty union-find with the given initial capacity
        /// (number of pre-allocated slots; the structure grows on demand).
        /// </summary>
        public IntUnionFind(int initialCapacity = 64)
        {
            if (initialCapacity < 1) initialCapacity = 1;
            _parent = new int[initialCapacity];
            _rank = new byte[initialCapacity];
            _capacity = initialCapacity;
            for (int i = 0; i < _capacity; i++) _parent[i] = -1;
        }

        /// <summary>
        /// Returns the representative of <paramref name="x"/>'s class,
        /// materializing the singleton class {x} if x has not been
        /// referenced before.
        /// </summary>
        public int Find(int x)
        {
            if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
            EnsureCapacity(x);
            if (_parent[x] == -1)
            {
                _parent[x] = x;
                return x;
            }
            // Path compression via two-pass to avoid recursion stack growth.
            int root = x;
            while (_parent[root] != root) root = _parent[root];
            int cur = x;
            while (_parent[cur] != root)
            {
                int next = _parent[cur];
                _parent[cur] = root;
                cur = next;
            }
            return root;
        }

        /// <summary>
        /// Returns true iff <paramref name="x"/> and <paramref name="y"/>
        /// are in the same class. Materializes singletons for unseen keys.
        /// </summary>
        public bool InSameClass(int x, int y) => Find(x) == Find(y);

        /// <summary>
        /// Unions the classes of <paramref name="x"/> and <paramref name="y"/>.
        /// Returns <c>false</c> if they were already in the same class (no
        /// change), <c>true</c> if a union was performed.
        /// </summary>
        public bool Union(int x, int y)
        {
            int rx = Find(x);
            int ry = Find(y);
            if (rx == ry) return false;
            int rkx = _rank[rx];
            int rky = _rank[ry];
            if (rkx < rky)
            {
                _parent[rx] = ry;
            }
            else if (rkx > rky)
            {
                _parent[ry] = rx;
            }
            else
            {
                _parent[ry] = rx;
                _rank[rx] = (byte)(rkx + 1);
            }
            return true;
        }

        /// <summary>
        /// Returns true iff <paramref name="x"/> has ever been referenced
        /// (i.e., its slot is materialized). Unreferenced keys are
        /// implicit singletons.
        /// </summary>
        public bool Contains(int x)
        {
            if (x < 0 || x >= _capacity) return false;
            return _parent[x] != -1;
        }

        private void EnsureCapacity(int index)
        {
            if (index < _capacity) return;
            int newCap = _capacity;
            while (newCap <= index) newCap *= 2;
            Array.Resize(ref _parent, newCap);
            Array.Resize(ref _rank, newCap);
            for (int i = _capacity; i < newCap; i++) _parent[i] = -1;
            _capacity = newCap;
        }
    }
}
