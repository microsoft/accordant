namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    [TestFixture]
    public class RltlTests
    {
        private sealed class Prop : IEquatable<Prop>, IComparable<Prop>
        {
            public string Name { get; }
            public Prop(string name) { Name = name; }
            public override string ToString() => Name;
            public override int GetHashCode() => Name.GetHashCode();
            public override bool Equals(object obj) => Equals(obj as Prop);
            public bool Equals(Prop other) => other != null && Name == other.Name;
            public int CompareTo(Prop other) => string.Compare(Name, other?.Name, StringComparison.Ordinal);
        }

        private sealed class PropEba : IEffectiveBooleanAlgebra<Prop, HashSet<string>>
        {
            public Prop Top => new Prop("⊤");
            public Prop Bottom => new Prop("⊥");
            public Prop And(Prop a, Prop b)
            {
                if (a.Name == "⊤") return b;
                if (b.Name == "⊤") return a;
                if (a.Name == "⊥" || b.Name == "⊥") return Bottom;
                if (a.Equals(b)) return a;
                return new Prop($"({a.Name}∧{b.Name})");
            }
            public Prop Or(Prop a, Prop b)
            {
                if (a.Name == "⊥") return b;
                if (b.Name == "⊥") return a;
                if (a.Name == "⊤" || b.Name == "⊤") return Top;
                if (a.Equals(b)) return a;
                return new Prop($"({a.Name}∨{b.Name})");
            }
            public Prop Not(Prop a)
            {
                if (a.Name == "⊤") return Bottom;
                if (a.Name == "⊥") return Top;
                if (a.Name.StartsWith("¬")) return new Prop(a.Name.Substring(1));
                return new Prop($"¬{a.Name}");
            }
            public bool IsSatisfiable(Prop p) => p.Name != "⊥";
            public bool Models(HashSet<string> e, Prop p)
            {
                if (p.Name == "⊤") return true;
                if (p.Name == "⊥") return false;
                if (p.Name.StartsWith("¬")) return !e.Contains(p.Name.Substring(1));
                return e.Contains(p.Name);
            }
        }

        private static readonly Prop A = new Prop("a");
        private static readonly Prop B = new Prop("b");
        private static readonly RltlAlgebra<Prop> Alg = new RltlAlgebra<Prop>(new PropEba());

        // ---------- Negation / NNF ----------

        [Test]
        public void Negate_SeqPrefix_YieldsTrigger()
        {
            var r = Ere<Prop>.Atom(A);
            var phi = Rltl<Prop>.Atom(B);
            var seq = Rltl<Prop>.SeqPrefix(r, phi);
            var neg = Alg.Not(seq);
            Assert.That(neg, Is.InstanceOf<RltlTrigger<Prop>>());
            var t = (RltlTrigger<Prop>)neg;
            Assert.That(t.Regex, Is.EqualTo(r));
            Assert.That(t.Phi, Is.EqualTo(Alg.Not(phi)));
        }

        [Test]
        public void Negate_OvlPrefix_YieldsMatch()
        {
            var r = Ere<Prop>.Atom(A);
            var phi = Rltl<Prop>.Atom(B);
            var ovl = Rltl<Prop>.OvlPrefix(r, phi);
            var neg = Alg.Not(ovl);
            Assert.That(neg, Is.InstanceOf<RltlMatch<Prop>>());
        }

        [Test]
        public void Negate_DoubleNegation_IsIdentity()
        {
            var r = Ere<Prop>.Concat(Ere<Prop>.Atom(A), Ere<Prop>.Star(Ere<Prop>.Atom(B)));
            var phi = Rltl<Prop>.Globally(Rltl<Prop>.Atom(A));
            var f = Rltl<Prop>.SeqPrefix(r, phi);
            Assert.That(Alg.Not(Alg.Not(f)), Is.EqualTo(f));
        }

        // ---------- Factory simplifications ----------

        [Test]
        public void SeqPrefix_EpsilonRegex_ReducesToPhi()
        {
            var phi = Rltl<Prop>.Atom(A);
            var f = Rltl<Prop>.SeqPrefix(Ere<Prop>.Epsilon(), phi);
            Assert.That(f, Is.EqualTo(phi));
        }

        [Test]
        public void SeqPrefix_EmptyRegex_ReducesToFalse()
        {
            var phi = Rltl<Prop>.Atom(A);
            var f = Rltl<Prop>.SeqPrefix(Ere<Prop>.Empty(), phi);
            Assert.That(f, Is.InstanceOf<RltlFalse<Prop>>());
        }

        [Test]
        public void OvlPrefix_EpsilonRegex_ReducesToFalse()
        {
            // : requires positive-length match, ε has none
            var phi = Rltl<Prop>.Atom(A);
            var f = Rltl<Prop>.OvlPrefix(Ere<Prop>.Epsilon(), phi);
            Assert.That(f, Is.InstanceOf<RltlFalse<Prop>>());
        }

        [Test]
        public void Trigger_EmptyRegex_ReducesToTrue()
        {
            var phi = Rltl<Prop>.Atom(A);
            var f = Rltl<Prop>.Trigger(Ere<Prop>.Empty(), phi);
            Assert.That(f, Is.InstanceOf<RltlTrue<Prop>>());
        }

        [Test]
        public void Until_RightFalse_ReducesToFalse()
        {
            // l U ⊥ ≡ ⊥
            var l = Rltl<Prop>.Atom(A);
            var f = Rltl<Prop>.Until(l, Rltl<Prop>.False());
            Assert.That(f, Is.InstanceOf<RltlFalse<Prop>>());
        }

        [Test]
        public void SeqPrefix_SigmaStarRegex_ReducesToEventually()
        {
            // Σ* ; φ ≡ ◇φ = ⊤ U φ
            var phi = Rltl<Prop>.Atom(A);
            var sigmaStar = Ere<Prop>.Star(Ere<Prop>.Sigma());
            var f = Rltl<Prop>.SeqPrefix(sigmaStar, phi);
            Assert.That(f, Is.EqualTo(Rltl<Prop>.Eventually(phi)));
        }

        [Test]
        public void OvlPrefix_SigmaStarRegex_ReducesToEventually()
        {
            var phi = Rltl<Prop>.Atom(A);
            var sigmaStar = Ere<Prop>.Star(Ere<Prop>.Sigma());
            var f = Rltl<Prop>.OvlPrefix(sigmaStar, phi);
            Assert.That(f, Is.EqualTo(Rltl<Prop>.Eventually(phi)));
        }

        [Test]
        public void Trigger_SigmaStarRegex_ReducesToGlobally()
        {
            // Σ* ⊳ φ ≡ □φ = ⊥ R φ
            var phi = Rltl<Prop>.Atom(A);
            var sigmaStar = Ere<Prop>.Star(Ere<Prop>.Sigma());
            var f = Rltl<Prop>.Trigger(sigmaStar, phi);
            Assert.That(f, Is.EqualTo(Rltl<Prop>.Globally(phi)));
        }

        [Test]
        public void Match_SigmaStarRegex_ReducesToGlobally()
        {
            var phi = Rltl<Prop>.Atom(A);
            var sigmaStar = Ere<Prop>.Star(Ere<Prop>.Sigma());
            var f = Rltl<Prop>.Match(sigmaStar, phi);
            Assert.That(f, Is.EqualTo(Rltl<Prop>.Globally(phi)));
        }

        // ---------- Accepting condition ----------

        private static RltlDerivative<Prop, HashSet<string>> NewDeriv()
            => new RltlDerivative<Prop, HashSet<string>>(new PropEba(), new ConditionRegistry<Prop>());

        [Test]
        public void IsAccepting_LivenessOperators_NonAccepting()
        {
            var d = NewDeriv();
            Assert.That(d.IsAccepting(
                Rltl<Prop>.Until(Rltl<Prop>.True(), Rltl<Prop>.Atom(A))), Is.False);
            Assert.That(d.IsAccepting(
                Rltl<Prop>.SeqPrefix(Ere<Prop>.Atom(A), Rltl<Prop>.Atom(B))), Is.False);
            Assert.That(d.IsAccepting(
                Rltl<Prop>.OvlPrefix(Ere<Prop>.Atom(A), Rltl<Prop>.Atom(B))), Is.False);
        }

        [Test]
        public void IsAccepting_SafetyOperators_Accepting()
        {
            var d = NewDeriv();
            Assert.That(d.IsAccepting(
                Rltl<Prop>.Globally(Rltl<Prop>.Atom(A))), Is.True);
            Assert.That(d.IsAccepting(
                Rltl<Prop>.Trigger(Ere<Prop>.Atom(A), Rltl<Prop>.Atom(B))), Is.True);
            Assert.That(d.IsAccepting(
                Rltl<Prop>.Match(Ere<Prop>.Atom(A), Rltl<Prop>.Atom(B))), Is.True);
        }

        // ---------- Closures: factories ----------

        [Test]
        public void WeakClosure_OnEmptyRegex_Reduces_To_False()
        {
            Assert.That(Rltl<Prop>.WeakClosure(Ere<Prop>.Empty()),
                Is.EqualTo(Rltl<Prop>.False()));
        }

        [Test]
        public void WeakClosure_OnNullableRegex_Reduces_To_True()
        {
            // ε is nullable
            Assert.That(Rltl<Prop>.WeakClosure(Ere<Prop>.Epsilon()),
                Is.EqualTo(Rltl<Prop>.True()));
            // a* is nullable
            Assert.That(Rltl<Prop>.WeakClosure(Ere<Prop>.Star(Ere<Prop>.Atom(A))),
                Is.EqualTo(Rltl<Prop>.True()));
        }

        [Test]
        public void NegWeakClosure_OnEmptyRegex_Reduces_To_True()
        {
            Assert.That(Rltl<Prop>.NegWeakClosure(Ere<Prop>.Empty()),
                Is.EqualTo(Rltl<Prop>.True()));
        }

        [Test]
        public void NegWeakClosure_OnNullableRegex_Reduces_To_False()
        {
            Assert.That(Rltl<Prop>.NegWeakClosure(Ere<Prop>.Epsilon()),
                Is.EqualTo(Rltl<Prop>.False()));
        }

        [Test]
        public void OmegaClosure_OnEmptyRegex_Reduces_To_False()
        {
            Assert.That(Rltl<Prop>.OmegaClosure(Ere<Prop>.Empty()),
                Is.EqualTo(Rltl<Prop>.False()));
        }

        [Test]
        public void OmegaClosure_OnEpsilon_Is_Kept_AsNode()
        {
            // ε is nullable but {ε}ω is NOT ⊤; the factory keeps it as a node.
            var f = Rltl<Prop>.OmegaClosure(Ere<Prop>.Epsilon());
            Assert.That(f, Is.InstanceOf<RltlOmegaClosure<Prop>>());
        }

        // ---------- Closures: negation duals ----------

        [Test]
        public void Negate_WeakClosure_YieldsNegWeakClosure()
        {
            var r = Ere<Prop>.Atom(A);
            var w = Rltl<Prop>.WeakClosure(r);   // a is not nullable, kept as node
            var neg = Alg.Not(w);
            Assert.That(neg, Is.InstanceOf<RltlNegWeakClosure<Prop>>());
            Assert.That(((RltlNegWeakClosure<Prop>)neg).Regex, Is.EqualTo(r));
        }

        [Test]
        public void Negate_NegWeakClosure_YieldsWeakClosure()
        {
            var r = Ere<Prop>.Atom(A);
            var nw = Rltl<Prop>.NegWeakClosure(r);
            var neg = Alg.Not(nw);
            Assert.That(neg, Is.InstanceOf<RltlWeakClosure<Prop>>());
        }

        [Test]
        public void Negate_OmegaClosure_Throws()
        {
            var ocl = Rltl<Prop>.OmegaClosure(Ere<Prop>.Atom(A));
            Assert.Throws<NotSupportedException>(() => Alg.Not(ocl));
        }

        // ---------- Closures: semantic emptiness + IsAccepting ----------

        [Test]
        public void EmptinessChecker_DetectsAliveAtom()
        {
            var d = NewDeriv();
            Assert.That(d.Emptiness.IsAlive(Ere<Prop>.Atom(A)), Is.True);
        }

        [Test]
        public void EmptinessChecker_DetectsDeadAtomBottom()
        {
            // Atom(⊥) is structurally not EreEmpty, but its only outgoing
            // derivative path is guarded by ⊥ which the EBA refutes — dead.
            var d = NewDeriv();
            var eba = new PropEba();
            var dead = Ere<Prop>.Atom(eba.Bottom);
            Assert.That(dead, Is.Not.InstanceOf<EreEmpty<Prop>>(),
                "guard must be structurally non-empty.");
            Assert.That(d.Emptiness.IsDead(dead), Is.True,
                "Atom(⊥) has empty language; checker should report dead.");
        }

        [Test]
        public void IsAccepting_WeakClosure_AliveRegex_Accepting()
        {
            var d = NewDeriv();
            // a is non-nullable but alive, so WeakClosure(a) is kept; alive ⇒ accepting.
            var w = Rltl<Prop>.WeakClosure(Ere<Prop>.Atom(A));
            Assert.That(w, Is.InstanceOf<RltlWeakClosure<Prop>>());
            Assert.That(d.IsAccepting(w), Is.True);
        }

        [Test]
        public void IsAccepting_NegWeakClosure_AliveRegex_NonAccepting()
        {
            var d = NewDeriv();
            var nw = Rltl<Prop>.NegWeakClosure(Ere<Prop>.Atom(A));
            Assert.That(nw, Is.InstanceOf<RltlNegWeakClosure<Prop>>());
            Assert.That(d.IsAccepting(nw), Is.False);
        }

        [Test]
        public void IsAccepting_OmegaClosure_Accepting()
        {
            var d = NewDeriv();
            var ocl = Rltl<Prop>.OmegaClosure(Ere<Prop>.Atom(A));
            Assert.That(d.IsAccepting(ocl), Is.True);
        }

        // ---------- Closures: derivative rules (JACM eq. 3010–3014) ----------

        [Test]
        public void Derivative_WeakClosure_NonNullable_LiftsToInnerDerivative()
        {
            // deriv({a}) = ite(Null(a)=false, {deriv(a)}, …) = {deriv(a)}
            // On letter 'a': deriv(a) yields ε (nullable), so wrapped weak closure
            // becomes WeakClosure(ε) = True (nullable rewrite by the factory).
            var d = NewDeriv();
            var w = Rltl<Prop>.WeakClosure(Ere<Prop>.Atom(A));
            var dw = d.Derivative(w);
            var onA = dw.Evaluate(new HashSet<string> { "a" }, d.Registry, d.Eba);
            Assert.That(onA.IsTrue, Is.True,
                "On 'a', deriv({a}) should fold to ⊤ since residual ε is nullable.");
            var onB = dw.Evaluate(new HashSet<string> { "b" }, d.Registry, d.Eba);
            Assert.That(onB.IsFalse, Is.True,
                "On 'b', residual is dead ⇒ {deriv(a)} = {⊥} = ⊥.");
        }

        [Test]
        public void Derivative_NegWeakClosure_NonNullable_LiftsToInnerDerivative()
        {
            // deriv({{a}}̄): on 'a' the residual is ε (nullable) ⇒ {{ε}}̄ = ⊥.
            // On 'b' the residual is dead ⇒ {{⊥}}̄ = ⊤.
            var d = NewDeriv();
            var nw = Rltl<Prop>.NegWeakClosure(Ere<Prop>.Atom(A));
            var dnw = d.Derivative(nw);
            var onA = dnw.Evaluate(new HashSet<string> { "a" }, d.Registry, d.Eba);
            Assert.That(onA.IsFalse, Is.True);
            var onB = dnw.Evaluate(new HashSet<string> { "b" }, d.Registry, d.Eba);
            Assert.That(onB.IsTrue, Is.True);
        }

        [Test]
        public void Derivative_OmegaClosure_UnrollsToSeqPrefix()
        {
            // deriv({a}ω) = deriv(a ; X {a}ω). On 'a', residual must be a Next of {a}ω.
            var d = NewDeriv();
            var ocl = Rltl<Prop>.OmegaClosure(Ere<Prop>.Atom(A));
            var docl = d.Derivative(ocl);
            var onA = docl.Evaluate(new HashSet<string> { "a" }, d.Registry, d.Eba);
            Assert.That(onA.IsFalse, Is.False);
            // The result should reference the same OmegaClosure node (cyclic).
            Assert.That(onA.Clauses.Any(c => c.Any(f => f is RltlNext<Prop> n
                && n.Inner is RltlOmegaClosure<Prop>)), Is.True);
        }

        // ---------- Derivative semantics ----------

        [Test]
        public void Derivative_SeqPrefix_AtomThenPhi()
        {
            // (a ; X b) — must see 'a' first, then 'b' next.
            // Derivative on letter satisfying 'a' (no nullable case since Ere a is not nullable):
            //   ∂(a) = ITE(a, ε, ∅); lifting gives  ITE(a, atom(SeqPrefix(ε, X b)), ⊥)
            //                                    = ITE(a, atom(X b), ⊥)   [ε reduces]
            //                                    = ITE(a, atom(X b), ⊥)
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new RltlDerivative<Prop, HashSet<string>>(eba, reg);

            var phi = Rltl<Prop>.Next(Rltl<Prop>.Atom(B));
            var f = Rltl<Prop>.SeqPrefix(Ere<Prop>.Atom(A), phi);
            var d = deriv.Derivative(f);

            // On 'a': leaf is Dnf with single clause containing X b
            var resultOnA = d.Evaluate(new HashSet<string> { "a" }, reg, eba);
            Assert.That(resultOnA.IsFalse, Is.False);
            Assert.That(resultOnA.Clauses, Has.Count.EqualTo(1));
            Assert.That(resultOnA.Clauses[0].Count(), Is.EqualTo(1));
            Assert.That(resultOnA.Clauses[0].First(), Is.EqualTo(phi));

            // On 'b' (no 'a'): leaf is ⊥
            var resultOnB = d.Evaluate(new HashSet<string> { "b" }, reg, eba);
            Assert.That(resultOnB.IsFalse, Is.True);
        }

        [Test]
        public void Derivative_TriggerSigmaP_BehavesLikeGlobally()
        {
            // Trigger(Σ*, p) ≡ G p:
            //   ∀k. w[0..k]∈Σ* → w[k..]⊨p, i.e., for every k, w[k]∈p.
            // Derivative on letter satisfying 'a':
            //   lifted leaf: Trigger(Σ*, p)  (since Σ* derivative = Σ*)
            //   plus ∂(p) on 'a' since Σ* is nullable
            //   So result = atom(Trigger(Σ*,p)) ∧ atom(⊤)? 
            //   Actually ∂(p) on a-true element should yield Dnf.True (a satisfies p? no — p=A).
            //   Let p = A. Then ∂(A) on 'a' = ⊤.
            //   So result = atom(Trigger(Σ*, A)) ∧ ⊤ = atom(Trigger(Σ*, A))
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new RltlDerivative<Prop, HashSet<string>>(eba, reg);

            var sigma = Ere<Prop>.Sigma();
            var p = Rltl<Prop>.Atom(A);
            var f = Rltl<Prop>.Trigger(sigma, p);
            var d = deriv.Derivative(f);

            // On 'a': p holds, recursive obligation persists
            var rA = d.Evaluate(new HashSet<string> { "a" }, reg, eba);
            Assert.That(rA.IsFalse, Is.False);
            Assert.That(rA.Clauses, Has.Count.EqualTo(1));
            Assert.That(rA.Clauses[0].First(), Is.EqualTo(f), "obligation persists");

            // On '¬a': ∂(p) = ⊥, so the conjunction yields ⊥
            var rNoA = d.Evaluate(new HashSet<string>(), reg, eba);
            Assert.That(rNoA.IsFalse, Is.True);
        }

        // ---------- End-to-end: build ABW → Æ → NBW ----------

        [Test]
        public void EndToEnd_BuildNbw_SeqPrefix()
        {
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new RltlDerivative<Prop, HashSet<string>>(eba, reg);

            // Property: (a* ; b)  — there is some prefix of a's followed by b.
            var formula = Rltl<Prop>.SeqPrefix(
                Ere<Prop>.Star(Ere<Prop>.Atom(A)),
                Rltl<Prop>.Atom(B));

            var abw = deriv.ToABW(formula);
            var ae = new IncrementalAE<Prop, HashSet<string>, Rltl<Prop>>(abw);
            var nbw = ae.ToNBW();

            // Force exploration
            var seen = new HashSet<BreakpointState<Rltl<Prop>>>();
            var queue = new Queue<BreakpointState<Rltl<Prop>>>(nbw.InitialStates);
            foreach (var s in nbw.InitialStates) seen.Add(s);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                foreach (var term in nbw.GetTransition(s))
                    foreach (var leaf in term.GetDistinctLeaves())
                        foreach (var succ in leaf)
                            if (seen.Add(succ)) queue.Enqueue(succ);
            }

            Assert.That(seen.Count, Is.GreaterThan(0));
            Assert.That(nbw.InitialStates.Any(), Is.True);
        }

        [Test]
        public void EndToEnd_GloballyEquivalent_TriggerSigma()
        {
            // Trigger(Σ*, a) and G a should produce structurally similar NBWs
            // (both safety properties — accepting is "true" everywhere).
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new RltlDerivative<Prop, HashSet<string>>(eba, reg);

            var ga = Rltl<Prop>.Globally(Rltl<Prop>.Atom(A));
            var trigSigma = Rltl<Prop>.Trigger(Ere<Prop>.Sigma(), Rltl<Prop>.Atom(A));

            var abw1 = deriv.ToABW(ga);
            var abw2 = deriv.ToABW(trigSigma);
            var nbw1 = new IncrementalAE<Prop, HashSet<string>, Rltl<Prop>>(abw1).ToNBW();
            var nbw2 = new IncrementalAE<Prop, HashSet<string>, Rltl<Prop>>(abw2).ToNBW();

            // Both should have at least one initial state, and the initial state
            // is accepting (no liveness obligation).
            Assert.That(nbw1.InitialStates.All(nbw1.IsAccepting), Is.True);
            Assert.That(nbw2.InitialStates.All(nbw2.IsAccepting), Is.True);
        }

        [Test]
        public void EndToEnd_SeqPrefix_HasLivenessObligation()
        {
            // (a* ; b) is liveness — the breakpoint construction must produce
            // at least one reachable state with a pending obligation (O ≠ ∅,
            // i.e. non-accepting), reflecting the unfulfilled liveness.
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new RltlDerivative<Prop, HashSet<string>>(eba, reg);

            var formula = Rltl<Prop>.SeqPrefix(
                Ere<Prop>.Star(Ere<Prop>.Atom(A)),
                Rltl<Prop>.Atom(B));

            var abw = deriv.ToABW(formula);
            var nbw = new IncrementalAE<Prop, HashSet<string>, Rltl<Prop>>(abw).ToNBW();

            // Force exploration of all reachable states.
            var seen = new HashSet<BreakpointState<Rltl<Prop>>>(nbw.InitialStates);
            var queue = new Queue<BreakpointState<Rltl<Prop>>>(nbw.InitialStates);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                foreach (var term in nbw.GetTransition(s))
                    foreach (var leaf in term.GetDistinctLeaves())
                        foreach (var succ in leaf)
                            if (seen.Add(succ)) queue.Enqueue(succ);
            }

            Assert.That(seen.Any(s => !nbw.IsAccepting(s)), Is.True,
                "Liveness formula must yield a reachable breakpoint with a pending obligation.");
        }
    }
}
