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
    /// Integration tests showing that the weak-equivalence breakpoint merge
    /// from <see cref="RltlBreakpointCanonicalizer{TPred,TElem}"/> fires under
    /// the production <see cref="StatePropEba"/> now that its
    /// <see cref="StatePropEba.IsSatisfiable"/> is propositionally precise
    /// (todo <c>statepred-precise-sat</c>): the JACM Example 5.1 reduction
    /// of <c>G(Fa ∧ F¬a)</c> reaches its 3-state minimum, and a real
    /// model-program check delivers the correct verdict with the merge on.
    /// </summary>
    [TestFixture]
    public class JacmExample51EndToEndTests
    {
        private sealed class TestState : State
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

        private static StateGraphNode Node(TestState s)
        {
            s.Freeze();
            return new StateGraphNode
            {
                State = s,
                StepFunctions = new List<IStepFunction> { new TestStep("step") },
                Edges = new List<StateGraphEdge>()
            };
        }

        private static void Edge(StateGraphNode from, StateGraphNode to)
            => from.Edges.Add(new StateGraphEdge { Target = to, StepFunction = new TestStep("step") });

        /// <summary>
        /// End-to-end model checking: the model alternates a / ¬a forever,
        /// so it satisfies <c>G(Fa ∧ F¬a)</c>. Run the check with the weak-
        /// equivalence merge enabled; the production <see cref="StatePropEba"/>
        /// (precise on the propositional fragment) must give a correct
        /// "valid" verdict.
        /// </summary>
        [Test]
        public void GFaFNa_AlternatingModel_ValidUnderMerge()
        {
            // Two-state cycle: s0 (a) ↔ s1 (¬a).
            var s0 = Node(new TestState("s0", a: true));
            var s1 = Node(new TestState("s1", a: false));
            Edge(s0, s1);
            Edge(s1, s0);

            var aProp = new StateProp("a", st => ((TestState)st).A);
            var alg = RltlAlgebra.Default;
            var formula = alg.Globally(
                alg.And(
                    alg.Eventually(Rltl<IStatePredicate>.Atom(new StatePredAtom(aProp))),
                    alg.Eventually(alg.NegAtom(new StatePredAtom(aProp)))));

            var withMerge = SymbolicRltlCheck.Check(
                s0, formula, mergeWeakEquivalent: true);
            var withoutMerge = SymbolicRltlCheck.Check(
                s0, formula);

            Assert.That(withMerge.Valid, Is.True, "G(Fa∧F¬a) holds on alternating model.");
            Assert.That(withoutMerge.Valid, Is.True, "Same verdict without merge.");
        }

        /// <summary>
        /// End-to-end counterexample: a model that eventually stops emitting
        /// <c>a</c> (transitions to a permanent <c>¬a</c> loop) violates
        /// <c>G(Fa ∧ F¬a)</c>. The check must report invalid with a
        /// counterexample, both with and without the merge.
        /// </summary>
        [Test]
        public void GFaFNa_EventuallyStuckModel_InvalidUnderMerge()
        {
            // s0 (a) → s1 (¬a) → s1 forever  ⇒  Fa is satisfied (a at s0)
            // but G F a is violated (no a after the first step).
            var s0 = Node(new TestState("s0", a: true));
            var s1 = Node(new TestState("s1", a: false));
            Edge(s0, s1);
            Edge(s1, s1);

            var aProp = new StateProp("a", st => ((TestState)st).A);
            var alg = RltlAlgebra.Default;
            var formula = alg.Globally(
                alg.And(
                    alg.Eventually(Rltl<IStatePredicate>.Atom(new StatePredAtom(aProp))),
                    alg.Eventually(alg.NegAtom(new StatePredAtom(aProp)))));

            var withMerge = SymbolicRltlCheck.Check(
                s0, formula, mergeWeakEquivalent: true);
            var withoutMerge = SymbolicRltlCheck.Check(
                s0, formula);

            Assert.That(withMerge.Valid, Is.False);
            Assert.That(withoutMerge.Valid, Is.False);
            Assert.That(withMerge.Trace, Is.Not.Null);
        }

        /// <summary>
        /// Direct state-count probe of the production pipeline: build the
        /// RLTL → ABW → IncrementalAE NBW for <c>G(Fa ∧ F¬a)</c> under
        /// <see cref="StatePropEba"/> with all canonicalisers (ERE, RLTL,
        /// and the weak-equivalent breakpoint merger) enabled. The merge
        /// must collapse the reachable BP states to exactly 3 (JACM Ex. 5.1).
        /// </summary>
        [Test]
        public void GFaFNa_ProductionEba_MergesTo3()
        {
            var eba = StatePropEba.Instance;
            var aProp = new StateProp("a", st => true);
            var atomA = new StatePredAtom(aProp);

            var registry = new ConditionRegistry<IStatePredicate>(
                EqualityComparer<IStatePredicate>.Default);
            var ed = new EreDerivative<IStatePredicate, State>(eba, registry);
            var ereCanon = new EreCanonicalizer<IStatePredicate, State>(
                new EreEquivalenceChecker<IStatePredicate, State>(ed));
            var ralg = new RltlAlgebra<IStatePredicate>(eba, ereCanon);
            var rltlCanon = new RltlCanonicalizer<IStatePredicate, State>(eba, ralg);
            var deriv = new RltlDerivative<IStatePredicate, State>(
                eba, registry, ereCanon, rltlCanon);

            var formula = ralg.Globally(
                ralg.And(
                    ralg.Eventually(Rltl<IStatePredicate>.Atom(atomA)),
                    ralg.Eventually(ralg.NegAtom(atomA))));

            var abw = deriv.ToABW(formula);
            var merger = new RltlBreakpointCanonicalizer<IStatePredicate, State>(eba, ralg);
            var ae = new IncrementalAE<IStatePredicate, State, Rltl<IStatePredicate>>(
                abw, merger.Canonicalize);
            var nbw = ae.ToNBW();

            var seen = new HashSet<BreakpointState<Rltl<IStatePredicate>>>(
                BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer());
            var queue = new Queue<BreakpointState<Rltl<IStatePredicate>>>(nbw.InitialStates);
            foreach (var s in nbw.InitialStates) seen.Add(s);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                foreach (var term in nbw.GetTransition(s))
                    foreach (var leaf in term.GetDistinctLeaves())
                        foreach (var succ in leaf)
                            if (seen.Add(succ)) queue.Enqueue(succ);
            }

            TestContext.WriteLine(
                $"G(Fa ∧ F¬a) with production StatePropEba + full canonicaliser stack: {seen.Count} BP state(s).");
            foreach (var s in seen)
            {
                var macro = string.Join(",", s.Macrostate.Select(f => f.ToString()));
                var oblig = string.Join(",", s.Obligation.Select(f => f.ToString()));
                TestContext.WriteLine($"  S={{{macro}}}  O={{{oblig}}}  accepting={s.Obligation.IsEmpty}");
            }

            Assert.That(seen.Count, Is.EqualTo(3),
                "Production EBA + precise IsSatisfiable + weak-equivalent BP merge must yield the JACM Ex. 5.1 minimum (3 states).");
        }
    }
}
