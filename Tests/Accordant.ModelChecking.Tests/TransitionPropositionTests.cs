// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests
{
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using NUnit.Framework;

    /// <summary>
    /// End-to-end tests for state-pair propositions <c>p(s, s')</c>.
    /// Verifies stutter-safe lifting, literal stutter-sensitive evaluation,
    /// mixed state/transition formulas, and state-only compatibility.
    /// </summary>
    [TestFixture]
    public class TransitionPropositionTests
    {
        #region Counter model with per-edge metadata

        private sealed class CounterState : State
        {
            public int Count { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
                => clonedMap[this] = new CounterState { Count = this.Count };

            protected override string StringRepresentationInternal(
                Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"Count={this.Count}";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        private sealed class IncrementStep : BaseStepFunction
        {
            private readonly int max;
            public IncrementStep(int max) { this.max = max; }
            public override string StepFunctionId => "Increment";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var cs = (CounterState)state;
                if (cs.Count >= this.max) return null;
                var next = (CounterState)cs.Clone();
                next.Count++;
                return new[]
                {
                    new StepResult
                    {
                        State = next,
                        StepFunctions = new IStepFunction[] { this },
                        EdgeMetadata = "inc",
                    },
                };
            }
        }

        private sealed class DecrementStep : BaseStepFunction
        {
            public override string StepFunctionId => "Decrement";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var cs = (CounterState)state;
                if (cs.Count <= 0) return null;
                var next = (CounterState)cs.Clone();
                next.Count--;
                return new[]
                {
                    new StepResult
                    {
                        State = next,
                        StepFunctions = new IStepFunction[] { this },
                        EdgeMetadata = "dec",
                    },
                };
            }
        }

        private sealed class NoOpStep : BaseStepFunction
        {
            public override string StepFunctionId => "NoOp";

            protected override IList<StepResult> ApplyInternal(IState state)
                => new[]
                {
                    new StepResult
                    {
                        State = state.Clone(),
                        StepFunctions = new IStepFunction[] { this },
                    },
                };
        }

        private static StateGraphNode IncrementOnly(int max)
            => StateGraph.ExploreStateGraph(
                new IStepFunction[] { new IncrementStep(max) },
                new CounterState { Count = 0 });

        private static StateGraphNode IncrementAndDecrement(int max)
            => StateGraph.ExploreStateGraph(
                new IStepFunction[] { new IncrementStep(max), new DecrementStep() },
                new CounterState { Count = 0 });

        private static StateGraphNode OneIncrement(bool insertFiniteStutter)
        {
            var increment = new IncrementStep(1);
            var noOp = new NoOpStep();
            var initialState = new CounterState();
            initialState.Freeze();
            var finalState = new CounterState { Count = 1 };
            finalState.Freeze();
            var final = new StateGraphNode
            {
                State = finalState,
                StepFunctions = new List<IStepFunction>(),
                Edges = new List<StateGraphEdge>(),
            };

            StateGraphNode IncrementSource(CounterState state)
                => new StateGraphNode
                {
                    State = state,
                    StepFunctions = new List<IStepFunction> { increment },
                    Edges = new List<StateGraphEdge>
                    {
                        new StateGraphEdge
                        {
                            Target = final,
                            StepFunction = increment,
                        },
                    },
                };

            if (!insertFiniteStutter)
                return IncrementSource(initialState);

            var repeatedState = new CounterState();
            repeatedState.Freeze();
            var repeated = IncrementSource(repeatedState);
            return new StateGraphNode
            {
                State = initialState,
                StepFunctions = new List<IStepFunction> { noOp },
                Edges = new List<StateGraphEdge>
                {
                    new StateGraphEdge { Target = repeated, StepFunction = noOp },
                },
            };
        }

        #endregion

        #region Target-state propositions p(s, s')

        [Test]
        public void TargetProposition_NonDecreasing_Holds_WhenOnlyIncrementing()
        {
            var root = IncrementOnly(max: 4);
            var p = Formula.For<CounterState>();
            var nonDecreasing = p.ObserveTransition(
                (s, sp) => sp.Count >= s.Count, "NonDecreasing");

            // Every changing edge increments; terminal stutter is ignored.
            var result = root.Check(p.Always(nonDecreasing));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void TargetProposition_NonDecreasing_Fails_WhenDecrementPresent()
        {
            var root = IncrementAndDecrement(max: 3);
            var p = Formula.For<CounterState>();
            var nonDecreasing = p.ObserveTransition(
                (s, sp) => sp.Count >= s.Count, "NonDecreasing");

            // A decrement edge takes s' = s - 1 < s, violating the property.
            var result = root.Check(p.Always(nonDecreasing));
            Assert.That(result.Valid, Is.False);
            Assert.That(result.Trace, Is.Not.Null);
        }

        #endregion

        #region Stutter-safe and sensitive transition semantics

        [Test]
        public void SafeAlways_IgnoresTerminalStutter()
        {
            var root = IncrementOnly(max: 4);
            var f = Formula.For<CounterState>();
            var increment = f.ObserveTransition(
                (s, sp) => sp.Count == s.Count + 1, "Increment");

            var result = root.Check(f.Always(increment));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void SafeEventually_RequiresChangingOccurrence()
        {
            var root = IncrementOnly(max: 0);
            var f = Formula.For<CounterState>();
            var unchanged = f.ObserveTransition(
                (s, sp) => s.Equals(sp), "Unchanged");

            var result = root.Check(f.Eventually(unchanged));
            Assert.That(result.Valid, Is.False);
        }

        [Test]
        public void SafeAlways_IgnoresNamedUnchangedModelEdges()
        {
            var root = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new IncrementStep(2), new NoOpStep() },
                new CounterState());
            var f = Formula.For<CounterState>();
            var increment = f.ObserveTransition(
                (state, next) => next.Count == state.Count + 1);

            Assert.That(root.Check(f.Always(increment)).Valid, Is.True);
        }

        [Test]
        public void SafeAlways_UsesSemanticEqualityAcrossDistinctNodes()
        {
            var state = new CounterState();
            state.Freeze();
            var equalState = new CounterState();
            equalState.Freeze();
            var noOp = new NoOpStep();
            var terminal = new StateGraphNode
            {
                State = equalState,
                StepFunctions = new List<IStepFunction>(),
                Edges = new List<StateGraphEdge>(),
            };
            var root = new StateGraphNode
            {
                State = state,
                StepFunctions = new List<IStepFunction> { noOp },
                Edges = new List<StateGraphEdge>
                {
                    new StateGraphEdge { Target = terminal, StepFunction = noOp },
                },
            };
            var f = Formula.For<CounterState>();
            var increment = f.ObserveTransition(
                (current, next) => next.Count == current.Count + 1);

            Assert.That(root.Check(f.Always(increment)).Valid, Is.True);
        }

        [Test]
        public void SafeTransitionOperators_UseAllowedAndOccursRoles()
        {
            var root = IncrementOnly(max: 2);
            var f = Formula.For<CounterState>();
            var increment = f.ObserveTransition(
                (state, next) => next.Count == state.Count + 1);
            var firstIncrement = f.ObserveTransition(
                (state, next) => state.Count == 0 && next.Count == 1);
            var reachesTwo = f.ObserveTransition(
                (state, next) => state.Count == 1 && next.Count == 2);

            Assert.That(root.Check(f.Always(increment)).Valid, Is.True);
            Assert.That(root.Check(f.Eventually(reachesTwo)).Valid, Is.True);
            Assert.That(root.Check(f.InfinitelyOften(increment)).Valid, Is.False);
            Assert.That(root.Check(f.Stabilizes(increment)).Valid, Is.True);
            Assert.That(root.Check(f.Until(increment, reachesTwo)).Valid, Is.True);
            Assert.That(root.Check(f.Release(reachesTwo, increment)).Valid, Is.True);
            Assert.That(root.Check(f.LeadsTo(firstIncrement, reachesTwo)).Valid, Is.True);
        }

        [Test]
        public void SafeMixedOperators_AreAnchoredAtTheSourcePosition()
        {
            var root = IncrementOnly(max: 2);
            var f = Formula.For<CounterState>();
            var atZero = f.Observe(state => state.Count == 0);
            var atTwo = f.Observe(state => state.Count == 2);
            var increment = f.ObserveTransition(
                (state, next) => next.Count == state.Count + 1);
            var firstIncrement = f.ObserveTransition(
                (state, next) => state.Count == 0 && next.Count == 1);
            var reachesTwo = f.ObserveTransition(
                (state, next) => state.Count == 1 && next.Count == 2);

            Assert.That(root.Check(f.Until(atZero, firstIncrement)).Valid, Is.True);
            Assert.That(root.Check(f.Until(increment, atTwo)).Valid, Is.True);
            Assert.That(root.Check(f.Release(atTwo, increment)).Valid, Is.True);
            Assert.That(root.Check(f.Release(reachesTwo, f.True)).Valid, Is.True);
            Assert.That(root.Check(f.LeadsTo(atZero, reachesTwo)).Valid, Is.True);
            Assert.That(root.Check(f.LeadsTo(firstIncrement, atTwo)).Valid, Is.True);
        }

        [Test]
        public void RecurrenceOperators_DistinguishChangingEdgesFromTerminalStutter()
        {
            var root = IncrementAndDecrement(max: 2);
            var f = Formula.For<CounterState>();
            var changes = f.ObserveTransition(
                (state, next) => state.Count != next.Count);
            var increments = f.ObserveTransition(
                (state, next) => next.Count == state.Count + 1);

            Assert.That(root.Check(f.InfinitelyOften(changes)).Valid, Is.True);
            Assert.That(root.Check(f.Stabilizes(increments)).Valid, Is.False);
        }

        [Test]
        public void SafeOperators_AreInvariantUnderOneFiniteInsertedStutter()
        {
            var direct = OneIncrement(insertFiniteStutter: false);
            var refined = OneIncrement(insertFiniteStutter: true);
            var f = Formula.For<CounterState>();
            var increment = f.ObserveTransition(
                (state, next) => next.Count == state.Count + 1);
            var formulas = new[]
            {
                f.Always(increment),
                f.Eventually(increment),
                f.InfinitelyOften(increment),
                f.Stabilizes(increment),
                f.Until(increment, increment),
                f.Release(increment, increment),
                f.LeadsTo(increment, increment),
            };

            foreach (var formula in formulas)
            {
                Assert.That(
                    refined.Check(formula).Valid,
                    Is.EqualTo(direct.Check(formula).Valid),
                    formula.ToString());
            }

            var sensitive = f.AllowStutterSensitiveFormulas();
            var literalIncrement = sensitive.ObserveTransition(
                (state, next) => next.Count == state.Count + 1);
            Assert.That(direct.Check(sensitive.Next(literalIncrement)).Valid, Is.False);
            Assert.That(refined.Check(sensitive.Next(literalIncrement)).Valid, Is.True);
        }

        [Test]
        public void SensitiveAlways_ObservesTerminalStutter()
        {
            var root = IncrementOnly(max: 4);
            var f = Formula.For<CounterState>().AllowStutterSensitiveFormulas();
            var increment = f.ObserveTransition(
                (s, sp) => sp.Count == s.Count + 1, "Increment");

            var result = root.Check(f.Always(increment));
            Assert.That(result.Valid, Is.False);
        }

        [Test]
        public void SensitiveEventually_CanObserveUnchangedPosition()
        {
            var root = IncrementOnly(max: 3);
            var f = Formula.For<CounterState>().AllowStutterSensitiveFormulas();
            var unchanged = f.ObserveTransition(
                (s, sp) => s.Equals(sp), "Unchanged");

            var result = root.Check(f.Eventually(unchanged));
            Assert.That(result.Valid, Is.True);
        }

        #endregion

        #region Backward compatibility of state-only propositions p(s)

        [Test]
        public void StateProposition_StillHolds_ForInBounds()
        {
            const int max = 3;
            var root = IncrementAndDecrement(max);
            var p = Formula.For<CounterState>();
            var inBounds = p.Observe(s => s.Count >= 0 && s.Count <= max, "InBounds");

            var result = root.Check(p.Always(inBounds));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void StateAndTransition_UseStrictSourcePositionSemantics()
        {
            var root = IncrementOnly(max: 3);
            var f = Formula.For<CounterState>();
            var atZero = f.Observe(s => s.Count == 0, "AtZero");
            var increment = f.ObserveTransition(
                (s, sp) => sp.Count == s.Count + 1, "Increment");

            var result = root.Check(f.Until(atZero, increment));
            Assert.That(result.Valid, Is.True);
        }

        #endregion
    }
}
