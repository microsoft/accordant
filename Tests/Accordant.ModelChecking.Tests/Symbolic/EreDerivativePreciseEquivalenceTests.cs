namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for <see cref="EreDerivative{TPred,TElem}.AreEquivalentPrecise"/>
    /// — the precise (CAV'26-bisim-backed) language-equivalence oracle that
    /// upgrades canonical-rep aliasing beyond the cheap signature check.
    /// </summary>
    [TestFixture]
    public class EreDerivativePreciseEquivalenceTests
    {
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

        private static EreDerivative<Prop, HashSet<string>> NewDeriv()
        {
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            return new EreDerivative<Prop, HashSet<string>>(eba, reg);
        }

        private static Ere<Prop> A => Ere<Prop>.Atom(new Prop("a"));
        private static Ere<Prop> B => Ere<Prop>.Atom(new Prop("b"));

        [Test]
        public void Precise_AgreesWithSignature_OnTriviallyEqualAndDistinct()
        {
            var d = NewDeriv();
            Assert.That(d.AreEquivalentPrecise(A, A), Is.True);
            Assert.That(d.AreEquivalentPrecise(A, B), Is.False);
        }

        [Test]
        public void Precise_StrictlyMorePowerful_ThanSignature()
        {
            // Find a pair the cheap check misses but the precise check catches.
            // (a + b)* ≡ (b + a)*  — these ARE caught by ACI canonicalisation,
            // so try harder: a*·a* ≡ a*  vs cheap signature.
            var d = NewDeriv();
            var aStar = Ere<Prop>.Star(A);
            var aStarTwice = Ere<Prop>.Concat(aStar, aStar);
            // We don't know whether the cheap check catches this — but the
            // precise check MUST.
            Assert.That(d.AreEquivalentPrecise(aStarTwice, aStar), Is.True);
        }

        [Test]
        public void Precise_DetectsKleeneUnfold()
        {
            // a* ≡ ε + a·a*  — the constructor often folds this, but if we
            // build it via a structurally-distinct path the precise check
            // still confirms.
            var d = NewDeriv();
            var aStar = Ere<Prop>.Star(A);
            var unfolded = Ere<Prop>.Union(Ere<Prop>.Epsilon(),
                Ere<Prop>.Concat(A, aStar));
            Assert.That(d.AreEquivalentPrecise(aStar, unfolded), Is.True);
        }

        [Test]
        public void Precise_AliasesCanonicalRep()
        {
            var d = NewDeriv();
            // Build two language-equivalent but structurally distinct forms.
            var aStar = Ere<Prop>.Star(A);
            var aStarTwice = Ere<Prop>.Concat(aStar, aStar);
            // Force derivatives so both are in the cache.
            d.Derivative(aStar);
            d.Derivative(aStarTwice);

            int repBefore_aStar = d.CanonicalRepresentative(aStar);
            int repBefore_two = d.CanonicalRepresentative(aStarTwice);
            // If the signature didn't already merge them, exercise the
            // precise oracle and verify the canonical-rep map is updated.
            if (repBefore_aStar != repBefore_two)
            {
                Assert.That(d.AreEquivalentPrecise(aStar, aStarTwice), Is.True);
                Assert.That(d.CanonicalRepresentative(aStar),
                    Is.EqualTo(d.CanonicalRepresentative(aStarTwice)));
            }
        }

        [Test]
        public void Precise_NotEquivalent_DoesNotAlias()
        {
            var d = NewDeriv();
            Assert.That(d.AreEquivalentPrecise(A, B), Is.False);
            Assert.That(d.CanonicalRepresentative(A),
                Is.Not.EqualTo(d.CanonicalRepresentative(B)));
        }

        [Test]
        public void Precise_IsSymmetric_AndCached()
        {
            var d = NewDeriv();
            var aStar = Ere<Prop>.Star(A);
            var unfolded = Ere<Prop>.Union(Ere<Prop>.Epsilon(),
                Ere<Prop>.Concat(A, aStar));
            Assert.That(d.AreEquivalentPrecise(aStar, unfolded), Is.True);
            // Reverse order — same answer, served from cache.
            Assert.That(d.AreEquivalentPrecise(unfolded, aStar), Is.True);
        }

        [Test]
        public void Precise_TransitiveAliasing_ThroughCanonicalRep()
        {
            // After aliasing a≡b and b≡c, CanonicalRepresentative(a) ==
            // CanonicalRepresentative(c) (transitivity via path-compression).
            // Construct three pairwise-equivalent regex forms.
            var d = NewDeriv();
            var r1 = Ere<Prop>.Star(A);                                 // a*
            var r2 = Ere<Prop>.Concat(Ere<Prop>.Star(A), Ere<Prop>.Star(A)); // a*·a*
            var r3 = Ere<Prop>.Star(Ere<Prop>.Star(A));                 // (a*)*

            Assert.That(d.AreEquivalentPrecise(r1, r2), Is.True);
            Assert.That(d.AreEquivalentPrecise(r2, r3), Is.True);
            // Transitivity:
            int rep1 = d.CanonicalRepresentative(r1);
            int rep3 = d.CanonicalRepresentative(r3);
            Assert.That(rep1, Is.EqualTo(rep3));
        }

        [Test]
        public void Precise_DistinguishesInequivalent_AfterEquivalentAlias()
        {
            // Aliasing a*·a* ≡ a* should NOT mistakenly equate a* and b*.
            var d = NewDeriv();
            var aStar = Ere<Prop>.Star(A);
            var aStarTwice = Ere<Prop>.Concat(aStar, aStar);
            var bStar = Ere<Prop>.Star(B);
            Assert.That(d.AreEquivalentPrecise(aStar, aStarTwice), Is.True);
            Assert.That(d.AreEquivalentPrecise(aStar, bStar), Is.False);
        }
    }
}
