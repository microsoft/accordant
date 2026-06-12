namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    [TestFixture]
    public class EreEquivalenceCheckerTests
    {
        // Reuse the same Prop/PropEba setup as EreTests.
        private sealed class Prop : IEquatable<Prop>, IComparable<Prop>
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

        private static Ere<Prop> A => Ere<Prop>.Atom(new Prop("a"));
        private static Ere<Prop> B => Ere<Prop>.Atom(new Prop("b"));
        private static Ere<Prop> C => Ere<Prop>.Atom(new Prop("c"));

        private EreEquivalenceChecker<Prop, HashSet<string>> NewChecker()
        {
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);
            return new EreEquivalenceChecker<Prop, HashSet<string>>(deriv);
        }

        [Test]
        public void Trivial_StructuralEquality()
        {
            var c = NewChecker();
            Assert.That(c.AreEquivalent(A, A), Is.True);
            Assert.That(c.AreEquivalent(Ere<Prop>.Empty(), Ere<Prop>.Empty()), Is.True);
            Assert.That(c.AreEquivalent(Ere<Prop>.Epsilon(), Ere<Prop>.Epsilon()), Is.True);
        }

        [Test]
        public void Trivial_DistinctAtoms()
        {
            var c = NewChecker();
            Assert.That(c.AreEquivalent(A, B), Is.False);
        }

        [Test]
        public void Empty_NotEquivalentToEpsilon()
        {
            var c = NewChecker();
            // L(∅) = ∅, L(ε) = {""} — differ on ε.
            Assert.That(c.AreEquivalent(Ere<Prop>.Empty(), Ere<Prop>.Epsilon()), Is.False);
        }

        [Test]
        public void UnionACI_IsHandledByConstructor()
        {
            // After ACI-normalisation Union(A,B) ≡ Union(B,A) structurally;
            // the checker should still confirm equivalence (trivially).
            var c = NewChecker();
            var ab = Ere<Prop>.Union(A, B);
            var ba = Ere<Prop>.Union(B, A);
            Assert.That(c.AreEquivalent(ab, ba), Is.True);
        }

        [Test]
        public void StarAbsorption_EquivalentForms()
        {
            // a* ≡ ε ∨ a·a*  (Kleene's identity).
            // Constructor folds a·a* + ε into a* (R + R·R* = R·R* + R; with ε → a*).
            // So this is mostly a constructor check; the checker is a no-op here.
            var c = NewChecker();
            var aStar = Ere<Prop>.Star(A);
            var unfolded = Ere<Prop>.Union(Ere<Prop>.Epsilon(),
                Ere<Prop>.Concat(A, aStar));
            Assert.That(c.AreEquivalent(aStar, unfolded), Is.True);
        }

        [Test]
        public void ConcatAssociativity_Equivalent()
        {
            // (a·b)·c ≡ a·(b·c) — both normalise to right-assoc form via the
            // Concat factory. Checker confirms.
            var c = NewChecker();
            var l = Ere<Prop>.Concat(Ere<Prop>.Concat(A, B), C);
            var r = Ere<Prop>.Concat(A, Ere<Prop>.Concat(B, C));
            Assert.That(c.AreEquivalent(l, r), Is.True);
        }

        [Test]
        public void StarUnion_BothSides_Equivalent()
        {
            // (a + b)* ≡ (b + a)* — trivially via ACI of Union.
            var c = NewChecker();
            var ab = Ere<Prop>.Star(Ere<Prop>.Union(A, B));
            var ba = Ere<Prop>.Star(Ere<Prop>.Union(B, A));
            Assert.That(c.AreEquivalent(ab, ba), Is.True);
        }

        [Test]
        public void DenotationallyEquivalent_StructurallyDifferent()
        {
            // a*  vs  (a + ε)*    — should be equivalent (ε* contributes nothing).
            // The Star factory folds (R + ε)* → R* so these end up structurally
            // identical. Test that the checker confirms equivalence.
            var c = NewChecker();
            var aStar = Ere<Prop>.Star(A);
            var aOptStar = Ere<Prop>.Star(Ere<Prop>.Union(A, Ere<Prop>.Epsilon()));
            Assert.That(c.AreEquivalent(aStar, aOptStar), Is.True);

            // Truly different (a* vs (a·a)*) — must report inequivalent.
            var aaStar = Ere<Prop>.Star(Ere<Prop>.Concat(A, A));
            Assert.That(c.AreEquivalent(aStar, aaStar), Is.False);
        }

        [Test]
        public void DeMorganEquivalent_ViaComplement()
        {
            // ~(a + b)  ≡  ~a ∩ ~b
            // The Complement factory pushes De Morgan inward (per the prior
            // regex-rewrites pass), so these should canonicalise to the same
            // form. The checker should confirm.
            var c = NewChecker();
            var lhs = Ere<Prop>.Complement(Ere<Prop>.Union(A, B));
            var rhs = Ere<Prop>.Intersect(Ere<Prop>.Complement(A), Ere<Prop>.Complement(B));
            Assert.That(c.AreEquivalent(lhs, rhs), Is.True);
        }

        [Test]
        public void Subsumption_PvsPunionQ_Inequivalent()
        {
            // p ≢ p ∨ q   when L(q) ⊄ L(p).
            // Bisim drives through the XOR, reaches a leaf showing q-witness alive.
            var c = NewChecker();
            var pq = Ere<Prop>.Union(A, B);
            Assert.That(c.AreEquivalent(A, pq), Is.False);
        }

        [Test]
        public void PaperShowcase_RepetitionVsAtom_NotEquivalent()
        {
            // a  ≢  a + (a·a)    — quick analogue of the paper's
            // "a vs a | a{10000}" non-equivalence detection via the
            // emptiness-fallthrough optimisation. The XOR derivative
            // produces leaves like (∅ ⊕ ε) = ε (nullable) on the second
            // step, so the checker concludes False fast.
            var c = NewChecker();
            var aa = Ere<Prop>.Concat(A, A);
            var r = Ere<Prop>.Union(A, aa);
            Assert.That(c.AreEquivalent(A, r), Is.False);
        }

        [Test]
        public void PaperShowcase_LongRepetition_NotEquivalent()
        {
            // The actual paper showcase: a  ≢  a + a^N  for large N.
            // The bisim alone would need ~N steps; the emptiness fall-through
            // detects non-equivalence in constant time (the residual
            // ∅ ⊕ a^k is non-empty for k > 0, alive immediately).
            // We test moderate N to keep the test fast.
            var c = NewChecker();
            const int N = 50;
            Ere<Prop> repeated = A;
            for (int i = 1; i < N; i++) repeated = Ere<Prop>.Concat(repeated, A);
            var r = Ere<Prop>.Union(A, repeated);
            Assert.That(c.AreEquivalent(A, r), Is.False);
        }

        [Test]
        public void IntersectionWithUniverse_IsIdentity()
        {
            // R ∩ Σ* ≡ R   — handled by Intersect factory (Σ* unit), trivial.
            var c = NewChecker();
            var aStar = Ere<Prop>.Star(A);
            var withSigma = Ere<Prop>.Intersect(aStar, Ere<Prop>.Sigma());
            Assert.That(c.AreEquivalent(aStar, withSigma), Is.True);
        }

        [Test]
        public void Complement_DoubleNegation()
        {
            // ~~R ≡ R — already collapsed by Complement factory.
            var c = NewChecker();
            var aStar = Ere<Prop>.Star(A);
            Assert.That(
                c.AreEquivalent(aStar,
                    Ere<Prop>.Complement(Ere<Prop>.Complement(aStar))),
                Is.True);
        }

        [Test]
        public void IsLanguageEmpty_OfIntersection()
        {
            // a ∩ ε ≡ ∅  (a needs one letter, ε needs zero letters).
            var c = NewChecker();
            var aWithEps = Ere<Prop>.Intersect(A, Ere<Prop>.Epsilon());
            Assert.That(c.IsLanguageEmpty(aWithEps), Is.True);
        }

        [Test]
        public void IsLanguageEmpty_OfStar_False()
        {
            // L(a*) ⊇ {""} → not empty.
            var c = NewChecker();
            Assert.That(c.IsLanguageEmpty(Ere<Prop>.Star(A)), Is.False);
        }
    }
}
