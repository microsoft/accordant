namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for LTL JSON serialization, incremental Æ, and correctness validation
    /// comparing batch vs incremental alternation elimination.
    /// </summary>
    [TestFixture]
    public class LtlSerializationAndAETests
    {
        private static readonly LtlAlgebra<string> SAlg =
            new LtlAlgebra<string>(StringFreeAlgebra.Instance);

        #region JSON Serialization Tests

        [Test]
        public void Json_RoundTrip_True()
        {
            var f = Ltl<string>.True();
            var json = LtlJson.Serialize(f);
            Assert.That(json, Is.EqualTo("{\"op\":\"True\"}"));
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
        }

        [Test]
        public void Json_RoundTrip_False()
        {
            var f = Ltl<string>.False();
            var json = LtlJson.Serialize(f);
            Assert.That(json, Is.EqualTo("{\"op\":\"False\"}"));
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
        }

        [Test]
        public void Json_RoundTrip_Atom()
        {
            var f = Ltl<string>.Atom("request");
            var json = LtlJson.Serialize(f);
            Assert.That(json, Does.Contain("\"Atom\""));
            Assert.That(json, Does.Contain("\"request\""));
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
        }

        [Test]
        public void Json_RoundTrip_NegAtom()
        {
            var f = SAlg.NegAtom("busy");
            var json = LtlJson.Serialize(f);
            // With EBA fusion, NegAtom("busy") => Atom(StringFreeAlgebra.Not("busy")) = Atom("¬busy")
            Assert.That(json, Does.Contain("\"Atom\""));
            Assert.That(json, Does.Contain("¬busy"));
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
        }

        [Test]
        public void Json_RoundTrip_Next()
        {
            var f = Ltl<string>.Next(Ltl<string>.Atom("a"));
            var json = LtlJson.Serialize(f);
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
        }

        [Test]
        public void Json_RoundTrip_Until()
        {
            var f = Ltl<string>.Until(Ltl<string>.Atom("a"), Ltl<string>.Atom("b"));
            var json = LtlJson.Serialize(f);
            Assert.That(json, Does.Contain("\"Until\""));
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
        }

        [Test]
        public void Json_RoundTrip_Release()
        {
            var f = Ltl<string>.Release(Ltl<string>.Atom("a"), Ltl<string>.Atom("b"));
            var json = LtlJson.Serialize(f);
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
        }

        [Test]
        public void Json_RoundTrip_And()
        {
            var f = SAlg.And(Ltl<string>.Atom("a"), Ltl<string>.Atom("b"));
            var json = LtlJson.Serialize(f);
            // With EBA fusion over StringFreeAlgebra, And(Atom("a"),Atom("b"))
            // collapses to a single Atom("(a ∧ b)").
            Assert.That(json, Does.Contain("\"Atom\""));
            Assert.That(json, Does.Contain("∧"));
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
        }

        [Test]
        public void Json_RoundTrip_Or()
        {
            var f = SAlg.Or(Ltl<string>.Atom("a"), Ltl<string>.Atom("b"));
            var json = LtlJson.Serialize(f);
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
        }

        [Test]
        public void Json_RoundTrip_ComplexFormula()
        {
            // G(a → F b)
            var a = Ltl<string>.Atom("a");
            var b = Ltl<string>.Atom("b");
            var formula = Ltl<string>.Globally(SAlg.Implies(a, Ltl<string>.Eventually(b)));
            var json = LtlJson.Serialize(formula);
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(formula));
        }

        [Test]
        public void Json_Deserialize_Sugar_Eventually()
        {
            var json = "{\"op\":\"Eventually\",\"inner\":{\"op\":\"Atom\",\"pred\":\"a\"}}";
            var f = LtlJson.Deserialize(json);
            var expected = Ltl<string>.Eventually(Ltl<string>.Atom("a"));
            Assert.That(f, Is.EqualTo(expected));
        }

        [Test]
        public void Json_Deserialize_Sugar_Globally()
        {
            var json = "{\"op\":\"Globally\",\"inner\":{\"op\":\"Atom\",\"pred\":\"a\"}}";
            var f = LtlJson.Deserialize(json);
            var expected = Ltl<string>.Globally(Ltl<string>.Atom("a"));
            Assert.That(f, Is.EqualTo(expected));
        }

        [Test]
        public void Json_Deserialize_Sugar_Implies()
        {
            var json = "{\"op\":\"Implies\",\"left\":{\"op\":\"Atom\",\"pred\":\"a\"},\"right\":{\"op\":\"Atom\",\"pred\":\"b\"}}";
            var f = LtlJson.Deserialize(json);
            var expected = SAlg.Implies(Ltl<string>.Atom("a"), Ltl<string>.Atom("b"));
            Assert.That(f, Is.EqualTo(expected));
        }

        [Test]
        public void Json_SpecialChars_InPredicate()
        {
            var f = Ltl<string>.Atom("x > 0");
            var json = LtlJson.Serialize(f);
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(f));
            Assert.That(((LtlAtom<string>)f2).Predicate, Is.EqualTo("x > 0"));
        }

        [Test]
        public void Json_NestedFormula_GFaAndGFb()
        {
            // GFa ∧ GFb
            var a = Ltl<string>.Atom("a");
            var b = Ltl<string>.Atom("b");
            var formula = SAlg.And(
                Ltl<string>.Globally(Ltl<string>.Eventually(a)),
                Ltl<string>.Globally(Ltl<string>.Eventually(b)));
            var json = LtlJson.Serialize(formula);
            var f2 = LtlJson.Deserialize(json);
            Assert.That(f2, Is.EqualTo(formula));
        }

        #endregion

        #region Incremental Æ Tests

        // Simple proposition EBA for testing
        private sealed class Prop : IEquatable<Prop>
        {
            public string Name { get; }
            public Prop(string name) { Name = name; }
            public override string ToString() => Name;
            public override int GetHashCode() => Name.GetHashCode();
            public override bool Equals(object obj) => Equals(obj as Prop);
            public bool Equals(Prop other) => other != null && Name == other.Name;
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

            public bool IsSatisfiable(Prop predicate) => predicate.Name != "⊥";

            public bool Models(HashSet<string> element, Prop predicate)
            {
                if (predicate.Name == "⊤") return true;
                if (predicate.Name == "⊥") return false;
                if (predicate.Name.StartsWith("¬"))
                    return !element.Contains(predicate.Name.Substring(1));
                return element.Contains(predicate.Name);
            }
        }

        private PropEba _eba;
        private LtlAlgebra<Prop> _alg;
        private ConditionRegistry<Prop> _registry;

        [SetUp]
        public void Setup()
        {
            _eba = new PropEba();
            _alg = new LtlAlgebra<Prop>(_eba);
            _registry = new ConditionRegistry<Prop>();
        }

        private LtlDerivative<Prop, HashSet<string>> MakeDerivative()
            => new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);

        [Test]
        public void IncrementalAE_Fa_ProducesNBW()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(new Prop("a"));
            var fa = Ltl<Prop>.Eventually(a);
            var abw = d.ToABW(fa);

            var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(abw);
            var nbw = incAE.ToNBW();

            Assert.That(nbw.InitialStates.Count, Is.EqualTo(1));
            Assert.That(incAE.IsAccepting(incAE.InitialState), Is.True,
                "Initial state has empty obligation, so it's accepting");

            // Get transition for initial state — this triggers lazy computation
            var trans = nbw.GetTransition(incAE.InitialState);
            Assert.That(trans, Is.Not.Null);
            Assert.That(trans.Count, Is.GreaterThan(0));
            Assert.That(incAE.ComputedStateCount, Is.EqualTo(1));
        }

        [Test]
        public void IncrementalAE_Ga_ProducesNBW()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(new Prop("a"));
            var ga = Ltl<Prop>.Globally(a);
            var abw = d.ToABW(ga);

            var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(abw);
            var nbw = incAE.ToNBW();

            var trans = nbw.GetTransition(incAE.InitialState);
            Assert.That(trans.Count, Is.GreaterThan(0));
        }

        [Test]
        public void IncrementalAE_LazyComputation_OnlyExploredStates()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(new Prop("a"));
            var b = Ltl<Prop>.Atom(new Prop("b"));
            var formula = Ltl<Prop>.Globally(_alg.Implies(a, Ltl<Prop>.Eventually(b)));
            var abw = d.ToABW(formula);

            var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(abw);
            var nbw = incAE.ToNBW();

            // Before any transitions are requested, nothing is computed
            Assert.That(incAE.ComputedStateCount, Is.EqualTo(0));

            // Get initial transition
            nbw.GetTransition(incAE.InitialState);
            Assert.That(incAE.ComputedStateCount, Is.EqualTo(1));

            // Exploring more states should increase the count
            var explored = AlternationElimination.Explore(nbw, maxStates: 10);
            Assert.That(incAE.ComputedStateCount, Is.GreaterThan(1));
        }

        [Test]
        public void IncrementalAE_MatchesBatch_Fa()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(new Prop("a"));
            var fa = Ltl<Prop>.Eventually(a);
            var abw = d.ToABW(fa);

            // Batch
            var batchNbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw);
            var batchStates = AlternationElimination.Explore(batchNbw);

            // Need fresh ABW for incremental (registries shared but ABW caches are independent)
            var d2 = new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);
            var abw2 = d2.ToABW(fa);
            var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(abw2);
            var incNbw = incAE.ToNBW();
            var incStates = AlternationElimination.Explore(incNbw);

            // Both should discover the same number of states
            Assert.That(incStates.Count, Is.EqualTo(batchStates.Count),
                "Incremental and batch Æ should produce same number of reachable states");
        }

        [Test]
        public void IncrementalAE_MatchesBatch_Ga()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(new Prop("a"));
            var ga = Ltl<Prop>.Globally(a);
            var abw = d.ToABW(ga);

            var batchNbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw);
            var batchStates = AlternationElimination.Explore(batchNbw);

            var d2 = new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);
            var abw2 = d2.ToABW(ga);
            var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(abw2);
            var incNbw = incAE.ToNBW();
            var incStates = AlternationElimination.Explore(incNbw);

            Assert.That(incStates.Count, Is.EqualTo(batchStates.Count));
        }

        [Test]
        public void IncrementalAE_MatchesBatch_GFa()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(new Prop("a"));
            var gfa = Ltl<Prop>.Globally(Ltl<Prop>.Eventually(a));
            var abw = d.ToABW(gfa);

            var batchNbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw);
            var batchStates = AlternationElimination.Explore(batchNbw);

            var d2 = new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);
            var abw2 = d2.ToABW(gfa);
            var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(abw2);
            var incNbw = incAE.ToNBW();
            var incStates = AlternationElimination.Explore(incNbw);

            Assert.That(incStates.Count, Is.EqualTo(batchStates.Count));
        }

        // ----- DnfLeaves knob: eagerAntimirov=false on batch and incremental -----

        [Test]
        public void DnfLeaves_BatchAndIncremental_MatchEagerStateCount_OnGFAndFairnessFormulas()
        {
            var a = Ltl<Prop>.Atom(new Prop("a"));
            var b = Ltl<Prop>.Atom(new Prop("b"));
            var c = Ltl<Prop>.Atom(new Prop("c"));
            var formulas = new (string name, Ltl<Prop> phi)[]
            {
                ("Fa",          Ltl<Prop>.Eventually(a)),
                ("Ga",          Ltl<Prop>.Globally(a)),
                ("GFa",         Ltl<Prop>.Globally(Ltl<Prop>.Eventually(a))),
                ("G(a->Fb)",    Ltl<Prop>.Globally(_alg.Implies(a, Ltl<Prop>.Eventually(b)))),
                ("GFa & GFb",   _alg.And(
                                    Ltl<Prop>.Globally(Ltl<Prop>.Eventually(a)),
                                    Ltl<Prop>.Globally(Ltl<Prop>.Eventually(b)))),
                ("GFa&GFb&GFc", _alg.And(_alg.And(
                                    Ltl<Prop>.Globally(Ltl<Prop>.Eventually(a)),
                                    Ltl<Prop>.Globally(Ltl<Prop>.Eventually(b))),
                                    Ltl<Prop>.Globally(Ltl<Prop>.Eventually(c)))),
            };

            foreach (var (name, phi) in formulas)
            {
                var dE = new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);
                var eagerBatch = AlternationElimination.Explore(
                    AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(
                        dE.ToABW(phi), eagerAntimirov: true)).Count;

                var dD = new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);
                var dnfBatch = AlternationElimination.Explore(
                    AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(
                        dD.ToABW(phi), eagerAntimirov: false)).Count;

                var dI = new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);
                var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(
                    dI.ToABW(phi), eagerAntimirov: false);
                var dnfInc = AlternationElimination.Explore(incAE.ToNBW()).Count;

                Assert.That(dnfBatch, Is.EqualTo(eagerBatch),
                    $"DnfLeaves batch should match eager batch on {name}");
                Assert.That(dnfInc, Is.EqualTo(eagerBatch),
                    $"DnfLeaves incremental should match eager batch on {name}");
            }
        }

        [Test]
        public void IncrementalAE_MatchesBatch_G_AImplFb()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(new Prop("a"));
            var b = Ltl<Prop>.Atom(new Prop("b"));
            var formula = Ltl<Prop>.Globally(_alg.Implies(a, Ltl<Prop>.Eventually(b)));
            var abw = d.ToABW(formula);

            var batchNbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw);
            var batchStates = AlternationElimination.Explore(batchNbw);

            var d2 = new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);
            var abw2 = d2.ToABW(formula);
            var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(abw2);
            var incNbw = incAE.ToNBW();
            var incStates = AlternationElimination.Explore(incNbw);

            Assert.That(incStates.Count, Is.EqualTo(batchStates.Count));
        }

        [Test]
        public void IncrementalAE_AcceptingStates_Consistent()
        {
            var d = MakeDerivative();
            var a = Ltl<Prop>.Atom(new Prop("a"));
            var fa = Ltl<Prop>.Eventually(a);
            var abw = d.ToABW(fa);

            var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(abw);
            var nbw = incAE.ToNBW();
            var states = AlternationElimination.Explore(nbw);

            // Verify accepting states have empty obligation
            foreach (var s in states)
            {
                bool acc = nbw.IsAccepting(s);
                Assert.That(acc, Is.EqualTo(s.Obligation.IsEmpty),
                    $"State {s}: IsAccepting={acc} but O.IsEmpty={s.Obligation.IsEmpty}");
            }
        }

        #endregion

        #region Cross-validation: Batch vs Incremental accepting state agreement

        [Test]
        public void CrossValidation_AcceptingStatesMatch_Fa()
        {
            ValidateAcceptingStatesMatch(
                Ltl<Prop>.Eventually(Ltl<Prop>.Atom(new Prop("a"))));
        }

        [Test]
        public void CrossValidation_AcceptingStatesMatch_GFa()
        {
            ValidateAcceptingStatesMatch(
                Ltl<Prop>.Globally(Ltl<Prop>.Eventually(Ltl<Prop>.Atom(new Prop("a")))));
        }

        [Test]
        public void CrossValidation_AcceptingStatesMatch_aUb()
        {
            ValidateAcceptingStatesMatch(
                Ltl<Prop>.Until(
                    Ltl<Prop>.Atom(new Prop("a")),
                    Ltl<Prop>.Atom(new Prop("b"))));
        }

        private void ValidateAcceptingStatesMatch(Ltl<Prop> formula)
        {
            var d1 = new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);
            var abw1 = d1.ToABW(formula);
            var batchNbw = AlternationElimination.Eliminate<Prop, HashSet<string>, Ltl<Prop>>(abw1);
            var batchStates = AlternationElimination.Explore(batchNbw);
            int batchAccepting = batchStates.Count(s => batchNbw.IsAccepting(s));

            var d2 = new LtlDerivative<Prop, HashSet<string>>(_eba, _registry);
            var abw2 = d2.ToABW(formula);
            var incAE = new IncrementalAE<Prop, HashSet<string>, Ltl<Prop>>(abw2);
            var incNbw = incAE.ToNBW();
            var incStates = AlternationElimination.Explore(incNbw);
            int incAccepting = incStates.Count(s => incNbw.IsAccepting(s));

            Assert.That(incAccepting, Is.EqualTo(batchAccepting),
                $"Formula {formula}: batch has {batchAccepting} accepting, incremental has {incAccepting}");
        }

        #endregion
    }
}
