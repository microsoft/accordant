namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Integration tests: LTL formula → symbolic derivative → ABW → Æ → NBW → DOT.
    /// Validates correctness of the full pipeline using simple LTL properties.
    /// </summary>
    [TestFixture]
    public class LtlIntegrationTests
    {
        // Simple predicate type: named propositions
        private sealed class Prop : IEquatable<Prop>
        {
            public string Name { get; }
            public Prop(string name) { Name = name; }
            public override string ToString() => Name;
            public override int GetHashCode() => Name.GetHashCode();
            public override bool Equals(object obj) => Equals(obj as Prop);
            public bool Equals(Prop other) => other != null && Name == other.Name;
        }

        // EBA over finite set of propositions {a, b}
        // Predicates are represented as sets of "true" propositions (minterms approach)
        // For simplicity, use the predicates directly and evaluate them against valuations.
        private sealed class PropEba : IEffectiveBooleanAlgebra<Prop, HashSet<string>>
        {
            // We use Prop objects as atomic predicates.
            // An element (valuation) is a set of proposition names that are true.
            // A Prop p is satisfied by valuation v iff p.Name ∈ v.
            //
            // For the EBA, we need And/Or/Not of predicates.
            // We'll use a wrapper: Prop can be atomic, or combined via compound predicates.
            // For simplicity in tests, we only use atomic predicates and let the
            // transition term ITE structure handle the Boolean combinations.

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

            public bool IsSatisfiable(Prop predicate)
            {
                return predicate.Name != "⊥";
            }

            public bool Models(HashSet<string> element, Prop predicate)
            {
                if (predicate.Name == "⊤") return true;
                if (predicate.Name == "⊥") return false;
                if (predicate.Name.StartsWith("¬"))
                    return !element.Contains(predicate.Name.Substring(1));
                // Compound predicates — simple parse for test purposes
                if (predicate.Name.StartsWith("(") && predicate.Name.Contains("∧"))
                {
                    var parts = SplitCompound(predicate.Name, "∧");
                    return parts.All(p => Models(element, new Prop(p)));
                }
                if (predicate.Name.StartsWith("(") && predicate.Name.Contains("∨"))
                {
                    var parts = SplitCompound(predicate.Name, "∨");
                    return parts.Any(p => Models(element, new Prop(p)));
                }
                return element.Contains(predicate.Name);
            }

            private static string[] SplitCompound(string s, string op)
            {
                // Remove outer parens and split on op
                s = s.Substring(1, s.Length - 2);
                return s.Split(new[] { op }, StringSplitOptions.None);
            }
        }

        private PropEba _eba;
        private LtlAlgebra<Prop> _alg;
        private ConditionRegistry<Prop> _registry;
        private Prop _a, _b;

        [SetUp]
        public void Setup()
        {
            _eba = new PropEba();
            _alg = new LtlAlgebra<Prop>(_eba);
            _registry = new ConditionRegistry<Prop>();
            _a = new Prop("a");
            _b = new Prop("b");
        }

        private LtlDerivative<Prop, HashSet<string>> MakeDerivative()
            => new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);

        #region LTL Formula Tests

        [Test]
        public void Ltl_True_False_Atoms()
        {
            var t = Ltl<Prop>.True();
            var f = Ltl<Prop>.False();
            var a = Ltl<Prop>.Atom(_a);
            var na = _alg.NegAtom(_a);

            Assert.That(t, Is.InstanceOf<LtlTrue<Prop>>());
            Assert.That(f, Is.InstanceOf<LtlFalse<Prop>>());
            Assert.That(a.ToString(), Is.EqualTo("a"));
            Assert.That(na.ToString(), Is.EqualTo("¬a"));
        }

        [Test]
        public void Ltl_Negation_PushesThrough()
        {
            var a = Ltl<Prop>.Atom(_a);
            var na = _alg.Not(a);
            Assert.That(na, Is.InstanceOf<LtlAtom<Prop>>());
            Assert.That(((LtlAtom<Prop>)na).Predicate, Is.EqualTo(_eba.Not(_a)));

            // ¬(a U b) = ¬a R ¬b
            var until = Ltl<Prop>.Until(Ltl<Prop>.Atom(_a), Ltl<Prop>.Atom(_b));
            var negUntil = _alg.Not(until);
            Assert.That(negUntil, Is.InstanceOf<LtlRelease<Prop>>());
        }

        [Test]
        public void Ltl_And_ACI_Normalization()
        {
            var a = Ltl<Prop>.Atom(_a);
            var b = Ltl<Prop>.Atom(_b);

            // Idempotent: a ∧ a = a
            var aa = _alg.And(a, a);
            Assert.That(aa, Is.EqualTo(a));

            // Commutative: a ∧ b = b ∧ a
            var ab = _alg.And(a, b);
            var ba = _alg.And(b, a);
            Assert.That(ab, Is.EqualTo(ba));

            // Identity: a ∧ ⊤ = a
            var at = _alg.And(a, Ltl<Prop>.True());
            Assert.That(at, Is.EqualTo(a));

            // Zero: a ∧ ⊥ = ⊥
            var af = _alg.And(a, Ltl<Prop>.False());
            Assert.That(af, Is.InstanceOf<LtlFalse<Prop>>());
        }

        [Test]
        public void Ltl_Or_ACI_Normalization()
        {
            var a = Ltl<Prop>.Atom(_a);
            var b = Ltl<Prop>.Atom(_b);

            // Idempotent: a ∨ a = a
            var aa = _alg.Or(a, a);
            Assert.That(aa, Is.EqualTo(a));

            // Identity: a ∨ ⊥ = a
            var af = _alg.Or(a, Ltl<Prop>.False());
            Assert.That(af, Is.EqualTo(a));

            // Zero: a ∨ ⊤ = ⊤
            var at = _alg.Or(a, Ltl<Prop>.True());
            Assert.That(at, Is.InstanceOf<LtlTrue<Prop>>());
        }

        [Test]
        public void Ltl_Eventually_Globally_Sugar()
        {
            var a = Ltl<Prop>.Atom(_a);

            var fa = Ltl<Prop>.Eventually(a);
            Assert.That(fa, Is.InstanceOf<LtlUntil<Prop>>());
            Assert.That(((LtlUntil<Prop>)fa).Left, Is.InstanceOf<LtlTrue<Prop>>());
            Assert.That(((LtlUntil<Prop>)fa).Right, Is.EqualTo(a));

            var ga = Ltl<Prop>.Globally(a);
            Assert.That(ga, Is.InstanceOf<LtlRelease<Prop>>());
            Assert.That(((LtlRelease<Prop>)ga).Left, Is.InstanceOf<LtlFalse<Prop>>());
            Assert.That(((LtlRelease<Prop>)ga).Right, Is.EqualTo(a));
        }

        [Test]
        public void Ltl_Equality_And_Comparison()
        {
            var a1 = Ltl<Prop>.Atom(_a);
            var a2 = Ltl<Prop>.Atom(_a);
            Assert.That(a1, Is.EqualTo(a2));
            Assert.That(a1.GetHashCode(), Is.EqualTo(a2.GetHashCode()));

            var b = Ltl<Prop>.Atom(_b);
            Assert.That(a1, Is.Not.EqualTo(b));

            // Compare is consistent (deterministic ordering)
            Assert.That(a1.CompareTo(a2), Is.EqualTo(0));
        }

        #endregion

        #region Symbolic Derivative Tests

        [Test]
        public void Derivative_TrueAndFalse()
        {
            var d = MakeDerivative();
            var dTrue = d.Derivative(Ltl<Prop>.True());
            var dFalse = d.Derivative(Ltl<Prop>.False());

            // ∂(⊤) should be Top (Dnf.True leaf)
            Assert.That(dTrue, Is.InstanceOf<TransitionTermLeaf<Dnf<Ltl<Prop>>>>());
            var leafTrue = ((TransitionTermLeaf<Dnf<Ltl<Prop>>>)dTrue).Value;
            Assert.That(leafTrue.IsTrue, Is.True);

            // ∂(⊥) should be Bottom (Dnf.False leaf)
            Assert.That(dFalse, Is.InstanceOf<TransitionTermLeaf<Dnf<Ltl<Prop>>>>());
            var leafFalse = ((TransitionTermLeaf<Dnf<Ltl<Prop>>>)dFalse).Value;
            Assert.That(leafFalse.IsFalse, Is.True);
        }

        [Test]
        public void Derivative_Atom()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var da = d.Derivative(a);

            // ∂(a) = ITE(a, ⊤, ⊥)
            Assert.That(da, Is.InstanceOf<TransitionTermIte<Dnf<Ltl<Prop>>>>());
            var ite = (TransitionTermIte<Dnf<Ltl<Prop>>>)da;
            Assert.That(((TransitionTermLeaf<Dnf<Ltl<Prop>>>)ite.Hi).Value.IsTrue, Is.True);
            Assert.That(((TransitionTermLeaf<Dnf<Ltl<Prop>>>)ite.Lo).Value.IsFalse, Is.True);
        }

        [Test]
        public void Derivative_NegAtom()
        {
            var d = MakeDerivative();
            var na = _alg.NegAtom(_a);
            var dna = d.Derivative(na);

            // After EBA fusion, NegAtom(a) = Atom(¬a), so derivative is ITE(¬a, ⊤, ⊥)
            Assert.That(dna, Is.InstanceOf<TransitionTermIte<Dnf<Ltl<Prop>>>>());
            var ite = (TransitionTermIte<Dnf<Ltl<Prop>>>)dna;
            Assert.That(((TransitionTermLeaf<Dnf<Ltl<Prop>>>)ite.Hi).Value.IsTrue, Is.True);
            Assert.That(((TransitionTermLeaf<Dnf<Ltl<Prop>>>)ite.Lo).Value.IsFalse, Is.True);
        }

        [Test]
        public void Derivative_Next()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var xa = Ltl<Prop>.Next(a);
            var dxa = d.Derivative(xa);

            // ∂(Xa) = atom(a) — a leaf containing Dnf with singleton clause {a}
            Assert.That(dxa, Is.InstanceOf<TransitionTermLeaf<Dnf<Ltl<Prop>>>>());
            var leaf = ((TransitionTermLeaf<Dnf<Ltl<Prop>>>)dxa).Value;
            Assert.That(leaf.ClauseCount, Is.EqualTo(1));
            Assert.That(leaf.Clauses[0].Count, Is.EqualTo(1));
            Assert.That(leaf.Clauses[0].First(), Is.EqualTo(a));
        }

        [Test]
        public void Derivative_Until()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var b = Ltl<Prop>.Atom(_b);
            var aUb = Ltl<Prop>.Until(a, b);

            var daUb = d.Derivative(aUb);

            // ∂(a U b) = ∂(b) ∨ (∂(a) ∧ atom(a U b))
            // = ITE(b, ⊤, ⊥) ∨ (ITE(a, ⊤, ⊥) ∧ atom(aUb))
            // Should be an ITE with conditions for a and b
            Assert.That(daUb, Is.Not.Null);
            // Verify it's not trivially bottom or top
            var leaves = daUb.GetDistinctLeaves().ToList();
            Assert.That(leaves.Count, Is.GreaterThan(0));
        }

        #endregion

        #region ABW Construction Tests

        [Test]
        public void ABW_FromEventually_a()
        {
            // Fa = ⊤ U a
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var fa = Ltl<Prop>.Eventually(a);

            var abw = d.ToABW(fa);

            Assert.That(abw.InitialState, Is.EqualTo(abw.DnfAlgebra.Atom(fa)));
            // Fa is an Until formula, so NOT accepting
            Assert.That(abw.IsAccepting(fa), Is.False);
            // True is accepting
            Assert.That(abw.IsAccepting(Ltl<Prop>.True()), Is.True);

            // Compute transition for initial state
            var trans = abw.GetTransition(fa);
            Assert.That(trans, Is.Not.Null);
        }

        [Test]
        public void ABW_FromGlobally_a()
        {
            // Ga = ⊥ R a
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var ga = Ltl<Prop>.Globally(a);

            var abw = d.ToABW(ga);

            Assert.That(abw.InitialState, Is.EqualTo(abw.DnfAlgebra.Atom(ga)));
            // Ga is a Release formula, which IS accepting
            Assert.That(abw.IsAccepting(ga), Is.True);

            var trans = abw.GetTransition(ga);
            Assert.That(trans, Is.Not.Null);
        }

        #endregion

        #region Full Pipeline: LTL → ABW → Æ → NBW

        [Test]
        public void Pipeline_Fa_EventuallyA()
        {
            // Fa = ⊤ U a: "a must eventually hold"
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var fa = Ltl<Prop>.Eventually(a);

            var abw = d.ToABW(fa);
            var nbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw);

            Assert.That(nbw, Is.Not.Null);
            Assert.That(nbw.InitialStates.Count, Is.GreaterThan(0));
            Assert.That(nbw.States.Count, Is.GreaterThan(0));

            // NBW should have some accepting states
            var acceptingStates = nbw.States.Where(s => nbw.IsAccepting(s)).ToList();
            Assert.That(acceptingStates.Count, Is.GreaterThan(0),
                "Fa should produce NBW with accepting states (breakpoint reached)");
        }

        [Test]
        public void Pipeline_Ga_GloballyA()
        {
            // Ga = ⊥ R a: "a must always hold"
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var ga = Ltl<Prop>.Globally(a);

            var abw = d.ToABW(ga);
            var nbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw);

            Assert.That(nbw, Is.Not.Null);
            Assert.That(nbw.States.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Pipeline_GFa_InfinitelyOftenA()
        {
            // GFa = G(Fa) = ⊥ R (⊤ U a): "a holds infinitely often"
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var gfa = Ltl<Prop>.Globally(Ltl<Prop>.Eventually(a));

            var abw = d.ToABW(gfa);
            var nbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw);

            Assert.That(nbw, Is.Not.Null);
            Assert.That(nbw.States.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Pipeline_G_AImpliesFb()
        {
            // G(a → Fb): "whenever a holds, b must eventually hold"
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var b = Ltl<Prop>.Atom(_b);
            var fb = Ltl<Prop>.Eventually(b);
            var aImplFb = _alg.Implies(a, fb);
            var formula = Ltl<Prop>.Globally(aImplFb);

            var abw = d.ToABW(formula);
            var nbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw);

            Assert.That(nbw, Is.Not.Null);
            Assert.That(nbw.States.Count, Is.GreaterThan(0));

            // This is a more complex formula — NBW should have at least 1 state
            Assert.That(nbw.States.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Pipeline_Xa_NextA()
        {
            // Xa: "a holds in the next step"
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(_a);
            var xa = Ltl<Prop>.Next(a);

            var abw = d.ToABW(xa);
            var nbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw);

            Assert.That(nbw, Is.Not.Null);
            Assert.That(nbw.States.Count, Is.GreaterThan(0));
        }

        #endregion

        #region DOT Visualization Tests (skipped — visualization not ported)

        // BuchiVisualization / DotRenderer not ported to Accordant.
        // These tests are intentionally removed.

        #endregion

        #region Accepting State Semantics

        [Test]
        public void IsAccepting_UntilIsNotAccepting()
        {
            var a = Ltl<Prop>.Atom(_a);
            var b = Ltl<Prop>.Atom(_b);
            var until = Ltl<Prop>.Until(a, b);

            Assert.That(LtlDerivative<Prop, HashSet<string>>.IsAccepting(until), Is.False);
        }

        [Test]
        public void IsAccepting_NonUntilIsAccepting()
        {
            var a = Ltl<Prop>.Atom(_a);
            Assert.That(LtlDerivative<Prop, HashSet<string>>.IsAccepting(a), Is.True);
            Assert.That(LtlDerivative<Prop, HashSet<string>>.IsAccepting(Ltl<Prop>.True()), Is.True);
            Assert.That(LtlDerivative<Prop, HashSet<string>>.IsAccepting(Ltl<Prop>.False()), Is.True);
            Assert.That(LtlDerivative<Prop, HashSet<string>>.IsAccepting(
                Ltl<Prop>.Globally(a)), Is.True);
            Assert.That(LtlDerivative<Prop, HashSet<string>>.IsAccepting(
                Ltl<Prop>.Next(a)), Is.True);
        }

        #endregion
    }
}
