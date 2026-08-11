// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    /// Pins the RLTL "Boolean formula used as an ABW state" soundness bug that
    /// made <c>Trigger</c> (and its dual <c>SeqPrefix</c>) report spurious
    /// violations on cyclic models.
    ///
    /// <para>
    /// The symbolic derivative turns each transition-term leaf into a
    /// <see cref="Dnf{TState}"/> over ABW states. The RLTL prefix-operator
    /// smart constructors distribute over regex <c>Union</c>
    /// (<c>(R₁+R₂);φ ≡ R₁;φ ∨ R₂;φ</c>, <c>(R₁+R₂)⊳φ ≡ R₁⊳φ ∧ R₂⊳φ</c>), and a
    /// residual regex becomes a union as soon as a concatenation is partially
    /// matched — e.g. <c>∂_A(Σ*·A·A) = A + Σ*·A·A</c>. Pre-fix, the whole
    /// resulting <c>∨</c>/<c>∧</c> formula was wrapped as a <em>single</em> Dnf
    /// atom, i.e. as one ABW state. Because the Büchi acceptance set F is
    /// decided from the state's head operator, such a compound state fell into
    /// the "no liveness obligation" default branch and was reported accepting
    /// even though every disjunct carried an unfulfilled <c>R;φ</c> obligation.
    /// The Miyano–Hayashi breakpoint then discharged the obligation every time
    /// the run passed through the compound state, so an ordinary cycle produced
    /// an accepting run of the ¬φ automaton — a counterexample to a property
    /// that actually holds.
    /// </para>
    ///
    /// <para>
    /// The fix lifts the positive Boolean structure into B⁺(Q) (Dnf disjunction
    /// / conjunction of atomic states) instead of hiding it inside a leaf, so
    /// every ABW state is a non-Boolean formula whose head decides acceptance.
    /// </para>
    /// </summary>
    [TestFixture]
    public class RltlBooleanStateAcceptanceRegressionTests
    {
        #region Test infrastructure

        private sealed class TestState : State
        {
            public string Label { get; }
            public int Value { get; }

            public TestState(string label, int value)
            {
                Label = label;
                Value = value;
            }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
                => clonedMap[this] = new TestState(Label, Value);

            protected override string StringRepresentationInternal(
                Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"{Label}({Value})";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
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

            public IList<StepResult> Apply(
                IState state, IReadOnlyList<(IStepFunction, StateGraphNode)> path) => null;
        }

        private static StateGraphNode MakeNode(string label, int value)
        {
            var state = new TestState(label, value);
            state.Freeze();
            return new StateGraphNode
            {
                State = state,
                StepFunctions = new List<IStepFunction> { new TestStepFunction("step") },
                Edges = new List<StateGraphEdge>(),
            };
        }

        private static void AddEdge(StateGraphNode from, StateGraphNode to)
            => from.Edges.Add(new StateGraphEdge
            {
                Target = to,
                StepFunction = new TestStepFunction("step"),
            });

        private static StateProp Prop(string name, Func<IState, bool> eval)
            => new StateProp(name, eval);

        private static Rltl<IStatePredicate> RAtom(StateProp p)
            => Rltl<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Ere<IStatePredicate> EAtom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Ere<IStatePredicate> ECat(params Ere<IStatePredicate>[] parts)
            => parts.Aggregate(Ere<IStatePredicate>.Concat);

        /// <summary>Bit 0 of <c>Value</c>.</summary>
        private static readonly StateProp A =
            Prop("A", s => (((TestState)s).Value & 1) != 0);

        /// <summary>Bit 1 of <c>Value</c>.</summary>
        private static readonly StateProp B =
            Prop("B", s => (((TestState)s).Value & 2) != 0);

        #endregion

        /// <summary>
        /// Minimal reproducer. <c>Σ*·A·A ⊳ ⊥</c> says "no prefix ever ends with
        /// two consecutive A-letters". The model alternates A, ¬A forever, so
        /// no such prefix exists and the property holds — pre-fix the checker
        /// returned a counterexample built from the ordinary A/¬A cycle.
        /// </summary>
        [Test]
        public void Trigger_TwoConsecutiveLetters_HoldsOnAlternatingCycle()
        {
            var a = MakeNode("a", 1);
            var x = MakeNode("x", 0);
            AddEdge(a, x);
            AddEdge(x, a);

            var phi = Rltl<IStatePredicate>.Trigger(
                ECat(Ere<IStatePredicate>.Sigma(), EAtom(A), EAtom(A)),
                Rltl<IStatePredicate>.False());

            var result = SymbolicRltlCheck.Check(a, phi);

            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        /// <summary>
        /// Negative control for the test above: when the cycle really does
        /// contain two consecutive A-letters the violation must still be found.
        /// </summary>
        [Test]
        public void Trigger_TwoConsecutiveLetters_StillDetectsRealViolation()
        {
            var a0 = MakeNode("a0", 1);
            var a1 = MakeNode("a1", 1);
            var x = MakeNode("x", 0);
            AddEdge(a0, a1);
            AddEdge(a1, x);
            AddEdge(x, a0);

            var phi = Rltl<IStatePredicate>.Trigger(
                ECat(Ere<IStatePredicate>.Sigma(), EAtom(A), EAtom(A)),
                Rltl<IStatePredicate>.False());

            var result = SymbolicRltlCheck.Check(a0, phi);

            Assert.That(result.Valid, Is.False);
            Assert.That(result.Trace, Is.Not.Null);
        }

        /// <summary>
        /// <c>L(R·A) ⊆ L(R)</c> for <c>R = A*</c>, so <c>A* ⊳ φ</c> implies
        /// <c>A*·A ⊳ φ</c>: the matched prefix lengths are the same set shifted
        /// by one, minus the empty match. Both verdicts must therefore be
        /// "holds" — pre-fix only the unshifted one was.
        /// </summary>
        [Test]
        public void Trigger_ShiftedPrefixLengths_AgreeWithUnshifted()
        {
            var s0 = MakeNode("s0", 3);
            var s1 = MakeNode("s1", 3);
            AddEdge(s0, s1);
            AddEdge(s1, s0);

            var star = Ere<IStatePredicate>.Star(EAtom(A));
            var starThenLetter = Ere<IStatePredicate>.Concat(star, EAtom(A));

            var unshifted = SymbolicRltlCheck.Check(
                s0, Rltl<IStatePredicate>.Trigger(star, RAtom(B)));
            var shifted = SymbolicRltlCheck.Check(
                s0, Rltl<IStatePredicate>.Trigger(starThenLetter, RAtom(B)));

            Assert.That(unshifted.Valid, Is.True, unshifted.GetTraceString());
            Assert.That(
                shifted.Valid,
                Is.True,
                "A*·A ⊳ B must hold whenever A* ⊳ B holds\n" + shifted.GetTraceString());
        }

        /// <summary>
        /// The bounded-overtaking shape <c>Σ*·A·B*·A ⊳ ⊥</c> — "A never happens
        /// twice with only B in between" — on a cycle where the run of B is
        /// always broken before the second A. This is the standalone form of
        /// the Peterson bounded-overtaking property, with a violating control.
        /// </summary>
        [Test]
        public void Trigger_BoundedOvertakingShape_SeparatesHoldingAndViolatingCycles()
        {
            var pattern = Rltl<IStatePredicate>.Trigger(
                ECat(
                    Ere<IStatePredicate>.Sigma(),
                    EAtom(A),
                    Ere<IStatePredicate>.Star(EAtom(B)),
                    EAtom(A)),
                Rltl<IStatePredicate>.False());

            // Holding cycle: A, B, (neither), back to A — the B-run is broken.
            var h0 = MakeNode("A", 1);
            var h1 = MakeNode("B", 2);
            var h2 = MakeNode("-", 0);
            AddEdge(h0, h1);
            AddEdge(h1, h2);
            AddEdge(h2, h0);

            var holds = SymbolicRltlCheck.Check(h0, pattern);
            Assert.That(holds.Valid, Is.True, holds.GetTraceString());

            // Violating cycle: A, B, A — an uninterrupted B-run between two A's.
            var v0 = MakeNode("A", 1);
            var v1 = MakeNode("B", 2);
            var v2 = MakeNode("A", 1);
            AddEdge(v0, v1);
            AddEdge(v1, v2);
            AddEdge(v2, v2);

            var violated = SymbolicRltlCheck.Check(v0, pattern);
            Assert.That(violated.Valid, Is.False);
        }

        /// <summary>
        /// Root-cause invariant, checked directly on the automaton rather than
        /// through a verdict: no ABW state produced by the RLTL derivative may
        /// be a Boolean connective, because <c>IsAccepting</c> classifies a
        /// state by its head operator and would lose the obligations nested
        /// underneath. Both the initial formula and every derivative leaf are
        /// covered.
        /// </summary>
        [Test]
        public void DerivativeAndInitialStates_AreNeverBooleanConnectives()
        {
            var eba = StatePropEbaProvider.Default;
            var registry = new ConditionRegistry<IStatePredicate>(
                EqualityComparer<IStatePredicate>.Default);
            var derivative = new RltlDerivative<IStatePredicate, State>(eba, registry);

            // ¬(Σ*·A·A ⊳ ⊥) = Σ*·A·A ; ⊤ — the formula from the reproducer,
            // conjoined with a top-level Boolean so the initial formula is a
            // connective too.
            var seq = Rltl<IStatePredicate>.SeqPrefix(
                ECat(Ere<IStatePredicate>.Sigma(), EAtom(A), EAtom(A)),
                Rltl<IStatePredicate>.True());
            var initial = Rltl<IStatePredicate>.And(seq, RAtom(B));

            var abw = derivative.ToABW(initial);

            var pending = new Queue<Rltl<IStatePredicate>>(abw.InitialState.GetAllStates());
            var seen = new HashSet<Rltl<IStatePredicate>>(pending);
            while (pending.Count > 0)
            {
                var state = pending.Dequeue();
                AssertNotBoolean(state);
                foreach (var leaf in abw.GetTransition(state).GetDistinctLeaves())
                {
                    foreach (var successor in leaf.GetAllStates())
                    {
                        AssertNotBoolean(successor);
                        if (seen.Add(successor)) pending.Enqueue(successor);
                    }
                }
            }

            // The whole point of splitting: the A-successor of Σ*·A·A;⊤ is the
            // two-clause Dnf (A;⊤) ∨ (Σ*·A·A;⊤). Both clauses must survive as
            // separate, non-accepting prefix obligations.
            var prefixStates = seen.OfType<RltlSeqPrefix<IStatePredicate>>().ToList();
            Assert.That(prefixStates.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(
                prefixStates.Any(s => derivative.IsAccepting(s)),
                Is.False,
                "a R;φ state carries a prefix obligation and is never accepting");
        }

        private static void AssertNotBoolean(Rltl<IStatePredicate> state)
            => Assert.That(
                state is RltlAnd<IStatePredicate> || state is RltlOr<IStatePredicate>,
                Is.False,
                $"ABW state '{state}' is a Boolean connective; its operands' "
                + "liveness obligations would be invisible to the acceptance set");

        /// <summary>
        /// Companion defect found while validating the fix above: the
        /// macrostate conjunction <c>δ(S) = ⋀_{q∈S} δ(q)</c> could lose a
        /// successor, turning a real counterexample into "property holds".
        ///
        /// <para><c>(Σ*·(A∧¬B) + B*) ; A</c> negates to a conjunction of two
        /// trigger states, so the pipeline conjoins two transition terms whose
        /// conditions include both <c>A</c> and <c>¬A</c>. The Apply memo table
        /// was keyed only on the operand node pair, so a sub-result that had
        /// been pruned to ⊥ because its path was unsatisfiable (<c>A ∧ ¬A</c>)
        /// was replayed on a satisfiable path, deleting the successor and
        /// emptying the automaton. Every prefix of <c>(¬A∧B)^ω</c> matches
        /// <c>B*</c> and <c>A</c> never holds, so the property is violated.
        /// See also
        /// <c>TransitionTermAlgebraTests.Apply_PrunedSubresult_DoesNotLeakOntoAnotherPath</c>.
        /// </para>
        /// </summary>
        [Test]
        public void SeqPrefix_OverUnion_DetectsViolationThroughMacrostateConjunction()
        {
            var node = MakeNode("nB", 2);   // ¬A ∧ B
            AddEdge(node, node);

            var aAndNotB = Ere<IStatePredicate>.Intersect(
                EAtom(A), Ere<IStatePredicate>.Complement(EAtom(B)));
            var union = Ere<IStatePredicate>.Union(
                Ere<IStatePredicate>.Concat(Ere<IStatePredicate>.Sigma(), aAndNotB),
                Ere<IStatePredicate>.Star(EAtom(B)));

            var result = SymbolicRltlCheck.Check(
                node, Rltl<IStatePredicate>.SeqPrefix(union, RAtom(A)));

            Assert.That(
                result.Valid,
                Is.False,
                "ε matches B* and A never holds, so (Σ*·(A∧¬B) + B*) ; A is violated");
            Assert.That(result.Trace, Is.Not.Null);
        }

        #region Differential oracle over lasso models

        /// <summary>
        /// Fixed-seed differential test that covers the whole class of bugs
        /// rather than the two specific shapes above.
        ///
        /// <para>Each case is a <em>deterministic</em> state graph — a tail
        /// followed by a cycle — so the model has exactly one behaviour
        /// <c>w = u·v^ω</c>. For a random ERE <c>R</c> the reference answers
        /// are computed by running the ERE derivative directly over <c>w</c>
        /// and testing nullability at each position, with
        /// (residual, lasso-position) loop detection for termination. The
        /// model checker must agree on all four prefix-operator readings.</para>
        ///
        /// <para>Before the fix this reported 101 disagreements out of 6000
        /// (spurious violations from Boolean ABW states, plus missed
        /// violations from the Apply memo defect).</para>
        /// </summary>
        [Test]
        public void RegexPrefixOperators_AgreeWithLassoDerivativeOracle()
        {
            var eba = StatePropEbaProvider.Default;
            var atoms = new[]
            {
                EAtom(A),
                EAtom(B),
                Ere<IStatePredicate>.Intersect(EAtom(A), Ere<IStatePredicate>.Complement(EAtom(B))),
                Ere<IStatePredicate>.Intersect(EAtom(B), Ere<IStatePredicate>.Complement(EAtom(A))),
            };

            var rng = new Random(987654);
            var failures = new List<string>();

            for (int trial = 0; trial < 400; trial++)
            {
                int tailLen = rng.Next(0, 3);
                int cycleLen = rng.Next(1, 5);
                int n = tailLen + cycleLen;

                var nodes = new StateGraphNode[n];
                var word = new State[n];
                for (int i = 0; i < n; i++)
                {
                    nodes[i] = MakeNode($"n{i}", rng.Next(0, 4));
                    word[i] = (State)nodes[i].State;
                }
                for (int i = 0; i < n; i++)
                    AddEdge(nodes[i], nodes[i == n - 1 ? tailLen : i + 1]);

                var r = RandomEre(rng, rng.Next(1, 4), atoms);

                var registry = new ConditionRegistry<IStatePredicate>(
                    EqualityComparer<IStatePredicate>.Default);
                var ereDerivative = new EreDerivative<IStatePredicate, State>(eba, registry);
                var (someMatch, everyMatchHasA, someMatchHasA) =
                    RunOracle(r, word, tailLen, ereDerivative, registry, eba);

                void Expect(string reading, Rltl<IStatePredicate> f, bool expected)
                {
                    var verdict = SymbolicRltlCheck.Check(nodes[0], f);
                    if (verdict.Valid != expected)
                    {
                        failures.Add(
                            $"[{reading}] trial {trial}: R={r} "
                            + $"word=[{string.Join(",", word.Select(s => ((TestState)s).Value))}] "
                            + $"tail={tailLen} oracle={expected} checker={verdict.Valid}");
                    }
                }

                Expect("R⊳⊥", Rltl<IStatePredicate>.Trigger(r, Rltl<IStatePredicate>.False()), !someMatch);
                Expect("R⊳A", Rltl<IStatePredicate>.Trigger(r, RAtom(A)), everyMatchHasA);
                Expect("R;⊤", Rltl<IStatePredicate>.SeqPrefix(r, Rltl<IStatePredicate>.True()), someMatch);
                Expect("R;A", Rltl<IStatePredicate>.SeqPrefix(r, RAtom(A)), someMatchHasA);
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(5)));
        }

        /// <summary>
        /// Reference semantics on the single behaviour of a lasso model:
        /// <c>someMatch</c> = ∃k. w[0..k) ∈ L(R); <c>everyMatchHasA</c> =
        /// ∀k. w[0..k) ∈ L(R) → A(w[k]); <c>someMatchHasA</c> =
        /// ∃k. w[0..k) ∈ L(R) ∧ A(w[k]).
        /// </summary>
        private static (bool someMatch, bool everyMatchHasA, bool someMatchHasA) RunOracle(
            Ere<IStatePredicate> r,
            IReadOnlyList<State> word,
            int tailLen,
            EreDerivative<IStatePredicate, State> ereDerivative,
            ConditionRegistry<IStatePredicate> registry,
            IEffectiveBooleanAlgebra<IStatePredicate, State> eba)
        {
            int cycleLen = word.Count - tailLen;
            var residual = r;
            var seen = new HashSet<(int residualId, int position)>();
            bool someMatch = false, everyMatchHasA = true, someMatchHasA = false;

            for (int k = 0; ; k++)
            {
                int pos = k < tailLen ? k : tailLen + ((k - tailLen) % cycleLen);
                if (residual.Nullable)
                {
                    someMatch = true;
                    if ((((TestState)word[pos]).Value & 1) != 0) someMatchHasA = true;
                    else everyMatchHasA = false;
                }
                if (!seen.Add((residual.Id, pos)))
                    return (someMatch, everyMatchHasA, someMatchHasA);
                residual = ereDerivative.Derivative(residual).Evaluate(word[pos], registry, eba);
            }
        }

        private static Ere<IStatePredicate> RandomEre(
            Random rng, int depth, Ere<IStatePredicate>[] atoms)
        {
            if (depth <= 0) return atoms[rng.Next(atoms.Length)];
            switch (rng.Next(6))
            {
                case 0:
                    return atoms[rng.Next(atoms.Length)];
                case 1:
                    return Ere<IStatePredicate>.Concat(
                        RandomEre(rng, depth - 1, atoms), RandomEre(rng, depth - 1, atoms));
                case 2:
                    return Ere<IStatePredicate>.Union(
                        RandomEre(rng, depth - 1, atoms), RandomEre(rng, depth - 1, atoms));
                case 3:
                    return Ere<IStatePredicate>.Star(RandomEre(rng, depth - 1, atoms));
                case 4:
                    return Ere<IStatePredicate>.Intersect(
                        RandomEre(rng, depth - 1, atoms), RandomEre(rng, depth - 1, atoms));
                default:
                    return Ere<IStatePredicate>.Concat(
                        Ere<IStatePredicate>.Sigma(), RandomEre(rng, depth - 1, atoms));
            }
        }

        #endregion

        #region Public stutter-safe surface        /// <summary>
        /// A model that cycles <c>Pos = 0 → 1 → 2 → 0</c>. Every step changes
        /// the state, so every step is visible to a <see cref="SafeRegex"/>.
        /// </summary>
        private sealed class RingState : State
        {
            public int Pos { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
                => clonedMap[this] = new RingState { Pos = Pos };

            protected override string StringRepresentationInternal(
                Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"Pos={Pos}";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        private sealed class AdvanceStep : BaseStepFunction
        {
            public override string StepFunctionId => "Advance";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var ring = (RingState)state;
                var next = (RingState)ring.Clone();
                next.Pos = (ring.Pos + 1) % 3;
                return new[]
                {
                    new StepResult
                    {
                        State = next,
                        StepFunctions = new IStepFunction[] { this },
                    },
                };
            }
        }

        /// <summary>
        /// End-to-end guard for the public stutter-safe surface:
        /// <c>FormulaBuilder.Whenever</c> lowers to the very same
        /// <c>Trigger</c> derivative path, so it must agree with the model on
        /// "two consecutive changing steps taken from <c>Pos == 0</c>", which
        /// is impossible on a 3-cycle. The <c>Pos &lt;= 1</c> variant is the
        /// control: those steps really are consecutive, so it must be
        /// violated.
        ///
        /// <para>Note the erasure lowering wraps every step as
        /// <c>Unchanged*·(Changed ∧ p)·Unchanged*</c>, so residual unions here
        /// share the <c>Unchanged*</c> head and get re-factored by
        /// <c>Ere.Union</c> before the prefix operator can distribute them —
        /// which is why this shape did not expose the bug on its own. It is
        /// kept as a regression guard for the surface users actually call.
        /// </para>
        /// </summary>
        [Test]
        public void Whenever_TwoConsecutiveChangingSteps_MatchesOnlyWhenTheyExist()
        {
            var root = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new AdvanceStep() },
                new RingState { Pos = 0 });

            var f = Formula.For<RingState>();
            var any = f.AnyChangingStep;

            var fromZero = f.ChangingStep(f.Observe(s => s.Pos == 0, "AtZero"));
            var fromLow = f.ChangingStep(f.Observe(s => s.Pos <= 1, "Low"));

            var noTwoZeroSteps = root.Check(
                f.Whenever(any.Star().Then(fromZero).Then(fromZero), f.False));
            Assert.That(noTwoZeroSteps.Valid, Is.True, noTwoZeroSteps.GetTraceString());

            var noTwoLowSteps = root.Check(
                f.Whenever(any.Star().Then(fromLow).Then(fromLow), f.False));
            Assert.That(
                noTwoLowSteps.Valid,
                Is.False,
                "steps from Pos 0 and Pos 1 are consecutive, so the pattern matches");
        }

        #endregion
    }
}
