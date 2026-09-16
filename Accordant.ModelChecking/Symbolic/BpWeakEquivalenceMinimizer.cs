namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Bisimulation-style minimiser implementing the JACM state-reduction
    /// lemma (Lemma 5.x, used in the Example 5.1 walk-through) on the
    /// breakpoint-NBW produced by <see cref="IncrementalAE{TPred,TElem,TState}"/>.
    ///
    /// <para>Two NBW states <c>s, s'</c> are weakly equivalent iff
    /// <c>IsAccepting(s) = IsAccepting(s')</c> AND, modulo the equivalence,
    /// their transition terms <c>σ(s), σ(s')</c> are structurally equal.
    /// For breakpoint states, the acceptance condition <c>O=∅</c> coincides
    /// with the paper's "U=∅ ⟺ U'=∅" colour split, so a single
    /// IsAccepting-based initial partition suffices.</para>
    ///
    /// <para>This is the standard NBW partition-refinement (Hopcroft-style
    /// without the splitter optimisation): cost is
    /// <c>O(|S|² · |TT|)</c> in the worst case but typically near-linear
    /// on the breakpoint graphs we see. Crucially it operates on the
    /// already-constructed NBW graph, so it does not invoke per-pair NBW
    /// emptiness checks the way the language-equivalence-based
    /// <see cref="RltlBreakpointCanonicalizer{TPred,TElem}"/> does — making
    /// it tractable on inputs like <c>GFa ∧ GFb ∧ GFc</c> where the
    /// language-level merger times out.</para>
    /// </summary>
    public static class BpWeakEquivalenceMinimizer
    {
        /// <summary>
        /// Builds an eager NBW whose states are equivalence classes of
        /// the input NBW under structural bisimulation respecting
        /// acceptance. Each class is represented by an arbitrary member.
        /// </summary>
        public static SymbolicNBW<TPred, TElem, TState> Minimize<TPred, TElem, TState>(
            SymbolicNBW<TPred, TElem, TState> nbw,
            IEqualityComparer<TState> eqCmp,
            IComparer<TState> ordCmp,
            int hardCap = 100_000)
        {
            if (nbw == null) throw new ArgumentNullException(nameof(nbw));
            if (eqCmp == null) throw new ArgumentNullException(nameof(eqCmp));
            if (ordCmp == null) throw new ArgumentNullException(nameof(ordCmp));

            // 1. Force lazy expansion: discover all reachable states.
            var reachable = new List<TState>();
            var seen = new HashSet<TState>(eqCmp);
            var work = new Queue<TState>();
            foreach (var s in nbw.InitialStates)
                if (seen.Add(s)) { reachable.Add(s); work.Enqueue(s); }
            while (work.Count > 0)
            {
                if (reachable.Count > hardCap) return nbw;  // bail out
                var s = work.Dequeue();
                foreach (var tt in nbw.GetTransition(s))
                    foreach (var leaf in tt.GetDistinctLeaves())
                        foreach (var succ in leaf)
                            if (seen.Add(succ)) { reachable.Add(succ); work.Enqueue(succ); }
            }

            // 2. Initial partition by acceptance.
            var classOf = new Dictionary<TState, int>(eqCmp);
            foreach (var s in reachable)
                classOf[s] = nbw.IsAccepting(s) ? 1 : 0;

            // Per-state cached transitions (avoid recomputation per refine
            // iteration; transitions don't change as classes refine).
            var trans = new Dictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>>(eqCmp);
            foreach (var s in reachable)
                trans[s] = nbw.GetTransition(s);

            // 3. Partition refinement to fixpoint.
            bool changed = true;
            while (changed)
            {
                changed = false;
                int nextId = 0;
                foreach (var c in classOf.Values) if (c >= nextId) nextId = c + 1;

                var groups = new Dictionary<int, List<TState>>();
                foreach (var s in reachable)
                {
                    if (!groups.TryGetValue(classOf[s], out var g))
                        groups[classOf[s]] = g = new List<TState>();
                    g.Add(s);
                }

                var nextClassOf = new Dictionary<TState, int>(eqCmp);
                foreach (var kv in groups)
                {
                    var members = kv.Value;
                    if (members.Count == 1) { nextClassOf[members[0]] = kv.Key; continue; }

                    // Sub-group by mapped-transition signature.
                    var bySig = new Dictionary<TransitionListSignature, List<TState>>();
                    foreach (var s in members)
                    {
                        var sig = SignatureOf(trans[s], classOf);
                        if (!bySig.TryGetValue(sig, out var lst))
                            bySig[sig] = lst = new List<TState>();
                        lst.Add(s);
                    }

                    if (bySig.Count == 1)
                    {
                        foreach (var s in members) nextClassOf[s] = kv.Key;
                    }
                    else
                    {
                        changed = true;
                        bool first = true;
                        foreach (var subKv in bySig)
                        {
                            int id = first ? kv.Key : nextId++;
                            first = false;
                            foreach (var s in subKv.Value) nextClassOf[s] = id;
                        }
                    }
                }
                classOf = nextClassOf;
            }

            // 4. Build the quotient.
            var repOf = new Dictionary<int, TState>();
            foreach (var s in reachable)
                if (!repOf.ContainsKey(classOf[s])) repOf[classOf[s]] = s;

            // Comparer used by the existing StateSets so we can rebuild
            // rep-mapped leaves with the same identity.
            IComparer<TState> leafCmp = ordCmp;
            foreach (var s in reachable)
            {
                foreach (var tt in trans[s])
                {
                    foreach (var leaf in tt.GetDistinctLeaves())
                    {
                        if (leaf != null) { leafCmp = leaf.Comparer; goto found; }
                    }
                }
            }
            found:

            var newInitials = new List<TState>();
            var initialSeen = new HashSet<TState>(eqCmp);
            foreach (var s in nbw.InitialStates)
            {
                var rep = repOf[classOf[s]];
                if (initialSeen.Add(rep)) newInitials.Add(rep);
            }

            var newTransitions = new Dictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>>(eqCmp);
            foreach (var rep in repOf.Values)
            {
                var src = trans[rep];
                var mapped = new List<TransitionTerm<StateSet<TState>>>(src.Count);
                foreach (var tt in src)
                    mapped.Add(MapLeaves(tt, classOf, repOf, leafCmp));
                newTransitions[rep] = mapped;
            }

            return new SymbolicNBW<TPred, TElem, TState>(
                nbw.Eba, nbw.Registry, newInitials, nbw.IsAccepting, newTransitions, eqCmp);
        }

        /// <summary>
        /// Fused construction + bisimulation minimisation: only expand
        /// canonical class representatives, with online partition
        /// refinement after every expansion. This implements the JACM
        /// AElim algorithm with the state-reduction lemma applied
        /// in-place (Section 5), avoiding the introduction of breakpoint
        /// states that would later be eliminated by post-construction
        /// minimisation.
        ///
        /// <para>Algorithm sketch:</para>
        /// <list type="number">
        ///   <item>Each discovered BP is initially placed in a coarse
        ///         "colour class" (accepting / non-accepting).</item>
        ///   <item>The current partition is recomputed by hashing
        ///         <c>(colour, sig)</c> where <c>sig</c> is the
        ///         class-id-rewritten transition list (only defined for
        ///         BPs whose σ has been computed). Iterate to fixpoint.</item>
        ///   <item>Pick an unexplored BP that is the first member of its
        ///         class (canonical rep). Compute its σ; discover its
        ///         successors; mark explored.</item>
        ///   <item>Repeat until no unexplored reps remain.</item>
        /// </list>
        ///
        /// <para>An unexplored BP that is bisim-equivalent to an already
        /// explored rep is never itself expanded — its σ is taken to be
        /// the rep's σ. This is the desired "fusion": the algorithm pays
        /// for at most one σ-computation per equivalence class.</para>
        /// </summary>
        public static SymbolicNBW<TPred, TElem, TState> MinimizeFused<TPred, TElem, TState>(
            SymbolicNBW<TPred, TElem, TState> nbw,
            IEqualityComparer<TState> eqCmp,
            IComparer<TState> ordCmp,
            int hardCap = 100_000)
        {
            if (nbw == null) throw new ArgumentNullException(nameof(nbw));
            if (eqCmp == null) throw new ArgumentNullException(nameof(eqCmp));
            if (ordCmp == null) throw new ArgumentNullException(nameof(ordCmp));

            // All discovered BPs, in discovery order (first member of a
            // class in this order is its canonical rep).
            var discovered = new List<TState>();
            var discoveredSet = new HashSet<TState>(eqCmp);
            // Currently assigned class id per BP.
            var classOf = new Dictionary<TState, int>(eqCmp);
            // σ of every BP whose transitions have been computed.
            var trans = new Dictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>>(eqCmp);
            var explored = new HashSet<TState>(eqCmp);

            // Colour classes 0 / 1 are reserved for not-yet-explored BPs
            // (non-accepting / accepting). Explored BPs receive fresh
            // class IDs ≥ 2 from RecomputePartition.
            void Discover(TState s)
            {
                if (!discoveredSet.Add(s)) return;
                discovered.Add(s);
                // Unexplored BPs sit in colour class -1 (non-accepting) or
                // -2 (accepting); these IDs cannot collide with the
                // discovery-index-based IDs assigned by RecomputePartition
                // to explored BPs (which are ≥ 2).
                classOf[s] = nbw.IsAccepting(s) ? -2 : -1;
            }

            foreach (var s in nbw.InitialStates) Discover(s);

            int safetyIterations = 0;
            int safetyCap = (hardCap + 16) * 16;
            while (true)
            {
                if (discovered.Count > hardCap) return nbw;
                if (++safetyIterations > safetyCap)
                    throw new InvalidOperationException(
                        "BpWeakEquivalenceMinimizer.MinimizeFused did not converge.");

                // (1) Re-partition to local fixpoint.
                bool partitionChanged;
                do { partitionChanged = RecomputePartition(discovered, trans, explored, classOf, nbw); }
                while (partitionChanged);

                // (2) Pick the first unexplored canonical rep, if any.
                TState toExpand = default;
                bool found = false;
                var seenClass = new HashSet<int>();
                foreach (var bp in discovered)
                {
                    int cid = classOf[bp];
                    if (!seenClass.Add(cid)) continue;          // bp not rep of its class
                    if (explored.Contains(bp)) continue;        // rep already explored
                    toExpand = bp; found = true; break;
                }
                if (!found) break;

                // (3) Expand it: compute σ, discover successors.
                var σ = nbw.GetTransition(toExpand);
                trans[toExpand] = σ;
                explored.Add(toExpand);
                foreach (var tt in σ)
                    foreach (var leaf in tt.GetDistinctLeaves())
                        foreach (var succ in leaf)
                            Discover(succ);
            }

            // Build the quotient. Reps = first-encountered per class.
            var repOf = new Dictionary<int, TState>();
            foreach (var s in discovered)
                if (!repOf.ContainsKey(classOf[s])) repOf[classOf[s]] = s;

            // Recover the leaf comparer used by the original transitions.
            IComparer<TState> leafCmp = ordCmp;
            foreach (var s in discovered)
            {
                if (!trans.TryGetValue(s, out var σ)) continue;
                foreach (var tt in σ)
                    foreach (var leaf in tt.GetDistinctLeaves())
                        if (leaf != null) { leafCmp = leaf.Comparer; goto found; }
            }
            found:

            var newInitials = new List<TState>();
            var initialSeen = new HashSet<TState>(eqCmp);
            foreach (var s in nbw.InitialStates)
            {
                var rep = repOf[classOf[s]];
                if (initialSeen.Add(rep)) newInitials.Add(rep);
            }

            var newTransitions = new Dictionary<TState,
                IReadOnlyList<TransitionTerm<StateSet<TState>>>>(eqCmp);
            foreach (var rep in repOf.Values)
            {
                if (!trans.TryGetValue(rep, out var src))
                {
                    // Should not happen: every rep is explored before loop exit.
                    src = nbw.GetTransition(rep);
                }
                var mapped = new List<TransitionTerm<StateSet<TState>>>(src.Count);
                foreach (var tt in src)
                    mapped.Add(MapLeaves(tt, classOf, repOf, leafCmp));
                newTransitions[rep] = mapped;
            }

            return new SymbolicNBW<TPred, TElem, TState>(
                nbw.Eba, nbw.Registry, newInitials, nbw.IsAccepting, newTransitions, eqCmp);
        }

        /// <summary>
        /// Recompute <paramref name="classOf"/> based on <c>(colour, sig)</c>
        /// hash-consing of explored states. Unexplored states stay in
        /// their colour class. Class IDs are the discovery-index of the
        /// canonical representative — they are stable across calls as long
        /// as the partition itself does not change, so change-detection is
        /// reliable. Returns true iff any class assignment changed.
        /// </summary>
        private static bool RecomputePartition<TPred, TElem, TState>(
            List<TState> discovered,
            Dictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>> trans,
            HashSet<TState> explored,
            Dictionary<TState, int> classOf,
            SymbolicNBW<TPred, TElem, TState> nbw)
        {
            var sigIndex = new Dictionary<(int colour, TransitionListSignature sig), int>();
            // Snapshot: compute everything using the OLD classOf, commit at the end.
            var newClassOf = new int[discovered.Count];
            for (int i = 0; i < discovered.Count; i++)
            {
                var bp = discovered[i];
                int colour = nbw.IsAccepting(bp) ? 1 : 0;
                int newCid;
                if (explored.Contains(bp))
                {
                    var sig = SignatureOf(trans[bp], classOf);
                    var key = (colour, sig);
                    if (!sigIndex.TryGetValue(key, out newCid))
                    {
                        // Stable ID: discovery index of the first (canonical) member.
                        // Shifted by +2 to leave room for the two reserved
                        // unexplored colour IDs (-1, -2).
                        newCid = i + 2;
                        sigIndex[key] = newCid;
                    }
                }
                else
                {
                    // Unexplored: stay in colour class. Negative IDs keep
                    // them disjoint from any explored class index.
                    newCid = -1 - colour;
                }
                newClassOf[i] = newCid;
            }

            bool changed = false;
            for (int i = 0; i < discovered.Count; i++)
            {
                var bp = discovered[i];
                if (classOf[bp] != newClassOf[i])
                {
                    classOf[bp] = newClassOf[i];
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// A canonical (hashable) signature of a transition list under the
        /// current class map. Two states with equal signatures and equal
        /// initial colour are bisimulation-equivalent at the current iterate.
        /// </summary>
        private readonly struct TransitionListSignature : IEquatable<TransitionListSignature>
        {
            private readonly TransitionTerm<StateSet<int>>[] _terms;
            private readonly int _hash;

            public TransitionListSignature(TransitionTerm<StateSet<int>>[] sortedTerms)
            {
                _terms = sortedTerms;
                int h = 17;
                foreach (var t in sortedTerms) h = unchecked(h * 31 + t.GetHashCode());
                _hash = h;
            }

            public bool Equals(TransitionListSignature other)
            {
                if (_terms.Length != other._terms.Length) return false;
                for (int i = 0; i < _terms.Length; i++)
                    if (!_terms[i].Equals(other._terms[i])) return false;
                return true;
            }
            public override bool Equals(object obj)
                => obj is TransitionListSignature s && Equals(s);
            public override int GetHashCode() => _hash;
        }

        /// <summary>
        /// Lightweight on-the-fly leaf dedup. NOT a minimisation: just checks
        /// whether a newly discovered state has a transition term structurally
        /// identical (modulo current canonicalisation of successors) to some
        /// already-canonical state of the same colour (accepting/non-accepting).
        /// If so, the new state aliases to the existing one. Otherwise it
        /// becomes a fresh canonical state.
        ///
        /// <para>No partition fixpoint, no per-pair language checks. The
        /// algorithm visits each successor exactly once (lazy DFS) and pays
        /// at most one signature comparison per discovered state. It will
        /// miss equivalences that require cyclic structural alignment, but
        /// catches all tree-shaped duplications.</para>
        /// </summary>
        public static SymbolicNBW<TPred, TElem, TState> DedupOnTheFly<TPred, TElem, TState>(
            SymbolicNBW<TPred, TElem, TState> nbw,
            IEqualityComparer<TState> eqCmp,
            IComparer<TState> ordCmp)
        {
            if (nbw == null) throw new ArgumentNullException(nameof(nbw));
            if (eqCmp == null) throw new ArgumentNullException(nameof(eqCmp));
            if (ordCmp == null) throw new ArgumentNullException(nameof(ordCmp));

            var canonical = new Dictionary<TState, TState>(eqCmp);
            var transOfCanon = new Dictionary<TState, IReadOnlyList<TransitionTerm<StateSet<TState>>>>(eqCmp);
            var indexOf = new Dictionary<TState, int>(eqCmp);
            var sigIndex = new Dictionary<(bool acc, TransitionListSignature sig), TState>();
            int nextIndex = 0;

            TState Canonicalize(TState s)
            {
                if (canonical.TryGetValue(s, out var existingCanon))
                    return existingCanon;

                // Tentative: claim s as its own canonical with a fresh index.
                int myIdx = nextIndex++;
                indexOf[s] = myIdx;
                canonical[s] = s;

                var raw = nbw.GetTransition(s);

                // Recurse into successor leaves, canonicalising each state.
                var rewritten = new List<TransitionTerm<StateSet<TState>>>(raw.Count);
                foreach (var tt in raw)
                    rewritten.Add(MapLeavesCanon(tt, Canonicalize, ordCmp));

                // Build signature over int indices of canonical successors.
                var sig = SignatureOfDirect(rewritten, indexOf);
                bool acc = nbw.IsAccepting(s);
                var key = (acc, sig);

                if (sigIndex.TryGetValue(key, out var existing) && !eqCmp.Equals(existing, s))
                {
                    // s collapses to the pre-existing canonical state.
                    canonical[s] = existing;
                    indexOf.Remove(s);
                    return existing;
                }

                sigIndex[key] = s;
                transOfCanon[s] = rewritten;
                return s;
            }

            var initial = new List<TState>();
            var initialSeen = new HashSet<TState>(eqCmp);
            foreach (var s in nbw.InitialStates)
            {
                var c = Canonicalize(s);
                if (initialSeen.Add(c)) initial.Add(c);
            }

            return new SymbolicNBW<TPred, TElem, TState>(
                nbw.Eba, nbw.Registry, initial,
                s => nbw.IsAccepting(Canonicalize(s)),
                s => transOfCanon[Canonicalize(s)],
                eqCmp);
        }

        private static TransitionTerm<StateSet<TState>> MapLeavesCanon<TState>(
            TransitionTerm<StateSet<TState>> tt,
            Func<TState, TState> canonicalize,
            IComparer<TState> ordCmp)
        {
            if (tt is TransitionTermLeaf<StateSet<TState>> leaf)
            {
                if (leaf.Value.IsEmpty) return tt;
                var unique = new HashSet<TState>(EqualityComparer<TState>.Default);
                var sorted = new List<TState>();
                foreach (var s in leaf.Value)
                {
                    var c = canonicalize(s);
                    if (unique.Add(c)) sorted.Add(c);
                }
                sorted.Sort(ordCmp);
                return TransitionTerm<StateSet<TState>>.Leaf(
                    new StateSet<TState>(sorted, ordCmp));
            }
            var ite = (TransitionTermIte<StateSet<TState>>)tt;
            var hi = MapLeavesCanon(ite.Hi, canonicalize, ordCmp);
            var lo = MapLeavesCanon(ite.Lo, canonicalize, ordCmp);
            return TransitionTerm<StateSet<TState>>.Ite(ite.ConditionIndex, hi, lo);
        }

        private static TransitionListSignature SignatureOfDirect<TState>(
            IReadOnlyList<TransitionTerm<StateSet<TState>>> terms,
            Dictionary<TState, int> indexOf)
        {
            var mapped = new TransitionTerm<StateSet<int>>[terms.Count];
            for (int i = 0; i < terms.Count; i++)
                mapped[i] = MapToClasses(terms[i], indexOf);
            Array.Sort(mapped, (a, b) =>
            {
                int hc = a.GetHashCode().CompareTo(b.GetHashCode());
                if (hc != 0) return hc;
                return a.Equals(b) ? 0 : string.CompareOrdinal(a.ToString(), b.ToString());
            });
            return new TransitionListSignature(mapped);
        }

        private static readonly IComparer<int> IntComparer = Comparer<int>.Default;

        private static TransitionListSignature SignatureOf<TState>(
            IReadOnlyList<TransitionTerm<StateSet<TState>>> terms,
            Dictionary<TState, int> classOf)
        {
            var mapped = new TransitionTerm<StateSet<int>>[terms.Count];
            for (int i = 0; i < terms.Count; i++)
                mapped[i] = MapToClasses(terms[i], classOf);
            // Sort by structural hash so list order doesn't matter.
            Array.Sort(mapped, (a, b) =>
            {
                int hc = a.GetHashCode().CompareTo(b.GetHashCode());
                if (hc != 0) return hc;
                return a.Equals(b) ? 0 : string.CompareOrdinal(a.ToString(), b.ToString());
            });
            return new TransitionListSignature(mapped);
        }

        private static TransitionTerm<StateSet<int>> MapToClasses<TState>(
            TransitionTerm<StateSet<TState>> tt,
            Dictionary<TState, int> classOf)
        {
            if (tt is TransitionTermLeaf<StateSet<TState>> leaf)
            {
                var ids = new SortedSet<int>();
                foreach (var s in leaf.Value) ids.Add(classOf[s]);
                var arr = new int[ids.Count];
                ids.CopyTo(arr);
                return TransitionTerm<StateSet<int>>.Leaf(
                    new StateSet<int>(arr, IntComparer));
            }
            var ite = (TransitionTermIte<StateSet<TState>>)tt;
            var hi = MapToClasses(ite.Hi, classOf);
            var lo = MapToClasses(ite.Lo, classOf);
            return TransitionTerm<StateSet<int>>.Ite(ite.ConditionIndex, hi, lo);
        }

        private static TransitionTerm<StateSet<TState>> MapLeaves<TState>(
            TransitionTerm<StateSet<TState>> tt,
            Dictionary<TState, int> classOf,
            Dictionary<int, TState> repOf,
            IComparer<TState> leafCmp)
        {
            if (tt is TransitionTermLeaf<StateSet<TState>> leaf)
            {
                if (leaf.Value.IsEmpty)
                    return TransitionTerm<StateSet<TState>>.Leaf(leaf.Value);
                var reps = new HashSet<TState>(EqualityComparer<TState>.Default);
                // Use the leaf's own comparer (assumed consistent with eqCmp
                // identity from the same algebra) via a sorted set instead.
                var sorted = new List<TState>();
                var seen = new HashSet<int>();
                foreach (var s in leaf.Value)
                {
                    int cid = classOf[s];
                    if (seen.Add(cid)) sorted.Add(repOf[cid]);
                }
                sorted.Sort(leafCmp);
                return TransitionTerm<StateSet<TState>>.Leaf(
                    new StateSet<TState>(sorted, leafCmp));
            }
            var ite = (TransitionTermIte<StateSet<TState>>)tt;
            var hi = MapLeaves(ite.Hi, classOf, repOf, leafCmp);
            var lo = MapLeaves(ite.Lo, classOf, repOf, leafCmp);
            return TransitionTerm<StateSet<TState>>.Ite(ite.ConditionIndex, hi, lo);
        }
    }
}
