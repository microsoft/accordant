using Microsoft.Accordant;

namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Bdd;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// End-to-end tests for the macrostate transition-merge knob
    /// (<c>SymbolicRltlCheck.Check(..., subsumeMacrostate: true)</c>),
    /// which now wires <see cref="RltlMacrostateTransitionMerge{TPred,TElem}"/>.
    ///
    /// <para>
    /// The rule collapses universal copies with identical transition
    /// terms and matching colour. On <c>GFa ∧ GF(¬a)</c> (JACM
    /// Example 5.1) this fires; on <c>GFa ∧ GFb ∧ GFc</c> it does not
    /// (the three F-states have distinct deltas).
    /// </para>
    /// </summary>
    [TestFixture]
    public class MacrostateTransitionMergeEndToEndTests
    {
        private static readonly BddStatePropEba Eba = BddStatePropEba.Instance;
        private static readonly StateProp A = new StateProp("a", _ => true);
        private static readonly StateProp B = new StateProp("b", _ => true);
        private static readonly StateProp C = new StateProp("c", _ => true);

        private static Rltl<IStatePredicate> GFaGFbGFc()
        {
            var alg = new RltlAlgebra<IStatePredicate>(Eba);
            var Fa  = alg.Eventually(alg.Atom(new StatePredAtom(A)));
            var Fb  = alg.Eventually(alg.Atom(new StatePredAtom(B)));
            var Fc  = alg.Eventually(alg.Atom(new StatePredAtom(C)));
            return alg.And(alg.Globally(Fa), alg.And(alg.Globally(Fb), alg.Globally(Fc)));
        }

        private static Rltl<IStatePredicate> GFa_And_GFnota()
        {
            var alg = new RltlAlgebra<IStatePredicate>(Eba);
            var a    = Rltl<IStatePredicate>.Atom(new StatePredAtom(A));
            var nota = Rltl<IStatePredicate>.Atom(Eba.Not(new StatePredAtom(A)));
            return alg.And(alg.Globally(alg.Eventually(a)),
                           alg.Globally(alg.Eventually(nota)));
        }

        // Build the breakpoint NBW for φ directly (no negation) with the
        // requested knobs and report the number of reachable BP states.
        private static int CountReachableBp(
            Rltl<IStatePredicate> phi, bool subsume, int hardCap)
        {
            var registry = new ConditionRegistry<IStatePredicate>(
                EqualityComparer<IStatePredicate>.Default);
            var deriv = new RltlDerivative<IStatePredicate, State>(Eba, registry, null, null);
            var abw = deriv.ToABW(phi);

            Func<StateSet<Rltl<IStatePredicate>>, MacroReduction<Rltl<IStatePredicate>>> reducer = null;
            if (subsume)
                reducer = new RltlMacrostateTransitionMerge<IStatePredicate, State>(abw.GetTransition).Reduce;

            var ae = new IncrementalAE<IStatePredicate, State, Rltl<IStatePredicate>>(
                abw, breakpointCanonicalizer: null, macroReducer: reducer);
            var nbw = ae.ToNBW();

            var cmp = BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer();
            var seen = new HashSet<BreakpointState<Rltl<IStatePredicate>>>(cmp);
            var queue = new Queue<BreakpointState<Rltl<IStatePredicate>>>();
            foreach (var s in nbw.InitialStates)
                if (seen.Add(s)) queue.Enqueue(s);
            while (queue.Count > 0 && seen.Count < hardCap)
            {
                var s = queue.Dequeue();
                foreach (var tt in nbw.GetTransition(s))
                    foreach (var leaf in tt.GetDistinctLeaves())
                        foreach (var succ in leaf)
                            if (seen.Add(succ)) queue.Enqueue(succ);
            }
            return seen.Count;
        }

        // Returns (anyNonEmptyObligation, reachableCount) — the soundness
        // probe: if every reachable BP has O = ∅ then the NBW is universal,
        // which would indicate an unsound reduction.
        private static (bool anyObligation, int count) WalkBp(
            Rltl<IStatePredicate> phi, bool subsume, int hardCap)
        {
            var registry = new ConditionRegistry<IStatePredicate>(
                EqualityComparer<IStatePredicate>.Default);
            var deriv = new RltlDerivative<IStatePredicate, State>(Eba, registry, null, null);
            var abw = deriv.ToABW(phi);
            Func<StateSet<Rltl<IStatePredicate>>, MacroReduction<Rltl<IStatePredicate>>> reducer = null;
            if (subsume)
                reducer = new RltlMacrostateTransitionMerge<IStatePredicate, State>(abw.GetTransition).Reduce;
            var ae = new IncrementalAE<IStatePredicate, State, Rltl<IStatePredicate>>(abw, null, reducer);
            var nbw = ae.ToNBW();
            var cmp = BreakpointState<Rltl<IStatePredicate>>.GetEqualityComparer();
            var seen = new HashSet<BreakpointState<Rltl<IStatePredicate>>>(cmp);
            var queue = new Queue<BreakpointState<Rltl<IStatePredicate>>>();
            foreach (var s in nbw.InitialStates)
                if (seen.Add(s)) queue.Enqueue(s);
            bool any = false;
            while (queue.Count > 0 && seen.Count < hardCap)
            {
                var s = queue.Dequeue();
                if (!s.Obligation.IsEmpty) any = true;
                foreach (var tt in nbw.GetTransition(s))
                    foreach (var leaf in tt.GetDistinctLeaves())
                        foreach (var succ in leaf)
                            if (seen.Add(succ)) queue.Enqueue(succ);
            }
            return (any, seen.Count);
        }

        // --------------- GFa ∧ GF(¬a) — JACM Example 5.1 ---------------

        [Test]
        public void TransitionMerge_OnGFaAndGFnota_IsSound()
        {
            var (any, n) = WalkBp(GFa_And_GFnota(), subsume: true, hardCap: 200);
            TestContext.Out.WriteLine($"GFa ∧ GF(¬a):  BPs={n}  anyNonEmptyObligation={any}");
            Assert.That(any, Is.True,
                "Under the structural transition-merge rule at least one " +
                "reachable BP must carry a non-empty obligation.");
        }

        [Test]
        public void TransitionMerge_OnGFaAndGFnota_DoesNotGrowState()
        {
            var off = CountReachableBp(GFa_And_GFnota(), subsume: false, hardCap: 200);
            var on  = CountReachableBp(GFa_And_GFnota(), subsume: true,  hardCap: 200);
            TestContext.Out.WriteLine($"GFa ∧ GF(¬a):  off={off}  merge={on}");
            Assert.That(on, Is.LessThanOrEqualTo(off));
        }

        // --------------- GFa ∧ GFb ∧ GFc (no merge fires) ---------------

        [Test]
        public void TransitionMerge_OnGFaGFbGFc_IsSound_NoChange()
        {
            var off = CountReachableBp(GFaGFbGFc(), subsume: false, hardCap: 200);
            var on  = CountReachableBp(GFaGFbGFc(), subsume: true,  hardCap: 200);
            TestContext.Out.WriteLine($"GFa ∧ GFb ∧ GFc:  off={off}  merge={on}");
            // The three F-states have distinct deltas → no collapse → equal counts.
            Assert.That(on, Is.EqualTo(off));

            var (any, _) = WalkBp(GFaGFbGFc(), subsume: true, hardCap: 200);
            Assert.That(any, Is.True, "NBW must not be universal.");
        }

        // --------------- Differential corpus: merge never grows state ---------------

        private static Rltl<IStatePredicate>[] DifferentialCorpus()
        {
            var alg = new RltlAlgebra<IStatePredicate>(Eba);
            var a = alg.Atom(new StatePredAtom(A));
            var b = alg.Atom(new StatePredAtom(B));
            var Fa = alg.Eventually(a);
            var Ga = alg.Globally(a);
            return new[]
            {
                a,
                Fa,
                Ga,
                alg.And(Fa, alg.Eventually(b)),
                alg.Or(Ga, alg.Globally(b)),
                alg.Globally(Fa),
                alg.And(alg.Globally(Fa), alg.Globally(alg.Eventually(b))),
                alg.Until(a, b),
                alg.Release(a, b),
                alg.Next(alg.And(a, b)),
            };
        }

        [Test, TestCaseSource(nameof(DifferentialCorpus))]
        public void TransitionMerge_DoesNotEnlargeBpCount(Rltl<IStatePredicate> phi)
        {
            int off = CountReachableBp(phi, subsume: false, hardCap: 500);
            int on  = CountReachableBp(phi, subsume: true,  hardCap: 500);
            Assert.That(on, Is.LessThanOrEqualTo(off),
                $"Merge must not enlarge the BP count for φ={phi}");
        }
    }
}
