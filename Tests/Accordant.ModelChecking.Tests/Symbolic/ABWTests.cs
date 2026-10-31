namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    [TestFixture]
    public class StateSetTests
    {
        private static readonly IComparer<int> Cmp = Comparer<int>.Default;

        [Test]
        public void Empty_HasCountZero()
        {
            var s = StateSet<int>.Empty(Cmp);
            Assert.AreEqual(0, s.Count);
            Assert.IsTrue(s.IsEmpty);
        }

        [Test]
        public void Singleton_HasCountOne()
        {
            var s = StateSet<int>.Singleton(42, Cmp);
            Assert.AreEqual(1, s.Count);
            Assert.IsTrue(s.Contains(42));
            Assert.IsFalse(s.Contains(0));
        }

        [Test]
        public void Constructor_SortsAndDeduplicates()
        {
            var s = new StateSet<int>(new[] { 3, 1, 2, 1, 3 }, Cmp);
            Assert.AreEqual(3, s.Count);
            Assert.AreEqual(1, s[0]);
            Assert.AreEqual(2, s[1]);
            Assert.AreEqual(3, s[2]);
        }

        [Test]
        public void Union_MergesSortedSets()
        {
            var a = new StateSet<int>(new[] { 1, 3, 5 }, Cmp);
            var b = new StateSet<int>(new[] { 2, 3, 4 }, Cmp);
            var u = a.Union(b);
            Assert.AreEqual(5, u.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, u.ToArray());
        }

        [Test]
        public void Intersect_FindsCommonElements()
        {
            var a = new StateSet<int>(new[] { 1, 2, 3, 4 }, Cmp);
            var b = new StateSet<int>(new[] { 2, 4, 6 }, Cmp);
            var i = a.Intersect(b);
            CollectionAssert.AreEqual(new[] { 2, 4 }, i.ToArray());
        }

        [Test]
        public void Except_RemovesElements()
        {
            var a = new StateSet<int>(new[] { 1, 2, 3, 4 }, Cmp);
            var b = new StateSet<int>(new[] { 2, 4 }, Cmp);
            var e = a.Except(b);
            CollectionAssert.AreEqual(new[] { 1, 3 }, e.ToArray());
        }

        [Test]
        public void IsSubsetOf_Works()
        {
            var a = new StateSet<int>(new[] { 2, 3 }, Cmp);
            var b = new StateSet<int>(new[] { 1, 2, 3, 4 }, Cmp);
            Assert.IsTrue(a.IsSubsetOf(b));
            Assert.IsFalse(b.IsSubsetOf(a));
            Assert.IsTrue(a.IsSubsetOf(a)); // reflexive
        }

        [Test]
        public void IsProperSubsetOf_ExcludesEqual()
        {
            var a = new StateSet<int>(new[] { 1, 2 }, Cmp);
            Assert.IsFalse(a.IsProperSubsetOf(a));
            Assert.IsTrue(a.IsProperSubsetOf(new StateSet<int>(new[] { 1, 2, 3 }, Cmp)));
        }

        [Test]
        public void Equality_Structural()
        {
            var a = new StateSet<int>(new[] { 1, 2, 3 }, Cmp);
            var b = new StateSet<int>(new[] { 3, 1, 2 }, Cmp);
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void CompareTo_LexicographicShorterFirst()
        {
            var a = new StateSet<int>(new[] { 1, 2 }, Cmp);
            var b = new StateSet<int>(new[] { 1, 2, 3 }, Cmp);
            Assert.IsTrue(a.CompareTo(b) < 0);
        }

        [Test]
        public void CompareTo_LexicographicSameLength()
        {
            var a = new StateSet<int>(new[] { 1, 3 }, Cmp);
            var b = new StateSet<int>(new[] { 1, 4 }, Cmp);
            Assert.IsTrue(a.CompareTo(b) < 0);
        }
    }

    [TestFixture]
    public class DnfTests
    {
        private static readonly IComparer<int> Cmp = Comparer<int>.Default;
        private DnfAlgebra<int> _alg;

        [SetUp]
        public void Setup()
        {
            _alg = new DnfAlgebra<int>(Cmp);
        }

        [Test]
        public void Top_HasOneEmptyClause()
        {
            var top = _alg.Top;
            Assert.IsTrue(top.IsTrue);
            Assert.IsFalse(top.IsFalse);
            Assert.AreEqual(1, top.ClauseCount);
            Assert.AreEqual(0, top.Clauses[0].Count);
        }

        [Test]
        public void Bottom_HasNoClauses()
        {
            var bot = _alg.Bottom;
            Assert.IsTrue(bot.IsFalse);
            Assert.IsFalse(bot.IsTrue);
            Assert.AreEqual(0, bot.ClauseCount);
        }

        [Test]
        public void Atom_SingleStateClause()
        {
            var a = _alg.Atom(5);
            Assert.AreEqual(1, a.ClauseCount);
            Assert.AreEqual(1, a.Clauses[0].Count);
            Assert.IsTrue(a.Clauses[0].Contains(5));
        }

        [Test]
        public void Or_UnionOfClauses()
        {
            var a = _alg.Atom(1);  // { {1} }
            var b = _alg.Atom(2);  // { {2} }
            var r = _alg.Or(a, b); // { {1}, {2} }
            Assert.AreEqual(2, r.ClauseCount);
        }

        [Test]
        public void Or_WithBottom_IsIdentity()
        {
            var a = _alg.Atom(1);
            Assert.AreEqual(a, _alg.Or(a, _alg.Bottom));
            Assert.AreEqual(a, _alg.Or(_alg.Bottom, a));
        }

        [Test]
        public void Or_WithTop_IsTop()
        {
            var a = _alg.Atom(1);
            Assert.IsTrue(_alg.Or(a, _alg.Top).IsTrue);
            Assert.IsTrue(_alg.Or(_alg.Top, a).IsTrue);
        }

        [Test]
        public void And_CrossProduct()
        {
            var a = _alg.Atom(1);  // { {1} }
            var b = _alg.Atom(2);  // { {2} }
            var r = _alg.And(a, b); // { {1,2} }
            Assert.AreEqual(1, r.ClauseCount);
            Assert.AreEqual(2, r.Clauses[0].Count);
            Assert.IsTrue(r.Clauses[0].Contains(1));
            Assert.IsTrue(r.Clauses[0].Contains(2));
        }

        [Test]
        public void And_WithTop_IsIdentity()
        {
            var a = _alg.Atom(1);
            Assert.AreEqual(a, _alg.And(a, _alg.Top));
            Assert.AreEqual(a, _alg.And(_alg.Top, a));
        }

        [Test]
        public void And_WithBottom_IsBottom()
        {
            var a = _alg.Atom(1);
            Assert.IsTrue(_alg.And(a, _alg.Bottom).IsFalse);
        }

        [Test]
        public void And_DistributesOverOr()
        {
            // (1 ∨ 2) ∧ 3 = (1∧3) ∨ (2∧3)
            var oneOrTwo = _alg.Or(_alg.Atom(1), _alg.Atom(2)); // { {1}, {2} }
            var three = _alg.Atom(3);                             // { {3} }
            var r = _alg.And(oneOrTwo, three);                    // { {1,3}, {2,3} }
            Assert.AreEqual(2, r.ClauseCount);
        }

        [Test]
        public void Subsumption_RemovesSupersets()
        {
            // {1} subsumes {1,2}: if {1} is enough, {1,2} is redundant
            var a = _alg.Atom(1);                                // { {1} }
            var b = _alg.Clause(new[] { 1, 2 });                 // { {1,2} }
            var r = _alg.Or(a, b);                               // { {1} }
            Assert.AreEqual(1, r.ClauseCount);
            Assert.AreEqual(1, r.Clauses[0].Count);
        }

        [Test]
        public void Subsumption_InAndResult()
        {
            // (1 ∨ 2) ∧ (1 ∨ 3) = {1} ∨ {1,3} ∨ {1,2} ∨ {2,3}
            // After subsumption: {1} subsumes {1,3} and {1,2}, so result = {1} ∨ {2,3}
            var a = _alg.Or(_alg.Atom(1), _alg.Atom(2));
            var b = _alg.Or(_alg.Atom(1), _alg.Atom(3));
            var r = _alg.And(a, b);
            Assert.AreEqual(2, r.ClauseCount);
            // Clauses should be {1} and {2,3}
            Assert.IsTrue(r.Clauses.Any(c => c.Count == 1 && c.Contains(1)));
            Assert.IsTrue(r.Clauses.Any(c => c.Count == 2 && c.Contains(2) && c.Contains(3)));
        }

        [Test]
        public void Equality_StructuralAcrossConstruction()
        {
            var a = _alg.And(_alg.Atom(1), _alg.Atom(2)); // { {1,2} }
            var b = _alg.Clause(new[] { 2, 1 });            // { {1,2} } (reordered)
            Assert.AreEqual(a, b);
        }

        [Test]
        public void Not_Throws()
        {
            Assert.Throws<NotSupportedException>(() => _alg.Not(_alg.Atom(1)));
        }

        [Test]
        public void GetAllStates_ReturnsDistinctStates()
        {
            var formula = _alg.Or(
                _alg.Clause(new[] { 1, 2 }),
                _alg.Clause(new[] { 2, 3 }));
            var states = new HashSet<int>(formula.GetAllStates());
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, states);
        }
    }

    [TestFixture]
    public class SymbolicABWTests
    {
        private IntEba _eba;
        private ConditionRegistry<IntPredicate> _registry;
        private DnfAlgebra<string> _dnfAlgebra;

        [SetUp]
        public void Setup()
        {
            _eba = new IntEba(3); // universe {0,1,2}
            _registry = new ConditionRegistry<IntPredicate>();
            _dnfAlgebra = new DnfAlgebra<string>(StringComparer.Ordinal);
        }

        [Test]
        public void BasicABW_CreationAndTransition()
        {
            // Simple ABW with 2 states: "p" (initial), "q"
            // δ(p) = (α ? {q} : ⊥)  — read α, go to q
            // δ(q) = ⊤               — accepting sink
            var alpha = _registry.Register(new IntPredicate("α", 0, 1));

            TransitionTerm<Dnf<string>> Delta(string state)
            {
                switch (state)
                {
                    case "p":
                        return TransitionTerm<Dnf<string>>.Ite(
                            alpha,
                            TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("q")),
                            TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Bottom));
                    case "q":
                        return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);
                    default:
                        throw new ArgumentException($"Unknown state: {state}");
                }
            }

            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra,
                "p", s => s == "q", Delta);

            // Check initial state
            Assert.AreEqual(_dnfAlgebra.Atom("p"), abw.InitialState);
            Assert.IsTrue(abw.States.Contains("p"));

            // Get transition for p — should discover q
            var deltaP = abw.GetTransition("p");
            Assert.IsTrue(abw.States.Contains("q"));

            // Evaluate for element 0 (in α): should give Dnf({q})
            var leafAt0 = deltaP.Evaluate(0, _registry, _eba);
            Assert.IsFalse(leafAt0.IsFalse);
            Assert.AreEqual(1, leafAt0.ClauseCount);
            Assert.IsTrue(leafAt0.Clauses[0].Contains("q"));

            // Evaluate for element 2 (not in α): should give ⊥
            var leafAt2 = deltaP.Evaluate(2, _registry, _eba);
            Assert.IsTrue(leafAt2.IsFalse);
        }

        [Test]
        public void ABW_AlternatingTransition()
        {
            // ABW with alternation: δ(s) = {p} ∧ {q} = { {p,q} }
            // Both p AND q must hold in successor
            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra,
                "s",
                s => false,
                s =>
                {
                    if (s == "s")
                        return TransitionTerm<Dnf<string>>.Leaf(
                            _dnfAlgebra.And(_dnfAlgebra.Atom("p"), _dnfAlgebra.Atom("q")));
                    return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);
                });

            var delta = abw.GetTransition("s");
            var leaf = ((TransitionTermLeaf<Dnf<string>>)delta).Value;
            Assert.AreEqual(1, leaf.ClauseCount);
            Assert.AreEqual(2, leaf.Clauses[0].Count); // {p, q}
        }

        [Test]
        public void ABW_GetTermAlgebra_CombinesTransitions()
        {
            // Two states with transitions, combine with And
            var alpha = _registry.Register(new IntPredicate("α", 0));

            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra,
                "init",
                s => false,
                s =>
                {
                    switch (s)
                    {
                        case "init":
                            return TransitionTerm<Dnf<string>>.Leaf(
                                _dnfAlgebra.And(_dnfAlgebra.Atom("a"), _dnfAlgebra.Atom("b")));
                        case "a":
                            return TransitionTerm<Dnf<string>>.Ite(alpha,
                                TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("a")),
                                TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Bottom));
                        case "b":
                            return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);
                        default:
                            return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Bottom);
                    }
                });

            var alg = abw.GetTermAlgebra();
            var deltaA = abw.GetTransition("a");
            var deltaB = abw.GetTransition("b");

            // Combined: δ(a) ∧ δ(b)
            var combined = alg.And(deltaA, deltaB);

            // For element 0 (α=true): {a} ∧ ⊤ = {a}
            var leafAt0 = combined.Evaluate(0, _registry, _eba);
            Assert.AreEqual(1, leafAt0.ClauseCount);
            Assert.IsTrue(leafAt0.Clauses[0].Contains("a"));

            // For element 1 (α=false): ⊥ ∧ ⊤ = ⊥
            var leafAt1 = combined.Evaluate(1, _registry, _eba);
            Assert.IsTrue(leafAt1.IsFalse);
        }
    }

    [TestFixture]
    public class AlternationEliminationTests
    {
        private IntEba _eba;
        private ConditionRegistry<IntPredicate> _registry;
        private DnfAlgebra<string> _dnfAlgebra;

        [SetUp]
        public void Setup()
        {
            _eba = new IntEba(2); // universe {0, 1}
            _registry = new ConditionRegistry<IntPredicate>();
            _dnfAlgebra = new DnfAlgebra<string>(StringComparer.Ordinal);
        }

        [Test]
        public void Eliminate_SimpleNondeterministic()
        {
            // ABW that is already nondeterministic (no alternation):
            // States: "p" (initial, accepting), "q"
            // δ(p) = (α ? {p} ∨ {q} : {p})  — on α, go to p or q; else stay in p
            // δ(q) = {q}                      — self-loop
            var alpha = _registry.Register(new IntPredicate("α", 0));

            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra,
                "p",
                s => s == "p",
                s =>
                {
                    switch (s)
                    {
                        case "p":
                            var pOrQ = _dnfAlgebra.Or(_dnfAlgebra.Atom("p"), _dnfAlgebra.Atom("q"));
                            var justP = _dnfAlgebra.Atom("p");
                            return TransitionTerm<Dnf<string>>.Ite(alpha,
                                TransitionTerm<Dnf<string>>.Leaf(pOrQ),
                                TransitionTerm<Dnf<string>>.Leaf(justP));
                        case "q":
                            return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("q"));
                        default:
                            return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Bottom);
                    }
                });

            var nbw = AlternationElimination.Eliminate<IntPredicate, int, string>(abw);

            // NBW initial state should be ({p}, ∅)
            Assert.AreEqual(1, nbw.InitialStates.Count);
            var init = nbw.InitialStates[0];
            Assert.AreEqual(1, init.Macrostate.Count);
            Assert.IsTrue(init.Macrostate.Contains("p"));
            Assert.IsTrue(init.Obligation.IsEmpty);

            // Initial state is accepting (O = ∅)
            Assert.IsTrue(nbw.IsAccepting(init));

            // Explore the NBW
            var states = AlternationElimination.Explore<IntPredicate, int, BreakpointState<string>>(nbw, maxStates: 20);
            Assert.IsTrue(states.Count > 0);
        }

        [Test]
        public void Eliminate_WithAlternation()
        {
            // ABW with true alternation:
            // States: "s" (initial), "a", "b"
            // δ(s) = {a} ∧ {b} = { {a,b} }  — both a AND b must hold
            // δ(a) = ⊤  (accepting sink)
            // δ(b) = ⊤  (accepting sink)
            // F = {a, b}
            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra,
                "s",
                s => s == "a" || s == "b",
                s =>
                {
                    if (s == "s")
                        return TransitionTerm<Dnf<string>>.Leaf(
                            _dnfAlgebra.And(_dnfAlgebra.Atom("a"), _dnfAlgebra.Atom("b")));
                    // a and b are accepting sinks
                    return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);
                });

            var nbw = AlternationElimination.Eliminate<IntPredicate, int, string>(abw);

            // Explore
            var states = AlternationElimination.Explore<IntPredicate, int, BreakpointState<string>>(nbw, maxStates: 20);
            Assert.IsTrue(states.Count > 0);

            // The initial transition should produce macrostate {a,b}
            var initTransitions = nbw.GetTransition(nbw.InitialStates[0]);
            Assert.IsTrue(initTransitions.Count > 0);
        }

        [Test]
        public void Eliminate_AcceptingCondition()
        {
            // ABW: single accepting state with self-loop
            // δ(p) = {p}, F = {p}
            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra,
                "p",
                s => s == "p",
                s => TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("p")));

            var nbw = AlternationElimination.Eliminate<IntPredicate, int, string>(abw);

            // Initial: ({p}, ∅) — accepting since O=∅
            Assert.IsTrue(nbw.IsAccepting(nbw.InitialStates[0]));

            // Get transitions and explore
            var states = AlternationElimination.Explore<IntPredicate, int, BreakpointState<string>>(nbw, maxStates: 20);

            // Should have breakpoint states
            bool hasAccepting = false;
            foreach (BreakpointState<string> s in states)
            {
                if (nbw.IsAccepting(s)) hasAccepting = true;
            }
            Assert.IsTrue(hasAccepting, "Should have accepting states");
            // For a single accepting self-loop, all reachable states have O=∅
            // because after reset O = S = {p}, then O' = O \ F = {p}\{p} = ∅
        }
    }

    [TestFixture]
    public class SymbolicABWGeneralizedInitialTests
    {
        private IntEba _eba;
        private ConditionRegistry<IntPredicate> _registry;
        private DnfAlgebra<string> _dnfAlgebra;

        [SetUp]
        public void Setup()
        {
            _eba = new IntEba(3);
            _registry = new ConditionRegistry<IntPredicate>();
            _dnfAlgebra = new DnfAlgebra<string>(StringComparer.Ordinal);
        }

        // Single accepting sink on every state.
        private TransitionTerm<Dnf<string>> SinkDelta(string s)
            => TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);

        [Test]
        public void DnfInitial_SeedsAllMentionedStates()
        {
            // φ₀ = {p} ∨ ({q} ∧ {r})  → all of p, q, r should be seeded.
            var initial = _dnfAlgebra.Or(
                _dnfAlgebra.Atom("p"),
                _dnfAlgebra.And(_dnfAlgebra.Atom("q"), _dnfAlgebra.Atom("r")));
            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, initial, _ => true, SinkDelta);

            Assert.AreEqual(initial, abw.InitialState);
            Assert.IsTrue(abw.States.Contains("p"));
            Assert.IsTrue(abw.States.Contains("q"));
            Assert.IsTrue(abw.States.Contains("r"));
        }

        [Test]
        public void AtomConstructor_WrapsAsSingletonDnf()
        {
            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "p", _ => true, SinkDelta);
            Assert.AreEqual(_dnfAlgebra.Atom("p"), abw.InitialState);
        }

        [Test]
        public void GetTransitionOnDnf_TrueAndFalse_AreLeafTopBottom()
        {
            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "p", _ => true, SinkDelta);

            var top = abw.GetTransition(_dnfAlgebra.Top);
            var bot = abw.GetTransition(_dnfAlgebra.Bottom);

            Assert.IsInstanceOf<TransitionTermLeaf<Dnf<string>>>(top);
            Assert.IsInstanceOf<TransitionTermLeaf<Dnf<string>>>(bot);
            Assert.IsTrue(((TransitionTermLeaf<Dnf<string>>)top).Value.IsTrue);
            Assert.IsTrue(((TransitionTermLeaf<Dnf<string>>)bot).Value.IsFalse);
        }

        [Test]
        public void GetTransitionOnDnf_AtomMatchesAtomicDelta()
        {
            var alpha = _registry.Register(new IntPredicate("α", 0, 1));
            TransitionTerm<Dnf<string>> Delta(string s) =>
                s == "p"
                    ? TransitionTerm<Dnf<string>>.Ite(alpha,
                        TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("q")),
                        TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Bottom))
                    : TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);

            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "p", s => s == "q", Delta);

            var atomic = abw.GetTransition("p");
            var lifted = abw.GetTransition(_dnfAlgebra.Atom("p"));

            // Same Dnf leaves on both paths for every element.
            for (int e = 0; e < 3; e++)
                Assert.AreEqual(atomic.Evaluate(e, _registry, _eba),
                                lifted.Evaluate(e, _registry, _eba));
        }

        [Test]
        public void GetTransitionOnDnf_DisjunctionLiftsToOr()
        {
            // δ(p) leaf {q}, δ(r) leaf {s}, so δ̂(p ∨ r) leaf = {q} ∨ {s}.
            TransitionTerm<Dnf<string>> Delta(string s)
            {
                if (s == "p") return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("q"));
                if (s == "r") return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("s"));
                return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);
            }
            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "p", _ => false, Delta);

            var lifted = abw.GetTransition(
                _dnfAlgebra.Or(_dnfAlgebra.Atom("p"), _dnfAlgebra.Atom("r")));

            var leaf = lifted.Evaluate(0, _registry, _eba);
            Assert.AreEqual(
                _dnfAlgebra.Or(_dnfAlgebra.Atom("q"), _dnfAlgebra.Atom("s")),
                leaf);
        }

        [Test]
        public void GetTransitionOnDnf_ConjunctionLiftsToAnd()
        {
            TransitionTerm<Dnf<string>> Delta(string s)
            {
                if (s == "p") return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("q"));
                if (s == "r") return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("s"));
                return TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);
            }
            var abw = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "p", _ => false, Delta);

            var lifted = abw.GetTransition(
                _dnfAlgebra.And(_dnfAlgebra.Atom("p"), _dnfAlgebra.Atom("r")));

            var leaf = lifted.Evaluate(0, _registry, _eba);
            // {q} ∧ {s} = { {q,s} }
            Assert.AreEqual(1, leaf.ClauseCount);
            Assert.IsTrue(leaf.Clauses[0].Contains("q"));
            Assert.IsTrue(leaf.Clauses[0].Contains("s"));
        }

        [Test]
        public void Union_InitialIsDisjunctionOfTheTwoInitials()
        {
            var a = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "a0", s => s == "a0", SinkDelta);
            var b = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "b0", s => s == "b0", SinkDelta);

            var u = SymbolicABW<IntPredicate, int, string>.Union(
                a, b, s => s.StartsWith("a"));

            Assert.AreEqual(
                _dnfAlgebra.Or(_dnfAlgebra.Atom("a0"), _dnfAlgebra.Atom("b0")),
                u.InitialState);
            Assert.IsTrue(u.IsAccepting("a0"));
            Assert.IsTrue(u.IsAccepting("b0"));
            Assert.IsFalse(u.IsAccepting("other"));
        }

        [Test]
        public void Intersect_InitialIsConjunctionOfTheTwoInitials()
        {
            var a = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "a0", s => s == "a0", SinkDelta);
            var b = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "b0", s => s == "b0", SinkDelta);

            var x = SymbolicABW<IntPredicate, int, string>.Intersect(
                a, b, s => s.StartsWith("a"));

            // {a0} ∧ {b0} = { {a0, b0} }
            Assert.AreEqual(1, x.InitialState.ClauseCount);
            Assert.IsTrue(x.InitialState.Clauses[0].Contains("a0"));
            Assert.IsTrue(x.InitialState.Clauses[0].Contains("b0"));
        }

        [Test]
        public void Union_DispatchesDeltaByIsInA()
        {
            // Distinct deltas: a routes "a0" → atom("aSucc"); b routes "b0" → atom("bSucc").
            TransitionTerm<Dnf<string>> DeltaA(string s) =>
                s == "a0"
                    ? TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("aSucc"))
                    : TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);
            TransitionTerm<Dnf<string>> DeltaB(string s) =>
                s == "b0"
                    ? TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Atom("bSucc"))
                    : TransitionTerm<Dnf<string>>.Leaf(_dnfAlgebra.Top);

            var a = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "a0", _ => false, DeltaA);
            var b = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "b0", _ => false, DeltaB);

            var u = SymbolicABW<IntPredicate, int, string>.Union(
                a, b, s => s.StartsWith("a"));

            var ta = u.GetTransition("a0").Evaluate(0, _registry, _eba);
            var tb = u.GetTransition("b0").Evaluate(0, _registry, _eba);
            Assert.AreEqual(_dnfAlgebra.Atom("aSucc"), ta);
            Assert.AreEqual(_dnfAlgebra.Atom("bSucc"), tb);
        }

        [Test]
        public void Union_IncompatibleEba_Throws()
        {
            var a = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "a0", _ => true, SinkDelta);
            var otherEba = new IntEba(3);
            var b = new SymbolicABW<IntPredicate, int, string>(
                otherEba, _registry, _dnfAlgebra, "b0", _ => true, SinkDelta);
            Assert.Throws<ArgumentException>(() =>
                SymbolicABW<IntPredicate, int, string>.Union(a, b, _ => true));
        }

        [Test]
        public void Union_NullDiscriminator_Throws()
        {
            var a = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "a0", _ => true, SinkDelta);
            var b = new SymbolicABW<IntPredicate, int, string>(
                _eba, _registry, _dnfAlgebra, "b0", _ => true, SinkDelta);
            Assert.Throws<ArgumentNullException>(() =>
                SymbolicABW<IntPredicate, int, string>.Union(a, b, null));
        }
    }
}
