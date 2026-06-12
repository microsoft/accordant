namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Differential oracle for ERE equivalence: every test case is decided
    /// two independent ways and required to agree with each other AND with
    /// the labelled expected answer.
    ///
    /// <para>Method A: <see cref="EreEquivalenceChecker{TPred,TElem}.AreEquivalent"/>
    /// — the CAV'26 bisimulation algorithm.</para>
    ///
    /// <para>Method B: two-way language-subsumption via emptiness —
    /// <c>L(p) = L(q)  ⇔  L(p ∩ ¬q) = ∅  ∧  L(q ∩ ¬p) = ∅</c>. Both halves
    /// are decided with <see cref="EreEmptinessChecker{TPred,TElem}"/>.</para>
    ///
    /// <para>The two methods exercise entirely different machinery
    /// (bisim + union-find + EreXor canonicalisation vs.
    /// intersect + complement + plain BFS over derivatives), so
    /// disagreement reliably exposes a bug in one of them.</para>
    /// </summary>
    [TestFixture]
    public class EreEquivalenceDifferentialOracleTests
    {
        public sealed class Prop : IEquatable<Prop>, IComparable<Prop>
        {
            public string Name { get; }
            public Prop(string name) { Name = name; }
            public bool Equals(Prop other) => other != null && Name == other.Name;
            public override bool Equals(object obj) => Equals(obj as Prop);
            public override int GetHashCode() => Name.GetHashCode();
            public int CompareTo(Prop other) => string.Compare(Name, other?.Name, StringComparison.Ordinal);
            public override string ToString() => Name;
        }

        private sealed class PropEba : IEffectiveBooleanAlgebra<Prop, HashSet<string>>
        {
            public Prop Top { get; } = new Prop("⊤");
            public Prop Bottom { get; } = new Prop("⊥");
            public Prop And(Prop a, Prop b)
            {
                if (a.Name == "⊤") return b;
                if (b.Name == "⊤") return a;
                if (a.Name == "⊥" || b.Name == "⊥") return Bottom;
                if (a.Equals(b)) return a;
                // (¬x) ∧ x  →  ⊥, x ∧ (¬x) → ⊥
                if (a.Name == "¬" + b.Name || b.Name == "¬" + a.Name) return Bottom;
                return new Prop($"({a.Name}∧{b.Name})");
            }
            public Prop Or(Prop a, Prop b)
            {
                if (a.Name == "⊥") return b;
                if (b.Name == "⊥") return a;
                if (a.Name == "⊤" || b.Name == "⊤") return Top;
                if (a.Equals(b)) return a;
                if (a.Name == "¬" + b.Name || b.Name == "¬" + a.Name) return Top;
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

        private static Ere<Prop> Atom(string n) => Ere<Prop>.Atom(new Prop(n));
        private static readonly Ere<Prop> A = Atom("a");
        private static readonly Ere<Prop> B = Atom("b");
        private static readonly Ere<Prop> C = Atom("c");
        private static readonly Ere<Prop> Eps = Ere<Prop>.Epsilon();
        private static readonly Ere<Prop> Bot = Ere<Prop>.Empty();
        private static Ere<Prop> Star(Ere<Prop> r) => Ere<Prop>.Star(r);
        private static Ere<Prop> Plus(params Ere<Prop>[] xs)
        {
            var r = xs[0];
            for (int i = 1; i < xs.Length; i++) r = Ere<Prop>.Union(r, xs[i]);
            return r;
        }
        private static Ere<Prop> Cat(params Ere<Prop>[] xs)
        {
            var r = xs[xs.Length - 1];
            for (int i = xs.Length - 2; i >= 0; i--) r = Ere<Prop>.Concat(xs[i], r);
            return r;
        }
        private static Ere<Prop> Not(Ere<Prop> r) => Ere<Prop>.Complement(r);
        private static Ere<Prop> Cap(Ere<Prop> a, Ere<Prop> b) => Ere<Prop>.Intersect(a, b);

        // A test "case": label, p, q, expected equivalence.
        public sealed class Case
        {
            public string Label { get; }
            public Ere<Prop> P { get; }
            public Ere<Prop> Q { get; }
            public bool ExpectedEquivalent { get; }
            public Case(string label, Ere<Prop> p, Ere<Prop> q, bool eq)
            { Label = label; P = p; Q = q; ExpectedEquivalent = eq; }
            public override string ToString() => Label;
        }

        public static IEnumerable<Case> AllCases()
        {
            // ---- Equivalent pairs ----
            yield return new Case("identity: a = a", A, A, true);
            yield return new Case("identity: ε = ε", Eps, Eps, true);
            yield return new Case("identity: ⊥ = ⊥", Bot, Bot, true);
            yield return new Case("Union ACI: a+b = b+a",
                Plus(A, B), Plus(B, A), true);
            yield return new Case("Union ACI 3: (a+b)+c = a+(b+c)",
                Plus(Plus(A, B), C), Plus(A, Plus(B, C)), true);
            yield return new Case("Union idempotence: a+a = a",
                Plus(A, A), A, true);
            yield return new Case("Concat assoc: (a·b)·c = a·(b·c)",
                Cat(Cat(A, B), C), Cat(A, Cat(B, C)), true);
            yield return new Case("Kleene unfold: a* = ε + a·a*",
                Star(A), Plus(Eps, Cat(A, Star(A))), true);
            yield return new Case("Right unfold: a* = ε + a*·a",
                Star(A), Plus(Eps, Cat(Star(A), A)), true);
            yield return new Case("Star idempotence: (a*)* = a*",
                Star(Star(A)), Star(A), true);
            yield return new Case("Star of (r+ε): (a+ε)* = a*",
                Star(Plus(A, Eps)), Star(A), true);
            yield return new Case("Star fusion: (a*·b*)* = (a+b)*",
                Star(Cat(Star(A), Star(B))), Star(Plus(A, B)), true);
            yield return new Case("Star-of-ε = ε", Star(Eps), Eps, true);
            yield return new Case("Star-of-⊥ = ε", Star(Bot), Eps, true);
            yield return new Case("ε·a = a", Cat(Eps, A), A, true);
            yield return new Case("a·ε = a", Cat(A, Eps), A, true);
            yield return new Case("⊥·a = ⊥", Cat(Bot, A), Bot, true);
            yield return new Case("Distributivity: a·(b+c) = a·b + a·c",
                Cat(A, Plus(B, C)), Plus(Cat(A, B), Cat(A, C)), true);
            yield return new Case("Distributivity right: (a+b)·c = a·c + b·c",
                Cat(Plus(A, B), C), Plus(Cat(A, C), Cat(B, C)), true);
            yield return new Case("Double complement: ¬¬a = a",
                Not(Not(A)), A, true);
            yield return new Case("De Morgan: ¬(a+b) = ¬a ∩ ¬b",
                Not(Plus(A, B)), Cap(Not(A), Not(B)), true);
            yield return new Case("De Morgan: ¬(a∩b) = ¬a + ¬b",
                Not(Cap(A, B)), Plus(Not(A), Not(B)), true);
            yield return new Case("Σ* = ¬⊥", Ere<Prop>.Sigma(), Not(Bot), true);
            yield return new Case("a ∩ Σ* = a", Cap(A, Ere<Prop>.Sigma()), A, true);
            yield return new Case("a ∩ ⊥ = ⊥", Cap(A, Bot), Bot, true);
            yield return new Case("a ∩ a = a", Cap(A, A), A, true);
            yield return new Case("Sliding: a·(b·a)* = (a·b)*·a",
                Cat(A, Star(Cat(B, A))), Cat(Star(Cat(A, B)), A), true);
            yield return new Case("Star concat: a*·a* = a*",
                Cat(Star(A), Star(A)), Star(A), true);

            // ---- Inequivalent pairs ----
            yield return new Case("distinct atoms: a ≠ b", A, B, false);
            yield return new Case("a ≠ ε", A, Eps, false);
            yield return new Case("a ≠ ⊥", A, Bot, false);
            yield return new Case("ε ≠ ⊥", Eps, Bot, false);
            yield return new Case("a* ≠ b*", Star(A), Star(B), false);
            yield return new Case("a* ≠ (a·a)*",
                Star(A), Star(Cat(A, A)), false);
            yield return new Case("(a+b)* ≠ a* + b*",
                Star(Plus(A, B)), Plus(Star(A), Star(B)), false);
            yield return new Case("(a·b)* ≠ a*·b*",
                Star(Cat(A, B)), Cat(Star(A), Star(B)), false);
            yield return new Case("a·b ≠ b·a",
                Cat(A, B), Cat(B, A), false);
            yield return new Case("a·b* ≠ a*·b",
                Cat(A, Star(B)), Cat(Star(A), B), false);
            yield return new Case("a ≠ a+b", A, Plus(A, B), false);
            yield return new Case("a+b ≠ a∩b (when both nonempty, distinct)",
                Plus(A, B), Cap(A, B), false);
            yield return new Case("¬a ≠ a", Not(A), A, false);
            // Σ* ≠ ε (Σ* contains all words, ε only the empty one)
            yield return new Case("Σ* ≠ ε", Ere<Prop>.Sigma(), Eps, false);

            // ---- Subtle equivalences (good stress for the algorithm) ----
            yield return new Case("a+ε+a·a* = a*",
                Plus(Plus(A, Eps), Cat(A, Star(A))), Star(A), true);
            yield return new Case("(a+b)* = (a*+b*)*",
                Star(Plus(A, B)), Star(Plus(Star(A), Star(B))), true);
            yield return new Case("(a*·b)* ·a* = (a+b)*",
                Cat(Star(Cat(Star(A), B)), Star(A)), Star(Plus(A, B)), true);
            yield return new Case("Idempotent intersection with star: a* ∩ a* = a*",
                Cap(Star(A), Star(A)), Star(A), true);
            yield return new Case("Star of union absorbs Σ*: (a+Σ*)* = Σ*",
                Star(Plus(A, Ere<Prop>.Sigma())), Ere<Prop>.Sigma(), true);
        }

        private (EreEquivalenceChecker<Prop, HashSet<string>> equiv,
                 EreEmptinessChecker<Prop, HashSet<string>> empt)
            MakeCheckers()
        {
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);
            var empt = new EreEmptinessChecker<Prop, HashSet<string>>(deriv);
            var equiv = new EreEquivalenceChecker<Prop, HashSet<string>>(deriv, empt);
            return (equiv, empt);
        }

        // L(p) ⊆ L(q)  ⇔  L(p ∩ ¬q) = ∅
        private static bool Subsumes(EreEmptinessChecker<Prop, HashSet<string>> empt,
            Ere<Prop> p, Ere<Prop> q)
            => empt.IsDead(Cap(p, Not(q)));

        private static bool SubsumptionEquivalent(
            EreEmptinessChecker<Prop, HashSet<string>> empt, Ere<Prop> p, Ere<Prop> q)
            => Subsumes(empt, p, q) && Subsumes(empt, q, p);

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var c in AllCases())
                yield return new TestCaseData(c).SetName(c.Label);
        }

        [TestCaseSource(nameof(Cases))]
        public void Bisim_AgreesWithSubsumption_AndWithExpected(Case c)
        {
            // Fresh checkers per case so accidental cross-case caching cannot
            // mask a bug.
            var (equiv, empt) = MakeCheckers();

            bool bisim = equiv.AreEquivalent(c.P, c.Q);
            bool subsumption = SubsumptionEquivalent(empt, c.P, c.Q);

            Assert.Multiple(() =>
            {
                Assert.That(bisim, Is.EqualTo(c.ExpectedEquivalent),
                    $"bisim disagrees with expected for '{c.Label}'");
                Assert.That(subsumption, Is.EqualTo(c.ExpectedEquivalent),
                    $"subsumption disagrees with expected for '{c.Label}'");
                Assert.That(bisim, Is.EqualTo(subsumption),
                    $"bisim and subsumption disagree for '{c.Label}' " +
                    $"(bisim={bisim}, subsumption={subsumption})");
            });
        }

        // Witness-shape sanity: when the bisim says "inequivalent", a witness
        // must be returned (non-null). Full semantic verification of the
        // witness against L(P) △ L(Q) requires a more precise EBA than the
        // toy used here (compound (a∧¬b) predicates aren't fully decided by
        // string-name IsSatisfiable), so that check lives in EreWitnessTests
        // with hand-picked element instantiations.
        [TestCaseSource(nameof(Cases))]
        public void Witness_IsReturnedForInequivalentPairs(Case c)
        {
            if (c.ExpectedEquivalent) Assert.Ignore("only meaningful for inequivalent pairs");
            var (equiv, _) = MakeCheckers();
            Assert.That(equiv.AreInequivalent(c.P, c.Q, out var w), Is.True);
            Assert.That(w, Is.Not.Null, $"missing witness for '{c.Label}'");
        }
    }
}
