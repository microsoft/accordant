namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    [TestFixture]
    public class EreTests
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

        private static Ere<Prop> A => Ere<Prop>.Atom(new Prop("a"));
        private static Ere<Prop> B => Ere<Prop>.Atom(new Prop("b"));

        [Test]
        public void Nullable_BasicCases()
        {
            Assert.That(Ere<Prop>.Empty().Nullable, Is.False);
            Assert.That(Ere<Prop>.Epsilon().Nullable, Is.True);
            Assert.That(A.Nullable, Is.False);
            Assert.That(Ere<Prop>.Star(A).Nullable, Is.True);
            Assert.That(Ere<Prop>.Concat(A, B).Nullable, Is.False);
            Assert.That(Ere<Prop>.Concat(Ere<Prop>.Star(A), Ere<Prop>.Star(B)).Nullable, Is.True);
            Assert.That(Ere<Prop>.Union(A, Ere<Prop>.Epsilon()).Nullable, Is.True);
            Assert.That(Ere<Prop>.Intersect(A, Ere<Prop>.Star(A)).Nullable, Is.False);
            Assert.That(Ere<Prop>.Complement(Ere<Prop>.Epsilon()).Nullable, Is.False);
            Assert.That(Ere<Prop>.Complement(A).Nullable, Is.True);
        }

        [Test]
        public void Simplifications_Concat()
        {
            Assert.That(Ere<Prop>.Concat(Ere<Prop>.Empty(), A), Is.EqualTo(Ere<Prop>.Empty()));
            Assert.That(Ere<Prop>.Concat(A, Ere<Prop>.Empty()), Is.EqualTo(Ere<Prop>.Empty()));
            Assert.That(Ere<Prop>.Concat(Ere<Prop>.Epsilon(), A), Is.EqualTo(A));
            Assert.That(Ere<Prop>.Concat(A, Ere<Prop>.Epsilon()), Is.EqualTo(A));
        }

        [Test]
        public void Simplifications_Star()
        {
            Assert.That(Ere<Prop>.Star(Ere<Prop>.Empty()), Is.EqualTo(Ere<Prop>.Epsilon()));
            Assert.That(Ere<Prop>.Star(Ere<Prop>.Epsilon()), Is.EqualTo(Ere<Prop>.Epsilon()));
            var aStar = Ere<Prop>.Star(A);
            Assert.That(Ere<Prop>.Star(aStar), Is.EqualTo(aStar));
        }

        [Test]
        public void Simplifications_Complement_Involutive()
        {
            var aStar = Ere<Prop>.Star(A);
            Assert.That(Ere<Prop>.Complement(Ere<Prop>.Complement(aStar)), Is.EqualTo(aStar));
        }

        [Test]
        public void Simplifications_Union_Idempotent()
        {
            Assert.That(Ere<Prop>.Union(A, A), Is.EqualTo(A));
            Assert.That(Ere<Prop>.Union(A, Ere<Prop>.Empty()), Is.EqualTo(A));
            Assert.That(Ere<Prop>.Union(A, Ere<Prop>.Sigma()), Is.EqualTo(Ere<Prop>.Sigma()));
        }

        [Test]
        public void Simplifications_Intersect_Idempotent()
        {
            Assert.That(Ere<Prop>.Intersect(A, A), Is.EqualTo(A));
            Assert.That(Ere<Prop>.Intersect(A, Ere<Prop>.Empty()), Is.EqualTo(Ere<Prop>.Empty()));
            Assert.That(Ere<Prop>.Intersect(A, Ere<Prop>.Sigma()), Is.EqualTo(A));
        }

        [Test]
        public void Simplifications_ComplementaryLanguage()
        {
            // R ∩ ~R = ∅
            Assert.That(
                Ere<Prop>.Intersect(A, Ere<Prop>.Complement(A)),
                Is.EqualTo(Ere<Prop>.Empty()));
            // R + ~R = Σ*
            Assert.That(
                Ere<Prop>.Union(A, Ere<Prop>.Complement(A)),
                Is.EqualTo(Ere<Prop>.Sigma()));
            // Survives flattening: A + B + ~A = Σ*
            Assert.That(
                Ere<Prop>.Union(A, Ere<Prop>.Union(B, Ere<Prop>.Complement(A))),
                Is.EqualTo(Ere<Prop>.Sigma()));
        }

        [Test]
        public void Simplifications_Intersect_StarAbsorption()
        {
            // R ∩ R* = R (dual of the union-side R + R* = R*).
            Assert.That(
                Ere<Prop>.Intersect(A, Ere<Prop>.Star(A)),
                Is.EqualTo(A));
        }

        [Test]
        public void Simplifications_Intersect_Epsilon()
        {
            // ε ∩ R = ε if R nullable, ∅ otherwise.
            Assert.That(
                Ere<Prop>.Intersect(Ere<Prop>.Epsilon(), A),
                Is.EqualTo(Ere<Prop>.Empty()));
            Assert.That(
                Ere<Prop>.Intersect(Ere<Prop>.Epsilon(), Ere<Prop>.Star(A)),
                Is.EqualTo(Ere<Prop>.Epsilon()));
        }

        [Test]
        public void Simplifications_DeMorgan()
        {
            // ~(R + S) = ~R ∩ ~S
            var lhs1 = Ere<Prop>.Complement(Ere<Prop>.Union(A, B));
            var rhs1 = Ere<Prop>.Intersect(Ere<Prop>.Complement(A), Ere<Prop>.Complement(B));
            Assert.That(lhs1, Is.EqualTo(rhs1));
            // ~(R ∩ S) = ~R + ~S
            var lhs2 = Ere<Prop>.Complement(Ere<Prop>.Intersect(A, B));
            var rhs2 = Ere<Prop>.Union(Ere<Prop>.Complement(A), Ere<Prop>.Complement(B));
            Assert.That(lhs2, Is.EqualTo(rhs2));
        }

        [Test]
        public void Derivative_Memoisation_ReusesResult()
        {
            // Repeated calls on the same Ere instance must return the same
            // hash-consed TransitionTerm (reference equality).
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var r = Ere<Prop>.Concat(Ere<Prop>.Star(A), B);
            var d1 = deriv.Derivative(r);
            var d2 = deriv.Derivative(r);
            Assert.That(ReferenceEquals(d1, d2), Is.True,
                "Memoised derivative should return the same instance.");
        }

        [Test]
        public void Derivative_Equivalence_DetectedViaBehaviorSignature()
        {
            // a + b  and  b + a should canonicalize to the same Ere already
            // (ACI in Union) — so the test just sanity-checks AreEquivalent
            // on regexes that are syntactically distinct but derivative-equal.
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            // (A + B) and (A + B + ∅) — the ∅ is dropped by Union.Create, so
            // these end up as the same Id. Use them as a smoke test.
            var u1 = Ere<Prop>.Union(A, B);
            var u2 = Ere<Prop>.Union(Ere<Prop>.Union(A, B), Ere<Prop>.Empty());
            Assert.That(deriv.AreEquivalent(u1, u2), Is.True);

            // (A·B*) and (A · (B*·B*)) — the latter normalises to (A·B*) via
            // the R*·R* = R* rewrite. Equivalent at the Ere level too.
            var bStar = Ere<Prop>.Star(B);
            var r1 = Ere<Prop>.Concat(A, bStar);
            var r2 = Ere<Prop>.Concat(A, Ere<Prop>.Concat(bStar, bStar));
            Assert.That(deriv.AreEquivalent(r1, r2), Is.True);

            // Sanity: clearly inequivalent regexes are not equivalent.
            Assert.That(deriv.AreEquivalent(A, B), Is.False);
        }

        [Test]
        public void Derivative_Atom()
        {
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var dA = deriv.Derivative(A);
            // Evaluate against a-true and a-false elements
            var aTrue = new HashSet<string> { "a" };
            var aFalse = new HashSet<string>();
            Assert.That(dA.Evaluate(aTrue, reg, eba), Is.EqualTo(Ere<Prop>.Epsilon()));
            Assert.That(dA.Evaluate(aFalse, reg, eba), Is.EqualTo(Ere<Prop>.Empty()));
        }

        [Test]
        public void Derivative_Star_PreservesSelf()
        {
            // ∂(a*) on letter 'a' should give ε · a* = a* (concat simplification)
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var aStar = Ere<Prop>.Star(A);
            var d = deriv.Derivative(aStar);
            var aTrue = new HashSet<string> { "a" };
            Assert.That(d.Evaluate(aTrue, reg, eba), Is.EqualTo(aStar));
            // On non-a, should give ∅ · a* = ∅
            Assert.That(d.Evaluate(new HashSet<string>(), reg, eba), Is.EqualTo(Ere<Prop>.Empty()));
        }

        [Test]
        public void Derivative_Concat_NullableLeft()
        {
            // ∂(a* · b) on 'a' = (∂(a*) · b) ∨ ∂(b) since a* is nullable
            //                 = a* · b ∨ ∅ = a*·b   (on 'a')
            // on 'b' = (∂(a*)·b on 'b': ∅·b=∅) ∨ (∂(b) on 'b': ε) = ε
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var r = Ere<Prop>.Concat(Ere<Prop>.Star(A), B);
            var d = deriv.Derivative(r);
            var aOnly = new HashSet<string> { "a" };
            var bOnly = new HashSet<string> { "b" };
            Assert.That(d.Evaluate(aOnly, reg, eba), Is.EqualTo(r));
            // b alone: a* doesn't match, so ∂(a*·b)=ε
            Assert.That(d.Evaluate(bOnly, reg, eba), Is.EqualTo(Ere<Prop>.Epsilon()));
        }

        [Test]
        public void Derivative_Union()
        {
            // ∂(a + b) on 'a' = ε ∨ ∅ = ε
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var d = deriv.Derivative(Ere<Prop>.Union(A, B));
            Assert.That(d.Evaluate(new HashSet<string> { "a" }, reg, eba), Is.EqualTo(Ere<Prop>.Epsilon()));
            Assert.That(d.Evaluate(new HashSet<string> { "b" }, reg, eba), Is.EqualTo(Ere<Prop>.Epsilon()));
            Assert.That(d.Evaluate(new HashSet<string>(), reg, eba), Is.EqualTo(Ere<Prop>.Empty()));
        }
        [Test]
        public void Simplifications_Star_StarConcatStar()
        {
            // R* · R* ≡ R*
            var aStar = Ere<Prop>.Star(A);
            Assert.That(Ere<Prop>.Concat(aStar, aStar), Is.EqualTo(aStar));

            // R* · (R* · X) ≡ R* · X — right-associated form
            var rhs = Ere<Prop>.Concat(aStar, B);
            Assert.That(Ere<Prop>.Concat(aStar, rhs), Is.EqualTo(rhs));

            // Σ* · Σ* ≡ Σ* (special case of the above)
            var sigma = Ere<Prop>.Sigma();
            var sigmaStar = Ere<Prop>.Star(sigma);
            Assert.That(Ere<Prop>.Concat(sigmaStar, sigmaStar), Is.EqualTo(sigmaStar));
        }

        [Test]
        public void Simplifications_Concat_NormalisesToRightAssociated()
        {
            // (a·b)·c, a·(b·c), and the same with deeper left-leaning
            // structures all normalise to the canonical right-associated
            // form via the Concat factory's recursive rewrite
            //   (x·y)·z → x·(y·z)
            // Hash-cons interning then guarantees reference equality, so
            // trivially-equivalent associativity variants collapse to a
            // single Ere node rather than two structurally-distinct atoms.
            var C = Ere<Prop>.Atom(new Prop("c"));
            var D = Ere<Prop>.Atom(new Prop("d"));

            // (a·b)·c  vs  a·(b·c)
            var ab_c = Ere<Prop>.Concat(Ere<Prop>.Concat(A, B), C);
            var a_bc = Ere<Prop>.Concat(A, Ere<Prop>.Concat(B, C));
            Assert.That(ab_c, Is.SameAs(a_bc),
                "(a·b)·c and a·(b·c) must hash-cons to the same instance");

            // (a·b)·(c·d)  vs  a·(b·(c·d))
            var ab_cd = Ere<Prop>.Concat(
                Ere<Prop>.Concat(A, B),
                Ere<Prop>.Concat(C, D));
            var a_b_cd = Ere<Prop>.Concat(A,
                Ere<Prop>.Concat(B, Ere<Prop>.Concat(C, D)));
            Assert.That(ab_cd, Is.SameAs(a_b_cd),
                "(a·b)·(c·d) must collapse to a·(b·(c·d))");

            // Deep left-leaning: ((a·b)·c)·d  vs  a·(b·(c·d))
            var abc_d = Ere<Prop>.Concat(
                Ere<Prop>.Concat(Ere<Prop>.Concat(A, B), C), D);
            Assert.That(abc_d, Is.SameAs(a_b_cd),
                "((a·b)·c)·d must collapse to a·(b·(c·d))");

            // Structural witness: the top-level Left is the atomic A.
            Assert.That(((EreConcat<Prop>)a_b_cd).Left, Is.SameAs(A),
                "right-associated form has an atomic Left at the top");
        }

        [Test]
        public void Simplifications_Union_StarAbsorbs()
        {
            // R + R* ≡ R*
            var aStar = Ere<Prop>.Star(A);
            Assert.That(Ere<Prop>.Union(A, aStar), Is.EqualTo(aStar));

            // ε + R* ≡ R*
            Assert.That(Ere<Prop>.Union(Ere<Prop>.Epsilon(), aStar), Is.EqualTo(aStar));

            // R·R* + R* ≡ R*  (R+ ⊆ R*)
            var rPlus = Ere<Prop>.Concat(A, aStar);
            Assert.That(Ere<Prop>.Union(rPlus, aStar), Is.EqualTo(aStar));

            // Other operands survive the absorption.
            var bStar = Ere<Prop>.Star(B);
            var u = Ere<Prop>.Union(A, Ere<Prop>.Union(aStar, bStar));
            Assert.That(u, Is.EqualTo(Ere<Prop>.Union(aStar, bStar)));
        }

        [Test]
        public void Simplifications_Star_DropsEpsilon()
        {
            // (R + ε)* ≡ R*
            var rPlusEps = Ere<Prop>.Union(A, Ere<Prop>.Epsilon());
            Assert.That(Ere<Prop>.Star(rPlusEps), Is.EqualTo(Ere<Prop>.Star(A)));

            // (R + S + ε)* ≡ (R + S)*
            var union = Ere<Prop>.Union(Ere<Prop>.Union(A, B), Ere<Prop>.Epsilon());
            Assert.That(Ere<Prop>.Star(union), Is.EqualTo(Ere<Prop>.Star(Ere<Prop>.Union(A, B))));
        }

        // ---------- Fusion (Section 7.3, JACM extension) ----------

        [Test]
        public void Fusion_Simplifications_Boundary()
        {
            // ∅ : R = ∅,  R : ∅ = ∅
            Assert.That(Ere<Prop>.Fusion(Ere<Prop>.Empty(), A), Is.EqualTo(Ere<Prop>.Empty()));
            Assert.That(Ere<Prop>.Fusion(A, Ere<Prop>.Empty()), Is.EqualTo(Ere<Prop>.Empty()));
            // ε : R = ∅,  R : ε = ∅ — fusion requires a shared letter, ε has none.
            Assert.That(Ere<Prop>.Fusion(Ere<Prop>.Epsilon(), A), Is.EqualTo(Ere<Prop>.Empty()));
            Assert.That(Ere<Prop>.Fusion(A, Ere<Prop>.Epsilon()), Is.EqualTo(Ere<Prop>.Empty()));
        }

        [Test]
        public void Fusion_Nullable_AlwaysFalse()
        {
            // nullable(R : S) = false   (eq. 31)
            var aStar = Ere<Prop>.Star(A);
            var bStar = Ere<Prop>.Star(B);
            Assert.That(Ere<Prop>.Fusion(aStar, bStar).Nullable, Is.False);
        }

        [Test]
        public void OneStep_BasicCases()
        {
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            // OneStep(∅) = ⊥, OneStep(ε) = ⊥
            Assert.That(deriv.OneStep(Ere<Prop>.Empty()).Name, Is.EqualTo("⊥"));
            Assert.That(deriv.OneStep(Ere<Prop>.Epsilon()).Name, Is.EqualTo("⊥"));
            // OneStep(p) = p
            Assert.That(deriv.OneStep(A).Name, Is.EqualTo("a"));
            // OneStep(R*) = OneStep(R)
            Assert.That(deriv.OneStep(Ere<Prop>.Star(A)).Name, Is.EqualTo("a"));
            // OneStep(R + S) = OneStep(R) ∨ OneStep(S)
            Assert.That(deriv.OneStep(Ere<Prop>.Union(A, B)).Name, Is.EqualTo("(a∨b)"));
            // OneStep(R · S): only the nullable factor's OneStep flows through.
            //   OneStep(a · b) = ⊥ (neither side nullable)
            Assert.That(deriv.OneStep(Ere<Prop>.Concat(A, B)).Name, Is.EqualTo("⊥"));
            //   OneStep(a* · b) = ⊥ ∨ b = b   (a* nullable, b not nullable)
            Assert.That(deriv.OneStep(Ere<Prop>.Concat(Ere<Prop>.Star(A), B)).Name, Is.EqualTo("b"));
            // OneStep(R : S) = OneStep(R) ∧ OneStep(S)
            Assert.That(deriv.OneStep(Ere<Prop>.Fusion(A, B)).Name, Is.EqualTo("(a∧b)"));
        }

        [Test]
        public void Fusion_Derivative_SingleLetter()
        {
            // For atoms a, b: L(a : b) = { v | v=a (length 1, i=0), v[..0]=a∈L(a), v[0..]=a∈L(b) }
            //                          = single-letter words satisfying a ∧ b.
            // Hence ∂(a:b) on { a,b }     = ε  (accepting)
            //       ∂(a:b) on { a }       = ∅
            //       ∂(a:b) on { b }       = ∅
            //       ∂(a:b) on { }         = ∅
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var d = deriv.Derivative(Ere<Prop>.Fusion(A, B));
            Assert.That(d.Evaluate(new HashSet<string> { "a", "b" }, reg, eba), Is.EqualTo(Ere<Prop>.Epsilon()));
            Assert.That(d.Evaluate(new HashSet<string> { "a" }, reg, eba),      Is.EqualTo(Ere<Prop>.Empty()));
            Assert.That(d.Evaluate(new HashSet<string> { "b" }, reg, eba),      Is.EqualTo(Ere<Prop>.Empty()));
            Assert.That(d.Evaluate(new HashSet<string>(),        reg, eba),     Is.EqualTo(Ere<Prop>.Empty()));
        }

        [Test]
        public void Fusion_Derivative_Example7_1_Equivalence()
        {
            // From the JACM ext. Example 7.1:  R = α*:β*  ≡  S = α*·(α∧β)·β*.
            // We check membership equivalence on a few representative words by
            // chaining derivatives (over predicate-atoms α=a, β=b).
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var aStar = Ere<Prop>.Star(A);
            var bStar = Ere<Prop>.Star(B);

            var R = Ere<Prop>.Fusion(aStar, bStar);
            var aAndB = Ere<Prop>.Intersect(A, B);
            var S = Ere<Prop>.Concat(aStar, Ere<Prop>.Concat(aAndB, bStar));

            var ab = new HashSet<string> { "a", "b" };
            var aOnly = new HashSet<string> { "a" };
            var bOnly = new HashSet<string> { "b" };

            // The single letter satisfying both a and b is in both languages.
            // After ∂ on {a,b}, residual is nullable.
            var dR1 = deriv.Derivative(R).Evaluate(ab, reg, eba);
            var dS1 = deriv.Derivative(S).Evaluate(ab, reg, eba);
            Assert.That(dR1.Nullable, Is.True,  "R should accept the singleton {a,b}");
            Assert.That(dS1.Nullable, Is.True,  "S should accept the singleton {a,b}");

            // The word [a,a,{a,b},b] is in both languages (α-prefix, shared α∧β, β-suffix).
            Ere<Prop> rPos = R, sPos = S;
            foreach (var letter in new[] { aOnly, aOnly, ab, bOnly })
            {
                rPos = deriv.Derivative(rPos).Evaluate(letter, reg, eba);
                sPos = deriv.Derivative(sPos).Evaluate(letter, reg, eba);
            }
            Assert.That(rPos.Nullable, Is.True,  "R should accept a·a·(a∧b)·b");
            Assert.That(sPos.Nullable, Is.True,  "S should accept a·a·(a∧b)·b");

            // [a,b] alone (no shared letter) is in neither: the fusion needs one
            // position where both α and β hold.
            var rNeg = deriv.Derivative(deriv.Derivative(R).Evaluate(aOnly, reg, eba))
                            .Evaluate(bOnly, reg, eba);
            var sNeg = deriv.Derivative(deriv.Derivative(S).Evaluate(aOnly, reg, eba))
                            .Evaluate(bOnly, reg, eba);
            Assert.That(rNeg.Nullable, Is.False, "R should reject a·b (no shared letter)");
            Assert.That(sNeg.Nullable, Is.False, "S should reject a·b (no shared letter)");
        }
        [Test]
        public void Fusion_Derivative_Example7_1_LanguageEquivalence()
        {
            // Full language equivalence  α* : β*  ≡  α* · (α∧β) · β*  via mutual
            // subsumption: each language minus the other is empty. We decide
            // emptiness by Brzozowski-derivative state-space exploration —
            // L(X) = ∅  iff  no reachable state of X is nullable.
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var aStar = Ere<Prop>.Star(A);
            var bStar = Ere<Prop>.Star(B);
            var aAndB = Ere<Prop>.Intersect(A, B);

            var R = Ere<Prop>.Fusion(aStar, bStar);                              // α* : β*
            var S = Ere<Prop>.Concat(aStar, Ere<Prop>.Concat(aAndB, bStar));     // α*·(α∧β)·β*

            Assert.That(SubsumedBy(S, R, deriv), Is.True, "L(S) ⊆ L(R)");
            Assert.That(SubsumedBy(R, S, deriv), Is.True, "L(R) ⊆ L(S)");
        }

        [Test]
        public void Xor_Identity_DropsEmpty()
        {
            // R ⊕ ⊥ ≡ R
            Assert.That(Ere<Prop>.Xor(A, Ere<Prop>.Empty()), Is.EqualTo(A));
            Assert.That(Ere<Prop>.Xor(Ere<Prop>.Empty(), B), Is.EqualTo(B));
            Assert.That(Ere<Prop>.Xor(Ere<Prop>.Empty(), Ere<Prop>.Empty()),
                Is.EqualTo(Ere<Prop>.Empty()));
        }

        [Test]
        public void Xor_SelfInverse_PairCancellation()
        {
            // R ⊕ R ≡ ⊥
            Assert.That(Ere<Prop>.Xor(A, A), Is.EqualTo(Ere<Prop>.Empty()));
            // A ⊕ B ⊕ A  ≡  B  (cancellation across nesting)
            var ab = Ere<Prop>.Xor(A, B);
            Assert.That(Ere<Prop>.Xor(ab, A), Is.EqualTo(B));
        }

        [Test]
        public void Xor_ComplementLift_PairCancellation()
        {
            // ~R ⊕ ~S ≡ R ⊕ S
            var notA = Ere<Prop>.Complement(A);
            var notB = Ere<Prop>.Complement(B);
            Assert.That(Ere<Prop>.Xor(notA, notB), Is.EqualTo(Ere<Prop>.Xor(A, B)));

            // ~R ⊕ R ≡ Σ*
            Assert.That(Ere<Prop>.Xor(notA, A), Is.EqualTo(Ere<Prop>.Sigma()));

            // Σ* ⊕ R ≡ ~R
            Assert.That(Ere<Prop>.Xor(Ere<Prop>.Sigma(), A), Is.EqualTo(notA));

            // Σ* ⊕ Σ* ≡ ⊥
            Assert.That(Ere<Prop>.Xor(Ere<Prop>.Sigma(), Ere<Prop>.Sigma()),
                Is.EqualTo(Ere<Prop>.Empty()));
        }

        [Test]
        public void Xor_ComplementLift_SingleOperand()
        {
            // R ⊕ ~S ≡ ~(R ⊕ S) — single XOR node with Negated=true (no wrapper).
            var x = Ere<Prop>.Xor(A, Ere<Prop>.Complement(B));
            Assert.That(x, Is.InstanceOf<EreXor<Prop>>());
            var node = (EreXor<Prop>)x;
            Assert.That(node.Negated, Is.True);
            Assert.That(node.Operands.Count, Is.EqualTo(2));
            // Equivalent to ~(A ⊕ B).
            Assert.That(x, Is.EqualTo(Ere<Prop>.Complement(Ere<Prop>.Xor(A, B))));
        }

        [Test]
        public void Xor_ComplementOfXor_AbsorbedByNegatedFlag()
        {
            // ~(A ⊕ B) is stored as EreXor with Negated=true, not as Complement(Xor).
            var xab = Ere<Prop>.Xor(A, B);
            var nxab = Ere<Prop>.Complement(xab);
            Assert.That(nxab, Is.InstanceOf<EreXor<Prop>>());
            Assert.That(((EreXor<Prop>)nxab).Negated, Is.True);

            // Double complement returns the original (reference-equal via hash-consing).
            Assert.That(Ere<Prop>.Complement(nxab), Is.SameAs(xab));
        }

        [Test]
        public void Xor_Flattening_OperandsSortedAndDistinct()
        {
            // (A ⊕ B) ⊕ (B ⊕ C) = A ⊕ C (pair-cancellation on B).
            var ab = Ere<Prop>.Xor(A, B);
            var C = Ere<Prop>.Atom(new Prop("c"));
            var bc = Ere<Prop>.Xor(B, C);
            Assert.That(Ere<Prop>.Xor(ab, bc), Is.EqualTo(Ere<Prop>.Xor(A, C)));
        }

        [Test]
        public void Xor_Nullable_ParityOfOperands()
        {
            // a (non-nullable) ⊕ a* (nullable) → nullable.
            var x = Ere<Prop>.Xor(A, Ere<Prop>.Star(A));
            Assert.That(x.Nullable, Is.True);

            // a* ⊕ b* (both nullable) → non-nullable.
            var x2 = Ere<Prop>.Xor(Ere<Prop>.Star(A), Ere<Prop>.Star(B));
            Assert.That(x2.Nullable, Is.False);

            // Three-way XNOR: ~(a ⊕ a* ⊕ b*) — parity of (false, true, true)=false,
            // then negated → true.
            var x3 = Ere<Prop>.Xnor(A, Ere<Prop>.Xor(Ere<Prop>.Star(A), Ere<Prop>.Star(B)));
            Assert.That(x3.Nullable, Is.True);
        }

        [Test]
        public void Xnor_IsComplementOfXor()
        {
            Assert.That(Ere<Prop>.Xnor(A, B),
                Is.EqualTo(Ere<Prop>.Complement(Ere<Prop>.Xor(A, B))));
        }

        [Test]
        public void Derivative_Xor_CommutesWithDerivative()
        {
            // δ(A ⊕ B) on input 'a' should match (δA ⊕ δB) on 'a'.
            //   δA on 'a' = ε,  δB on 'a' = ∅  →  ε ⊕ ∅ = ε
            //   δA on 'b' = ∅,  δB on 'b' = ε  →  ∅ ⊕ ε = ε
            //   δ(A⊕B) on neither = ∅ ⊕ ∅ = ∅
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var xab = Ere<Prop>.Xor(A, B);
            var d = deriv.Derivative(xab);
            Assert.That(d.Evaluate(new HashSet<string> { "a" }, reg, eba),
                Is.EqualTo(Ere<Prop>.Epsilon()));
            Assert.That(d.Evaluate(new HashSet<string> { "b" }, reg, eba),
                Is.EqualTo(Ere<Prop>.Epsilon()));
            Assert.That(d.Evaluate(new HashSet<string> { "a", "b" }, reg, eba),
                Is.EqualTo(Ere<Prop>.Empty()));
            Assert.That(d.Evaluate(new HashSet<string>(), reg, eba),
                Is.EqualTo(Ere<Prop>.Empty()));
        }

        [Test]
        public void Derivative_Xnor_NegatesLeaves()
        {
            // δ(A ⊙ B) = ~(δA ⊕ δB). On 'a': ~ε = ~ε (which is non-nullable Σ*\ε).
            // We don't strictly need to validate that exact form; what matters
            // is that the result equals Complement of the XOR derivative.
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var xab = Ere<Prop>.Xor(A, B);
            var xnab = Ere<Prop>.Xnor(A, B);
            var dXor = deriv.Derivative(xab);
            var dXnor = deriv.Derivative(xnab);

            // The two transition terms should be exact negations leaf-wise.
            foreach (var input in new[]
            {
                new HashSet<string> { "a" }, new HashSet<string> { "b" },
                new HashSet<string> { "a", "b" }, new HashSet<string>()
            })
            {
                var l1 = dXor.Evaluate(input, reg, eba);
                var l2 = dXnor.Evaluate(input, reg, eba);
                Assert.That(l2, Is.EqualTo(Ere<Prop>.Complement(l1)),
                    $"On input {string.Join(",", input)}: δ(A⊙B) should be ~δ(A⊕B)");
            }
        }

        [Test]
        public void Derivative_Xor_Self_IsZero()
        {
            // A ⊕ A normalises to ⊥ at construction; derivative is ⊥.
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);

            var xaa = Ere<Prop>.Xor(A, A);
            Assert.That(xaa, Is.EqualTo(Ere<Prop>.Empty()));
            var d = deriv.Derivative(xaa);
            Assert.That(d.Evaluate(new HashSet<string> { "a" }, reg, eba),
                Is.EqualTo(Ere<Prop>.Empty()));
        }

        /// <summary>
        private static bool SubsumedBy(
            Ere<Prop> s, Ere<Prop> r,
            EreDerivative<Prop, HashSet<string>> deriv)
        {
            var diff = Ere<Prop>.Intersect(s, Ere<Prop>.Complement(r));
            return IsEmpty(diff, deriv);
        }

        private static bool IsEmpty(
            Ere<Prop> regex,
            EreDerivative<Prop, HashSet<string>> deriv,
            int stateLimit = 200)
        {
            var seen = new HashSet<Ere<Prop>> { regex };
            var work = new Queue<Ere<Prop>>();
            work.Enqueue(regex);
            while (work.Count > 0)
            {
                var q = work.Dequeue();
                if (q.Nullable) return false;          // accepting state reached
                if (q is EreEmpty<Prop>) continue;     // dead, no successors
                var d = deriv.Derivative(q);
                foreach (var next in CollectLeaves(d))
                {
                    if (next is EreEmpty<Prop>) continue;
                    if (seen.Add(next))
                    {
                        if (seen.Count > stateLimit)
                            throw new Exception(
                                $"State-space explosion (> {stateLimit}); refusing to enumerate further.");
                        work.Enqueue(next);
                    }
                }
            }
            return true;
        }

        private static IEnumerable<Ere<Prop>> CollectLeaves(TransitionTerm<Ere<Prop>> t)
        {
            if (t is TransitionTermLeaf<Ere<Prop>> leaf) { yield return leaf.Value; yield break; }
            var ite = (TransitionTermIte<Ere<Prop>>)t;
            foreach (var l in CollectLeaves(ite.Hi)) yield return l;
            foreach (var l in CollectLeaves(ite.Lo)) yield return l;
        }
    }
}
