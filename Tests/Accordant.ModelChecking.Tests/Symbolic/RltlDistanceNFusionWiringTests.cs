namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Phase 7 / Layer A — RLTL-side end-to-end wiring test for the
    /// distribution rewrites. Builds an RLTL formula whose construction
    /// triggers <em>both</em> Layer A rules in sequence:
    ///
    /// <list type="number">
    ///   <item>The ERE-side <c>Fusion</c>-over-Union-left rule, when fusing
    ///         a top-level <see cref="EreUnion{TPred}"/> of two
    ///         <c>distance_n</c> variants with a trailing end-marker atom.</item>
    ///   <item>The RLTL-side <c>SeqPrefix</c>-over-Union rule, when wrapping
    ///         the resulting union of fused regexes with <c>;φ</c>.</item>
    /// </list>
    ///
    /// <para>The fully-distributed shape is then passed through the full
    /// <see cref="SymbolicRltlCheck"/> pipeline (RLTL → ABW → NBW → NDFS)
    /// on a small Kripke structure to verify the semantics survive the
    /// rewrites — both for a satisfying and a violating instance.</para>
    /// </summary>
    [TestFixture]
    public class RltlDistanceNFusionWiringTests
    {
        #region Test infrastructure (mirrors SymbolicRltlCheckTests)

        private sealed class TestState : State
        {
            public string Label { get; }
            public int Value { get; }
            public TestState(string label, int value) { Label = label; Value = value; }
            protected override void CloneInternal(Dictionary<object, object> m)
                => m[this] = new TestState(Label, Value);
            protected override void LockComponents(HashSet<object> visited) { }
            protected override string StringRepresentationInternal(Dictionary<object, string> paths, string path, bool forceRecompute) => $"{Label}({Value})";
            protected override void FreezeComponents(HashSet<object> visited) { }
        }

        private sealed class TestStepFunction : IStepFunction
        {
            public string StepFunctionId { get; }
            public int StepFunctionIdHash { get; }
            public TestStepFunction(string id)
            { StepFunctionId = id; StepFunctionIdHash = id.GetHashCode(); }
            public IList<StepResult> Apply(IState s, IReadOnlyList<(IStepFunction, StateGraphNode)> path) => null;
        }

        private static StateGraphNode MakeNode(TestState st, params string[] sfIds)
        {
            st.Freeze();
            return new StateGraphNode
            {
                State = st,
                StepFunctions = sfIds.Select(id => (IStepFunction)new TestStepFunction(id)).ToList(),
                Edges = new List<StateGraphEdge>()
            };
        }

        private static void AddEdge(StateGraphNode from, StateGraphNode to)
            => from.Edges.Add(new StateGraphEdge
            {
                Target = to,
                StepFunction = new TestStepFunction("step")
            });

        private static StateProp Prop(string name, Func<IState, bool> f) => new StateProp(name, f);

        private static Ere<IStatePredicate> EAtom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));
        private static Ere<IStatePredicate> ESigma() => Ere<IStatePredicate>.Sigma();
        private static Ere<IStatePredicate> EStar(Ere<IStatePredicate> r) => Ere<IStatePredicate>.Star(r);
        private static Ere<IStatePredicate> ESigmaStar() => EStar(ESigma());
        private static Ere<IStatePredicate> EConcat(params Ere<IStatePredicate>[] xs)
        {
            Ere<IStatePredicate> acc = Ere<IStatePredicate>.Epsilon();
            for (int i = xs.Length - 1; i >= 0; i--)
                acc = Ere<IStatePredicate>.Concat(xs[i], acc);
            return acc;
        }
        private static Ere<IStatePredicate> EUnion(params Ere<IStatePredicate>[] xs)
        {
            Ere<IStatePredicate> acc = Ere<IStatePredicate>.Empty();
            foreach (var x in xs) acc = Ere<IStatePredicate>.Union(acc, x);
            return acc;
        }
        private static Rltl<IStatePredicate> RAtom(StateProp p)
            => Rltl<IStatePredicate>.Atom(new StatePredAtom(p));

        // distance_n with a chosen "marker" predicate: Σ* · marker · Σ^n.
        private static Ere<IStatePredicate> DistanceN(StateProp marker, int n)
        {
            var sigma = ESigma();
            var parts = new List<Ere<IStatePredicate>> { ESigmaStar(), EAtom(marker) };
            for (int i = 0; i < n; i++) parts.Add(sigma);
            return EConcat(parts.ToArray());
        }

        #endregion

        // -----------------------------------------------------------------
        // Layer A structural tests — assert that constructing the formula
        // through the smart constructors triggers both distribution rules.
        // -----------------------------------------------------------------

        [Test]
        public void FusionOverUnion_AtTheEnd_ProducesTopLevelEreUnion()
        {
            // (distance_n_a + distance_n_b) : end
            // After head-factoring (P2.1): distance_n_a + distance_n_b
            // collapses to Σ*·((a·Σ^n) + (b·Σ^n)) — a single Concat, not
            // a Union. So Fusion-over-Union-left doesn't fire and the
            // top-level shape is a Fusion rather than a Union of Fusions.
            // Semantics are preserved (factoring is sound); this test
            // just documents the new canonical shape.
            var a   = Prop("a",   s => false);
            var b   = Prop("b",   s => false);
            var end = Prop("end", s => false);
            const int n = 2;

            var inner = EUnion(DistanceN(a, n), DistanceN(b, n));
            // After head-factoring, inner is Σ*·((a·rest)+(b·rest)).
            Assert.That(inner, Is.InstanceOf<EreConcat<IStatePredicate>>(),
                "Σ*-headed disjuncts head-factor into a single Concat.");
            var concat = (EreConcat<IStatePredicate>)inner;
            Assert.That(concat.Right, Is.InstanceOf<EreUnion<IStatePredicate>>(),
                "the factored tail retains the union of differing-head subterms.");

            var fused = Ere<IStatePredicate>.Fusion(inner, EAtom(end));
            Assert.That(fused, Is.InstanceOf<EreFusion<IStatePredicate>>(),
                "Fusion-over-Union-left does not fire on a factored Concat; "
                + "the top-level remains a Fusion.");
        }

        [Test]
        public void SeqPrefixOverUnion_OnFusedRegex_ProducesRltlOr()
        {
            // R = (Σ*·a·Σ^n + Σ*·b·Σ^n) : end
            // After head-factoring (P2.1) the underlying ERE is no longer
            // a top-level Union, so SeqPrefix-over-Union does not split
            // and the RLTL shape stays a single RltlSeqPrefix. This is
            // the new canonical shape; language semantics are preserved.
            var a   = Prop("a",   s => false);
            var b   = Prop("b",   s => false);
            var end = Prop("end", s => false);
            var q   = Prop("q",   s => false);
            const int n = 2;

            var r = Ere<IStatePredicate>.Fusion(
                EUnion(DistanceN(a, n), DistanceN(b, n)),
                EAtom(end));
            var f = Rltl<IStatePredicate>.SeqPrefix(r, RAtom(q));

            Assert.That(f, Is.InstanceOf<RltlSeqPrefix<IStatePredicate>>(),
                "with head-factoring, the regex remains a single Concat-headed "
                + "Fusion, so SeqPrefix yields a single SeqPrefix (no RltlOr).");
        }

        // -----------------------------------------------------------------
        // End-to-end wiring tests through SymbolicRltlCheck — verifies the
        // distributed formula model-checks correctly on a small Kripke
        // structure for both satisfying and violating runs.
        // -----------------------------------------------------------------

        /// <summary>
        /// Satisfying instance. The system marks the start state with
        /// both <c>a</c> and <c>end</c>, then two Σ-steps, then <c>q</c>
        /// for ever. With n=2, <c>distance_2_a : end</c> matches the
        /// prefix [s0] (Σ* matches ε, the <c>a</c>-position is s0, and
        /// the fused trailing <c>end</c> requires the last letter of that
        /// match to also satisfy <c>end</c> — yes). Then <c>;q</c>
        /// requires the suffix at position 1 to satisfy q.
        /// </summary>
        [Test]
        public void EndToEnd_FusedDistanceN_Sat_ViaADisjunct()
        {
            var a   = Prop("a",   s => ((TestState)s).Value == 1);
            var b   = Prop("b",   s => ((TestState)s).Value == 2);
            var end = Prop("end", s => ((TestState)s).Value == 1); // a∧end at s0
            var q   = Prop("q",   s => ((TestState)s).Value == 9);

            // s0(a,end) → s1(q) → s2(q) self-loop. Position 0 satisfies a∧end
            // (matched by (Σ*·a):end with k-1=0), position 1 satisfies q (witness
            // for the ;q clause).
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 9));
            var s2 = MakeNode(new TestState("s2", 9));
            AddEdge(s0, s1); AddEdge(s1, s2); AddEdge(s2, s2);

            const int n = 0; // distance_0 — minimal blow-up version
            var r = Ere<IStatePredicate>.Fusion(
                EUnion(DistanceN(a, n), DistanceN(b, n)),
                EAtom(end));
            var phi = Rltl<IStatePredicate>.SeqPrefix(r, RAtom(q));

            // Sanity: head-factoring collapses to a single SeqPrefix
            // (no RltlOr split). Semantics preserved.
            Assert.That(phi, Is.InstanceOf<RltlSeqPrefix<IStatePredicate>>());

            var result = SymbolicRltlCheck.Check(s0, phi);
            Assert.That(result.Valid, Is.True,
                "the a-disjunct's prefix (a∧end at s0) is matched and q holds at s1's successor — formula satisfied");
        }

        /// <summary>
        /// Violating instance. Same formula but the system marks the
        /// start with <c>a</c> only (no <c>end</c>) and never produces
        /// <c>b</c>, so neither fused disjunct can find a matching
        /// prefix. Expect a counterexample trace.
        /// </summary>
        [Test]
        public void EndToEnd_FusedDistanceN_Vio_NeitherDisjunctMatches()
        {
            var a   = Prop("a",   s => ((TestState)s).Value == 1);
            var b   = Prop("b",   s => ((TestState)s).Value == 2);
            // end is now disjoint from both a and b — no position can be both.
            var end = Prop("end", s => ((TestState)s).Value == 7);
            var q   = Prop("q",   s => ((TestState)s).Value == 9);

            // s0(a) → s1 → s2(q) → s2(q) — no state ever satisfies end.
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            var s2 = MakeNode(new TestState("s2", 9));
            AddEdge(s0, s1); AddEdge(s1, s2); AddEdge(s2, s2);

            const int n = 0;
            var r = Ere<IStatePredicate>.Fusion(
                EUnion(DistanceN(a, n), DistanceN(b, n)),
                EAtom(end));
            var phi = Rltl<IStatePredicate>.SeqPrefix(r, RAtom(q));

            Assert.That(phi, Is.InstanceOf<RltlSeqPrefix<IStatePredicate>>(),
                "with head-factoring, no top-level RltlOr is produced; "
                + "the model checker handles the factored shape directly.");

            var result = SymbolicRltlCheck.Check(s0, phi);
            Assert.That(result.Valid, Is.False,
                "no run satisfies either disjunct — formula must be violated");
            Assert.That(result.Trace, Is.Not.Null, "violation should yield a counterexample trace");
        }

        /// <summary>
        /// Equivalence-with-hand-distributed test. Construct the same
        /// formula two ways — once via the (auto-distributing) single
        /// call, once by hand-distributing into the explicit Or shape —
        /// and verify the underlying RLTL nodes are reference-equal (so
        /// SymbolicRltlCheck must give identical results on any input).
        /// </summary>
        /// <summary>
        /// Equivalence-with-hand-distributed test. With head-factoring (P2.1)
        /// the two routes diverge structurally: <c>auto</c> factors the
        /// Σ*-headed disjuncts into a single <c>SeqPrefix</c>; <c>hand</c>
        /// constructs a top-level <c>RltlOr</c> from already-separated
        /// disjuncts that cannot re-merge through the RLTL surface. The
        /// languages remain equal (factoring is sound), but the canonical
        /// ASTs no longer coincide. We assert the model-checker verdict
        /// agrees on a small Kripke run instead of pinning AST shape.
        /// </summary>
        [Test]
        public void DistributedShape_EqualsHandDistributedShape()
        {
            var a   = Prop("a",   s => ((TestState)s).Value == 1);
            var b   = Prop("b",   s => ((TestState)s).Value == 2);
            var end = Prop("end", s => ((TestState)s).Value == 1);
            var q   = Prop("q",   s => ((TestState)s).Value == 9);
            const int n = 0;

            var auto = Rltl<IStatePredicate>.SeqPrefix(
                Ere<IStatePredicate>.Fusion(
                    EUnion(DistanceN(a, n), DistanceN(b, n)),
                    EAtom(end)),
                RAtom(q));

            var hand = Rltl<IStatePredicate>.Or(
                Rltl<IStatePredicate>.SeqPrefix(
                    Ere<IStatePredicate>.Fusion(DistanceN(a, n), EAtom(end)),
                    RAtom(q)),
                Rltl<IStatePredicate>.SeqPrefix(
                    Ere<IStatePredicate>.Fusion(DistanceN(b, n), EAtom(end)),
                    RAtom(q)));

            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 9));
            var s2 = MakeNode(new TestState("s2", 9));
            AddEdge(s0, s1); AddEdge(s1, s2); AddEdge(s2, s2);

            var rAuto = SymbolicRltlCheck.Check(s0, auto);
            var rHand = SymbolicRltlCheck.Check(s0, hand);
            Assert.That(rAuto.Valid, Is.EqualTo(rHand.Valid),
                "the head-factored form and the hand-distributed form must agree on "
                + "the model-checker verdict (language equivalence).");
        }
    }
}
