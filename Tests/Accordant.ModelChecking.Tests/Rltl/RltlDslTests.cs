namespace Accordant.ModelChecking.Tests.Rltl
{
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// Smoke tests for the user-facing RLTL DSL (<see cref="RltlFormula"/>,
    /// <see cref="Regex"/>, <see cref="RltlCheck"/>). The semantics are
    /// delegated to <c>SymbolicRltlCheck</c> (already covered by
    /// <c>SymbolicRltlCheckTests</c>); these tests focus on the surface API
    /// and operator overloads.
    /// </summary>
    [TestFixture]
    public class RltlDslTests
    {
        #region Test infrastructure

        private sealed class TestState : State
        {
            public int Value { get; set; }
            public TestState(int v) { Value = v; }
            protected override void CloneInternal(Dictionary<object, object> m)
                => m[this] = new TestState(Value);
            protected override void LockComponents(HashSet<object> v) { }
            protected override string StringRepresentationInternal(Dictionary<object, string> p, string path, bool forceRecompute) => $"s({Value})";
            protected override void FreezeComponents(HashSet<object> visited) { }
        }

        private sealed class Step : IStepFunction
        {
            public string StepFunctionId { get; }
            public int StepFunctionIdHash { get; }
            public Step(string id) { StepFunctionId = id; StepFunctionIdHash = id.GetHashCode(); }
            public IList<StepResult> Apply(IState s, IReadOnlyList<(IStepFunction, StateGraphNode)> path) => null;
        }

        private static StateGraphNode N(int v)
        {
            var st = new TestState(v); st.Freeze();
            return new StateGraphNode
            {
                State = st,
                StepFunctions = new List<IStepFunction>(),
                Edges = new List<StateGraphEdge>()
            };
        }

        private static void E(StateGraphNode a, StateGraphNode b, string id = "s")
            => a.Edges.Add(new StateGraphEdge { Target = b, StepFunction = new Step(id) });

        #endregion

        #region Boolean / temporal operator overloads

        [Test]
        public void Dsl_Always_Eventually_LeadsTo_OperatorOverloads()
        {
            // s0(active) → s1(detected) → s1 self-loop.
            var s0 = N(1);
            var s1 = N(2);
            E(s0, s1);
            E(s1, s1);

            var active = RltlFormula.Prop(s => ((TestState)s).Value == 1, "active");
            var detected = RltlFormula.Prop(s => ((TestState)s).Value == 2, "detected");

            // Combined: □(active → ◇detected) ∧ □(detected → detected),
            // exercising leads-to and the & operator.
            var safety = RltlFormula.Always(RltlFormula.Implies(detected, detected));
            var liveness = RltlFormula.LeadsTo(active, detected);
            var combined = safety & liveness;

            var result = RltlCheck.Check(s0, combined);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        [Test]
        public void Dsl_Negation_ProducesEquivalentInverse()
        {
            var s0 = N(0);
            var s1 = N(1);
            E(s0, s1); E(s1, s0);

            var a = RltlFormula.Prop(s => ((TestState)s).Value == 99, "a");
            var fa = RltlFormula.Eventually(a);

            Assert.That(RltlCheck.Check(s0, fa).Valid, Is.False);
            Assert.That(RltlCheck.Check(s0, !fa).Valid, Is.True);
        }

        #endregion

        #region Regex DSL

        [Test]
        public void Dsl_Regex_Sigma_TriggerEquivalentToAlways()
        {
            // Σ* ⊳ p ≡ G p.
            var p = RltlFormula.Prop(st => ((TestState)st).Value == 1, "p");
            var formula = RltlFormula.Trigger(Regex.Sigma, p);

            var s0 = N(1); var s1 = N(1);
            E(s0, s1); E(s1, s1);
            Assert.That(RltlCheck.Check(s0, formula).Valid, Is.True);

            var v0 = N(1); var v1 = N(0);
            E(v0, v1); E(v1, v1);
            Assert.That(RltlCheck.Check(v0, formula).Valid, Is.False);
        }

        [Test]
        public void Dsl_Regex_OperatorOverloads_BuildAcceptedRegex()
        {
            // Just check the DSL composes: (p | !p) — every letter — starred,
            // used as a Trigger guard, equivalent to G φ.
            var p = Regex.Prop(s => ((TestState)s).Value == 1, "p");
            var anyLetter = p | !p;
            var rgxAll = Regex.Star(anyLetter);

            var q = RltlFormula.Prop(s => ((TestState)s).Value > 0, "v>0");
            var formula = RltlFormula.Trigger(rgxAll, q);

            var s0 = N(1); var s1 = N(2);
            E(s0, s1); E(s1, s1);
            Assert.That(RltlCheck.Check(s0, formula).Valid, Is.True);

            var v0 = N(1); var v1 = N(0);
            E(v0, v1); E(v1, v1);
            Assert.That(RltlCheck.Check(v0, formula).Valid, Is.False);
        }

        [Test]
        public void Dsl_SeqPrefix_Then_RegexShape()
        {
            // (p .Then q) ; r — exists a (p then q) prefix followed by r.
            var p = Regex.Prop(s => ((TestState)s).Value == 1, "p");
            var q = Regex.Prop(s => ((TestState)s).Value == 2, "q");
            var rPred = RltlFormula.Prop(s => ((TestState)s).Value == 3, "r");

            var formula = RltlFormula.SeqPrefix(p.Then(q), rPred);

            // Satisfied: 1 → 2 → 3 → 3
            var s0 = N(1); var s1 = N(2); var s2 = N(3);
            E(s0, s1); E(s1, s2); E(s2, s2);
            Assert.That(RltlCheck.Check(s0, formula).Valid, Is.True);

            // Violated: 1 → 2 → 0 → 0 (q-suffix never satisfies r).
            var v0 = N(1); var v1 = N(2); var v2 = N(0);
            E(v0, v1); E(v1, v2); E(v2, v2);
            Assert.That(RltlCheck.Check(v0, formula).Valid, Is.False);
        }

        #endregion

        #region Constants

        [Test]
        public void Dsl_True_AlwaysHolds_False_AlwaysFails()
        {
            var s0 = N(0); E(s0, s0);
            Assert.That(RltlCheck.Check(s0, RltlFormula.True).Valid, Is.True);
            Assert.That(RltlCheck.Check(s0, RltlFormula.False).Valid, Is.False);
        }

        #endregion
    }
}
