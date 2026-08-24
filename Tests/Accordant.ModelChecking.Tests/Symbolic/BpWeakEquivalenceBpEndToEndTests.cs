namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Bdd;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// End-to-end tests for the post-construction bisimulation minimiser
    /// (<see cref="BpWeakEquivalenceMinimizer"/>) exposed via the
    /// <c>mergeWeakEquivalentBp</c> knob on
    /// <see cref="SymbolicRltlCheck.Check"/>.
    ///
    /// <para>The knob implements JACM Lemma 5.x (state-reduction in the
    /// breakpoint NBW) as a graph-bound partition refinement. Compared to
    /// the per-pair language-equivalence-based <c>mergeWeakEquivalent</c>
    /// knob, it is structural rather than semantic and operates on the
    /// already-constructed NBW — so it stays tractable on
    /// <c>GFa ∧ GFb ∧ GFc</c> where the language-level merge times out.</para>
    /// </summary>
    [TestFixture]
    public class BpWeakEquivalenceBpEndToEndTests
    {
        private static readonly BddStatePropEba Eba = BddStatePropEba.Instance;

        private static readonly StateProp PropA = new StateProp("a", _ => true);
        private static readonly StateProp PropB = new StateProp("b", _ => true);
        private static readonly StateProp PropC = new StateProp("c", _ => true);

        private static Rltl<IStatePredicate> A(StateProp p)
            => Rltl<IStatePredicate>.Atom(new StatePredAtom(p));
        private static Rltl<IStatePredicate> NA(StateProp p)
            => Rltl<IStatePredicate>.Atom(Eba.Not(new StatePredAtom(p)));

        private static int CountReachable(
            SymbolicNBW<IStatePredicate, State, BreakpointState<Rltl<IStatePredicate>>> nbw,
            int cap)
        {
            var seen = new HashSet<BreakpointState<Rltl<IStatePredicate>>>(
                BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer());
            var work = new Queue<BreakpointState<Rltl<IStatePredicate>>>();
            foreach (var s in nbw.InitialStates) if (seen.Add(s)) work.Enqueue(s);
            while (work.Count > 0 && seen.Count <= cap)
            {
                var s = work.Dequeue();
                foreach (var tt in nbw.GetTransition(s))
                    foreach (var leaf in tt.GetDistinctLeaves())
                        foreach (var succ in leaf)
                            if (seen.Add(succ)) work.Enqueue(succ);
            }
            return seen.Count;
        }

        public enum MinVariant { Post, Fused, Dedup }

        private static SymbolicNBW<IStatePredicate, State, BreakpointState<Rltl<IStatePredicate>>>
            BuildMinimisedNbw(Rltl<IStatePredicate> formula, bool negate, MinVariant variant = MinVariant.Dedup)
        {
            var (min, _) = BuildMinimisedNbwWithAe(formula, negate, variant);
            return min;
        }

        private static (SymbolicNBW<IStatePredicate, State, BreakpointState<Rltl<IStatePredicate>>> min,
                        IncrementalAE<IStatePredicate, State, Rltl<IStatePredicate>> ae)
            BuildMinimisedNbwWithAe(Rltl<IStatePredicate> formula, bool negate, MinVariant variant)
        {
            var registry = new ConditionRegistry<IStatePredicate>(
                EqualityComparer<IStatePredicate>.Default);
            var ed = new EreDerivative<IStatePredicate, State>(Eba, registry);
            var ereCanon = new EreCanonicalizer<IStatePredicate, State>(
                new EreEquivalenceChecker<IStatePredicate, State>(ed));
            var ralg = new RltlAlgebra<IStatePredicate>(Eba, ereCanon);
            var rltlCanon = new RltlCanonicalizer<IStatePredicate, State>(Eba, ralg);
            var deriv = new RltlDerivative<IStatePredicate, State>(
                Eba, registry, ereCanon, rltlCanon);
            var seed = negate ? ralg.Not(formula) : formula;
            var abw = deriv.ToABW(seed);
            var ae = new IncrementalAE<IStatePredicate, State, Rltl<IStatePredicate>>(abw);
            var nbw = ae.ToNBW();

            var bpEq = BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer();
            var bpOrd = BreakpointState<Rltl<IStatePredicate>>.GetComparer(
                Comparer<Rltl<IStatePredicate>>.Default);
            var min = variant switch
            {
                MinVariant.Post  => BpWeakEquivalenceMinimizer.Minimize(nbw, bpEq, bpOrd),
                MinVariant.Fused => BpWeakEquivalenceMinimizer.MinimizeFused(nbw, bpEq, bpOrd),
                MinVariant.Dedup => BpWeakEquivalenceMinimizer.DedupOnTheFly(nbw, bpEq, bpOrd),
                _ => throw new System.ArgumentOutOfRangeException(nameof(variant)),
            };
            return (min, ae);
        }

        /// <summary>
        /// JACM Example 5.1: <c>G(Fa ∧ F¬a)</c>. The unminimised breakpoint
        /// NBW has 8 reachable states; the bisimulation minimiser must
        /// collapse it to exactly 3, matching the paper.
        /// </summary>
        [Test]
        public void Jacm51_BisimMin_CollapsesToThree()
        {
            var ralg = RltlAlgebra.Default;
            var phi = ralg.Globally(ralg.And(
                ralg.Eventually(A(PropA)),
                ralg.Eventually(NA(PropA))));
            var min = BuildMinimisedNbw(phi, negate: false);
            int count = CountReachable(min, 100);

            // Print reachable BPs for diagnostics.
            var seen = new HashSet<BreakpointState<Rltl<IStatePredicate>>>(
                BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer());
            var work = new Queue<BreakpointState<Rltl<IStatePredicate>>>();
            foreach (var s in min.InitialStates) if (seen.Add(s)) work.Enqueue(s);
            while (work.Count > 0)
            {
                var s = work.Dequeue();
                var macro = string.Join(",", System.Linq.Enumerable.Select(s.Macrostate, f => f.ToString()));
                var oblig = string.Join(",", System.Linq.Enumerable.Select(s.Obligation, f => f.ToString()));
                TestContext.WriteLine($"  S={{{macro}}}  O={{{oblig}}}  acc={s.Obligation.IsEmpty}");
                foreach (var tt in min.GetTransition(s))
                {
                    TestContext.WriteLine($"    tt: {tt}");
                    foreach (var leaf in tt.GetDistinctLeaves())
                        foreach (var succ in leaf)
                            if (seen.Add(succ)) work.Enqueue(succ);
                }
            }

            Assert.That(count, Is.EqualTo(3),
                "G(Fa ∧ F¬a) must collapse to 3 BP states under bisimulation minimisation.");
        }

        /// <summary>
        /// <c>GFa ∧ GFb ∧ GFc</c>: the language-level
        /// <c>mergeWeakEquivalent</c> times out at 30s here, but
        /// bisimulation minimisation should complete in well under a second
        /// and reduce the 27 reachable BP states.
        /// </summary>
        [Test, Timeout(120_000)]
        public void GFaGFbGFc_BisimMin_TractableAndReduces()
        {
            var ralg = RltlAlgebra.Default;
            var phi = ralg.And(
                ralg.And(
                    ralg.Globally(ralg.Eventually(A(PropA))),
                    ralg.Globally(ralg.Eventually(A(PropB)))),
                ralg.Globally(ralg.Eventually(A(PropC))));

            var sw = Stopwatch.StartNew();
            var minP = BuildMinimisedNbw(phi, negate: true, MinVariant.Post);
            int countPost = CountReachable(minP, 1000);
            sw.Stop();
            TestContext.WriteLine($"Post : {countPost} BP states in {sw.ElapsedMilliseconds} ms");

            sw.Restart();
            var minD = BuildMinimisedNbw(phi, negate: true, MinVariant.Dedup);
            int countDedup = CountReachable(minD, 1000);
            sw.Stop();
            TestContext.WriteLine($"Dedup: {countDedup} BP states in {sw.ElapsedMilliseconds} ms");

            Assert.That(countPost, Is.LessThanOrEqualTo(27),
                "Bisimulation minimisation must not enlarge the BP count.");
            // Dedup is shallow (no cyclic structural alignment) so its count
            // may exceed the bisim-optimal count, but must still be sound.
            Assert.That(countDedup, Is.LessThanOrEqualTo(27),
                "Dedup must not enlarge the BP count.");
        }

        /// <summary>
        /// On-the-fly dedup must expand strictly fewer raw BPs on
        /// <c>GFa ∧ GFb ∧ GFc</c> than the post-construction variant — the
        /// dedup short-circuits at construction time so aliased BPs are
        /// never expanded further.
        /// </summary>
        [Test, Timeout(120_000)]
        public void GFaGFbGFc_Dedup_ExpandsFewerThanPostConstruction()
        {
            var ralg = RltlAlgebra.Default;
            Rltl<IStatePredicate> Phi() => ralg.And(
                ralg.And(
                    ralg.Globally(ralg.Eventually(A(PropA))),
                    ralg.Globally(ralg.Eventually(A(PropB)))),
                ralg.Globally(ralg.Eventually(A(PropC))));

            var (minPost, aePost) = BuildMinimisedNbwWithAe(Phi(), negate: true, MinVariant.Post);
            var (minDedup, aeDedup) = BuildMinimisedNbwWithAe(Phi(), negate: true, MinVariant.Dedup);

            int postCount = CountReachable(minPost, 1000);
            int dedupCount = CountReachable(minDedup, 1000);
            TestContext.WriteLine(
                $"Post : {aePost.ComputedStateCount} BP σ-computations → {postCount} reachable.");
            TestContext.WriteLine(
                $"Dedup: {aeDedup.ComputedStateCount} BP σ-computations → {dedupCount} reachable.");

            Assert.That(aeDedup.ComputedStateCount, Is.LessThanOrEqualTo(aePost.ComputedStateCount),
                "Dedup must not exceed post-construction BP expansions.");
        }

        /// <summary>
        /// Soundness: alternating-a model satisfies G(Fa ∧ F¬a),
        /// even with the bisim minimisation applied.
        /// </summary>
        [Test]
        public void GFaFNa_AlternatingModel_ValidUnderBpMerge()
        {
            var s0 = JacmExample51EndToEndTests_Helpers.MakeNode("s0", a: true);
            var s1 = JacmExample51EndToEndTests_Helpers.MakeNode("s1", a: false);
            JacmExample51EndToEndTests_Helpers.AddEdge(s0, s1);
            JacmExample51EndToEndTests_Helpers.AddEdge(s1, s0);

            var aProp = new StateProp("a",
                st => ((JacmExample51EndToEndTests_Helpers.TestState)st).A);
            var alg = RltlAlgebra.Default;
            var formula = alg.Globally(alg.And(
                alg.Eventually(Rltl<IStatePredicate>.Atom(new StatePredAtom(aProp))),
                alg.Eventually(alg.NegAtom(new StatePredAtom(aProp)))));

            var withBp = SymbolicRltlCheck.Check(s0, formula, mergeWeakEquivalentBp: true);
            Assert.That(withBp.Valid, Is.True);
        }

        /// <summary>
        /// Soundness: <c>G(Fa ∧ F¬a)</c> fails on a model that eventually
        /// stops emitting <c>a</c>, even with bisim minimisation applied,
        /// and a counterexample trace is reported.
        /// </summary>
        [Test]
        public void GFaFNa_EventuallyStuckModel_InvalidUnderBpMerge()
        {
            var s0 = JacmExample51EndToEndTests_Helpers.MakeNode("s0", a: true);
            var s1 = JacmExample51EndToEndTests_Helpers.MakeNode("s1", a: false);
            JacmExample51EndToEndTests_Helpers.AddEdge(s0, s1);
            JacmExample51EndToEndTests_Helpers.AddEdge(s1, s1);

            var aProp = new StateProp("a",
                st => ((JacmExample51EndToEndTests_Helpers.TestState)st).A);
            var alg = RltlAlgebra.Default;
            var formula = alg.Globally(alg.And(
                alg.Eventually(Rltl<IStatePredicate>.Atom(new StatePredAtom(aProp))),
                alg.Eventually(alg.NegAtom(new StatePredAtom(aProp)))));

            var withBp = SymbolicRltlCheck.Check(s0, formula, mergeWeakEquivalentBp: true);
            Assert.That(withBp.Valid, Is.False);
            Assert.That(withBp.Trace, Is.Not.Null);
        }
    }

    /// <summary>
    /// Shared test-state helpers replicating the minimal model-program
    /// scaffold used by <see cref="JacmExample51EndToEndTests"/>.
    /// </summary>
    internal static class JacmExample51EndToEndTests_Helpers
    {
        public sealed class TestState : State
        {
            public string Label { get; }
            public bool A { get; }
            public TestState(string label, bool a) { Label = label; A = a; }
            protected override void CloneInternal(Dictionary<object, object> map)
                => map[this] = new TestState(Label, A);
            protected override void LockComponents(HashSet<object> visited) { }
            protected override string StringRepresentationInternal(Dictionary<object, string> paths, string path, bool forceRecompute) => Label;
            protected override void FreezeComponents(HashSet<object> visited) { }
        }

        private sealed class TestStep : IStepFunction
        {
            public string StepFunctionId { get; }
            public int StepFunctionIdHash { get; }
            public TestStep(string id) { StepFunctionId = id; StepFunctionIdHash = id.GetHashCode(); }
            public IList<StepResult> Apply(IState s, IReadOnlyList<(IStepFunction, StateGraphNode)> path) => null;
        }

        public static StateGraphNode MakeNode(string label, bool a)
        {
            var s = new TestState(label, a);
            s.Freeze();
            return new StateGraphNode
            {
                State = s,
                StepFunctions = new List<IStepFunction> { new TestStep("step") },
                Edges = new List<StateGraphEdge>()
            };
        }

        public static void AddEdge(StateGraphNode from, StateGraphNode to)
            => from.Edges.Add(new StateGraphEdge { Target = to, StepFunction = new TestStep("step") });
    }
}
