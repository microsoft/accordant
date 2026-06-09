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
    /// End-to-end tests for <see cref="SymbolicRltlCheck"/>. Verify that the
    /// RLTL pipeline (RLTL → ABW → NBW → NDFS) gives the expected verdicts
    /// on small hand-built state graphs, and that on the LTL subset it agrees
    /// with <see cref="SymbolicLtlCheck.CheckNDFS"/>.
    /// </summary>
    [TestFixture]
    public class SymbolicRltlCheckTests
    {
        #region Test Infrastructure (mirrors SymbolicLtlCheckTests)

        private sealed class TestState : State
        {
            public string Label { get; set; }
            public int Value { get; set; }

            public TestState(string label, int value = 0)
            {
                Label = label;
                Value = value;
            }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
            {
                clonedMap[this] = new TestState(Label, Value);
            }

            protected override void LockComponents(HashSet<object> visited) { }

            protected override string StringRepresentationInternal(Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"{Label}({Value})";
            protected override void FreezeComponents(HashSet<object> visited) { }
        }

        private sealed class TestStepFunction : IStepFunction
        {
            public string StepFunctionId { get; }
            public int StepFunctionIdHash { get; }

            public TestStepFunction(string id)
            {
                StepFunctionId = id;
                StepFunctionIdHash = id.GetHashCode();
            }

            public IList<StepResult> Apply(IState state,
                IReadOnlyList<(IStepFunction, StateGraphNode)> path) => null;
        }

        private static StateGraphNode MakeNode(TestState state, params string[] sfIds)
        {
            state.Freeze();
            return new StateGraphNode
            {
                State = state,
                StepFunctions = sfIds.Select(id => (IStepFunction)new TestStepFunction(id)).ToList(),
                Edges = new List<StateGraphEdge>()
            };
        }

        private static void AddEdge(StateGraphNode from, StateGraphNode to, string sfId = "step")
        {
            from.Edges.Add(new StateGraphEdge
            {
                Target = to,
                StepFunction = new TestStepFunction(sfId)
            });
        }

        private static StateProp Prop(string name, Func<IState, bool> eval)
            => new StateProp(name, eval);

        // --- RLTL constructors -------------------------------------------

        private static Rltl<IStatePredicate> RAtom(StateProp p)
            => Rltl<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Rltl<IStatePredicate> RG(Rltl<IStatePredicate> f)
            => Rltl<IStatePredicate>.Globally(f);

        private static Rltl<IStatePredicate> RF(Rltl<IStatePredicate> f)
            => Rltl<IStatePredicate>.Eventually(f);

        private static Rltl<IStatePredicate> RImplies(Rltl<IStatePredicate> a, Rltl<IStatePredicate> b)
            => RltlAlgebra.Default.Implies(a, b);

        private static Ere<IStatePredicate> ESigma() => Ere<IStatePredicate>.Sigma();
        private static Ere<IStatePredicate> EStar(Ere<IStatePredicate> r) => Ere<IStatePredicate>.Star(r);
        private static Ere<IStatePredicate> EAtom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));
        private static Ere<IStatePredicate> EConcat(Ere<IStatePredicate> a, Ere<IStatePredicate> b)
            => Ere<IStatePredicate>.Concat(a, b);

        // --- LTL constructors (for cross-checking the LTL subset) --------

        private static Ltl<IStatePredicate> LAtom(StateProp p)
            => Ltl<IStatePredicate>.Atom(new StatePredAtom(p));
        private static Ltl<IStatePredicate> LG(Ltl<IStatePredicate> f)
            => Ltl<IStatePredicate>.Globally(f);
        private static Ltl<IStatePredicate> LF(Ltl<IStatePredicate> f)
            => Ltl<IStatePredicate>.Eventually(f);

        #endregion

        #region LTL subset — agreement with SymbolicLtlCheck

        [Test]
        public void Rltl_Ga_Satisfied()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 1));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("v>0", s => ((TestState)s).Value > 0);
            var rltl = SymbolicRltlCheck.Check(s0, RG(RAtom(p)));
            var ltl = SymbolicLtlCheck.CheckNDFS(s0, LG(LAtom(p)));

            Assert.That(rltl.Valid, Is.True);
            Assert.That(rltl.Valid, Is.EqualTo(ltl.Valid));
        }

        [Test]
        public void Rltl_Ga_Violated_HasCycleTrace()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("v>0", s => ((TestState)s).Value > 0);
            var result = SymbolicRltlCheck.Check(s0, RG(RAtom(p)));

            Assert.That(result.Valid, Is.False);
            Assert.That(result.Trace, Is.Not.Null);
            Assert.That(result.Trace.Any(t => t.IsInCycle), Is.True);
        }

        [Test]
        public void Rltl_Fa_Satisfied()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            var s1 = MakeNode(new TestState("s1", 99));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("g", s => ((TestState)s).Value == 99);
            var result = SymbolicRltlCheck.Check(s0, RF(RAtom(p)));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void Rltl_Fa_Violated()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            var s1 = MakeNode(new TestState("s1", 1));
            AddEdge(s0, s1);
            AddEdge(s1, s0);

            var p = Prop("g", s => ((TestState)s).Value == 99);
            var result = SymbolicRltlCheck.Check(s0, RF(RAtom(p)));
            Assert.That(result.Valid, Is.False);
        }

        [Test]
        public void Rltl_GFa_Satisfied()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s0);

            var p = Prop("a", s => ((TestState)s).Value == 1);
            var result = SymbolicRltlCheck.Check(s0, RG(RF(RAtom(p))));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void Rltl_GFa_Violated()
        {
            var s0 = MakeNode(new TestState("s0", 1));
            var s1 = MakeNode(new TestState("s1", 0));
            AddEdge(s0, s1);
            AddEdge(s1, s1);

            var p = Prop("a", s => ((TestState)s).Value == 1);
            var result = SymbolicRltlCheck.Check(s0, RG(RF(RAtom(p))));
            Assert.That(result.Valid, Is.False);
        }

        #endregion

        #region Regex-prefix operators

        /// <summary>Σ* ⊳ p ≡ G p — the safety reading of the trigger operator.</summary>
        [Test]
        public void Rltl_TriggerSigmaStar_p_Equivalent_To_Gp()
        {
            // Satisfied case: all states have val>0.
            var sat0 = MakeNode(new TestState("s0", 1));
            var sat1 = MakeNode(new TestState("s1", 2));
            AddEdge(sat0, sat1); AddEdge(sat1, sat1);

            // Violated case: cycle visiting val=0.
            var vio0 = MakeNode(new TestState("v0", 1));
            var vio1 = MakeNode(new TestState("v1", 0));
            AddEdge(vio0, vio1); AddEdge(vio1, vio1);

            var p = Prop("v>0", s => ((TestState)s).Value > 0);
            var triggerForm = Rltl<IStatePredicate>.Trigger(ESigma(), RAtom(p));

            var satResult = SymbolicRltlCheck.Check(sat0, triggerForm);
            var vioResult = SymbolicRltlCheck.Check(vio0, triggerForm);

            Assert.That(satResult.Valid, Is.True, "Σ*⊳p should hold when p holds globally");
            Assert.That(vioResult.Valid, Is.False, "Σ*⊳p should fail in a cycle visiting ¬p");
        }

        /// <summary>Σ* ; p ≡ F p — the liveness reading of the seq-prefix operator.</summary>
        [Test]
        public void Rltl_SeqPrefixSigmaStar_p_Equivalent_To_Fp()
        {
            // Satisfied case: reaches goal.
            var sat0 = MakeNode(new TestState("s0", 0));
            var sat1 = MakeNode(new TestState("s1", 99));
            AddEdge(sat0, sat1); AddEdge(sat1, sat1);

            // Violated case: cycle without goal.
            var vio0 = MakeNode(new TestState("v0", 0));
            var vio1 = MakeNode(new TestState("v1", 1));
            AddEdge(vio0, vio1); AddEdge(vio1, vio0);

            var p = Prop("g", s => ((TestState)s).Value == 99);
            var seqForm = Rltl<IStatePredicate>.SeqPrefix(ESigma(), RAtom(p));

            var satResult = SymbolicRltlCheck.Check(sat0, seqForm);
            var vioResult = SymbolicRltlCheck.Check(vio0, seqForm);

            Assert.That(satResult.Valid, Is.True, "Σ*;p should hold when goal is reached");
            Assert.That(vioResult.Valid, Is.False, "Σ*;p should fail in a goal-free cycle");
        }

        /// <summary>
        /// A genuinely regex-shaped property with no direct LTL equivalent:
        /// <c>(p·q)* ⊳ r</c> — for every position k that is reached after a
        /// finite alternating sequence p,q,p,q,… (an even-length match of
        /// <c>(p·q)*</c>), the suffix at k must satisfy r. We exercise both
        /// the satisfied and violated branches. Predicates are encoded as
        /// bits of <c>Value</c> so a single state can satisfy p, q, r
        /// independently.
        /// </summary>
        [Test]
        public void Rltl_PqStar_Trigger_r_RegexShape()
        {
            var p = Prop("p", s => (((TestState)s).Value & 1) != 0);
            var q = Prop("q", s => (((TestState)s).Value & 2) != 0);
            var r = Prop("r", s => (((TestState)s).Value & 4) != 0);

            var phi = Rltl<IStatePredicate>.Trigger(
                EStar(EConcat(EAtom(p), EAtom(q))),
                RAtom(r));

            // Satisfied: a single state with r set (val=4) and a self-loop.
            // The only prefix in L((p·q)*) is ε ⇒ r must hold at pos 0 (it does).
            // No state ever satisfies p, so no longer prefix matches.
            var sat0 = MakeNode(new TestState("sat0", 4));
            AddEdge(sat0, sat0);

            var satResult = SymbolicRltlCheck.Check(sat0, phi);
            Assert.That(satResult.Valid, Is.True,
                "Only ε matches (p·q)*; r holds at pos 0 ⇒ property holds");

            // Violated: v0 satisfies p AND r (val=5), v1 satisfies q (val=2),
            // v2 satisfies nothing (val=0) and self-loops.
            //   pos 0: prefix ε ⇒ need r at v0 — holds (bit 2 of 5 is set)
            //   pos 2: prefix w[0..2] = (p,q) ∈ L((p·q)*) ⇒ need r at v2 — FAILS
            var v0 = MakeNode(new TestState("v0", 5)); // p ∧ r
            var v1 = MakeNode(new TestState("v1", 2)); // q
            var v2 = MakeNode(new TestState("v2", 0)); // ¬p ¬q ¬r
            AddEdge(v0, v1); AddEdge(v1, v2); AddEdge(v2, v2);

            var vioResult = SymbolicRltlCheck.Check(v0, phi);
            Assert.That(vioResult.Valid, Is.False,
                "(p·q)*⊳r should fail: after the (p,q) match at positions 0..1, r does not hold at position 2");
        }

        #endregion

        #region Edge cases

        [Test]
        public void Rltl_True_AlwaysHolds()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            AddEdge(s0, s0);
            var result = SymbolicRltlCheck.Check(s0, Rltl<IStatePredicate>.True());
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void Rltl_False_AlwaysFails()
        {
            var s0 = MakeNode(new TestState("s0", 0));
            AddEdge(s0, s0);
            var result = SymbolicRltlCheck.Check(s0, Rltl<IStatePredicate>.False());
            Assert.That(result.Valid, Is.False);
        }

        [Test]
        public void Rltl_BoundedDepth_NoViolationWithinBound()
        {
            var nodes = new List<StateGraphNode>();
            for (int i = 0; i < 8; i++)
                nodes.Add(MakeNode(new TestState($"s{i}", i < 5 ? 1 : 0)));
            for (int i = 0; i < nodes.Count - 1; i++)
                AddEdge(nodes[i], nodes[i + 1]);
            AddEdge(nodes[^1], nodes[^1]);

            var p = Prop("v>0", s => ((TestState)s).Value > 0);
            var result = SymbolicRltlCheck.Check(
                nodes[0],
                Rltl<IStatePredicate>.Trigger(ESigma(), RAtom(p)),
                maxDepth: 3);
            Assert.That(result.Valid, Is.True);
        }

        #endregion

        #region Regex language equivalence via RLTL emptiness

        /// <summary>
        /// Full language equivalence  α* : β*  ≡  α* · (α ∧ β) · β*  decided
        /// through the RLTL model-checking pipeline rather than ad-hoc
        /// derivative exploration. Strategy:
        /// <list type="bullet">
        ///   <item>Build a <i>chaos</i> state graph over the 2-atom alphabet
        ///   {a,b} — one node per truth assignment, every node reachable from
        ///   every other. Any infinite word over Σ = 2^{a,b} is realised.</item>
        ///   <item>Regex emptiness over that universal language is then
        ///   exactly RLTL property  ¬(R ; ⊤)  holding on the chaos graph
        ///   (no run can have any prefix in L(R)).</item>
        ///   <item>L(R) = L(S)  iff  both  R ∩ ¬S  and  S ∩ ¬R  are empty.</item>
        /// </list>
        /// </summary>
        [Test]
        public void Rltl_Fusion_Example7_1_LanguageEquivalence_ViaEmptinessCheck()
        {
            // --- Chaos state graph over {a, b} ---------------------------
            // States are indexed by 2 bits: bit 0 = a, bit 1 = b.
            var chaos = new StateGraphNode[4];
            for (int i = 0; i < 4; i++)
                chaos[i] = MakeNode(new TestState($"ab={i:b2}", i));
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    AddEdge(chaos[i], chaos[j]);

            var a = Prop("a", s => (((TestState)s).Value & 1) != 0);
            var b = Prop("b", s => (((TestState)s).Value & 2) != 0);

            // --- Regex pair to test ---------------------------------------
            // α* : β*   vs   α* · (α ∧ β) · β*
            var aE = EAtom(a);
            var bE = EAtom(b);
            var aStar = EStar(aE);
            var bStar = EStar(bE);
            var aAndB = Ere<IStatePredicate>.Intersect(aE, bE);

            var R = Ere<IStatePredicate>.Fusion(aStar, bStar);
            var S = EConcat(aStar, EConcat(aAndB, bStar));

            // --- Equivalence  ⇔  both differences are empty languages -----
            Assert.That(LanguageEmptyOnChaos(
                Ere<IStatePredicate>.Intersect(R, Ere<IStatePredicate>.Complement(S)),
                chaos[0]),
                Is.True, "R \\ S is empty");
            Assert.That(LanguageEmptyOnChaos(
                Ere<IStatePredicate>.Intersect(S, Ere<IStatePredicate>.Complement(R)),
                chaos[0]),
                Is.True, "S \\ R is empty");
        }

        /// <summary>
        /// L(<paramref name="r"/>) ∩ Σω-prefixes = ∅, decided as the RLTL
        /// property <c>¬(r ; True)</c> on a universal (chaos) state graph.
        /// Returns the verdict (<c>true</c> = language empty).
        /// </summary>
        private static bool LanguageEmptyOnChaos(Ere<IStatePredicate> r, StateGraphNode chaosRoot)
        {
            var seqRtoTrue = Rltl<IStatePredicate>.SeqPrefix(r, Rltl<IStatePredicate>.True());
            var notSeq = RltlAlgebra.Default.Not(seqRtoTrue);
            return SymbolicRltlCheck.Check(chaosRoot, notSeq).Valid;
        }

        #endregion
    }
}
