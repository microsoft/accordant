// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests
{
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using NUnit.Framework;

    /// <summary>
    /// End-to-end tests for propositions over transitions <c>p(s, a, s')</c>
    /// and its overloads <c>p(s)</c> / <c>p(s, s')</c>. Verifies that the
    /// target state, the action (step function) and the edge metadata are all
    /// observable by a proposition during model checking, that state-only
    /// propositions remain byte-for-byte backward compatible, and that the
    /// reserved stutter action is presented at terminal self-loops.
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

        private static StateGraphNode IncrementOnly(int max)
            => StateGraph.ExploreStateGraph(
                new IStepFunction[] { new IncrementStep(max) },
                new CounterState { Count = 0 });

        private static StateGraphNode IncrementAndDecrement(int max)
            => StateGraph.ExploreStateGraph(
                new IStepFunction[] { new IncrementStep(max), new DecrementStep() },
                new CounterState { Count = 0 });

        #endregion

        #region Target-state propositions p(s, s')

        [Test]
        public void TargetProposition_NonDecreasing_Holds_WhenOnlyIncrementing()
        {
            var root = IncrementOnly(max: 4);
            var p = Formula.For<CounterState>().WithoutStutterGuarantee();
            var nonDecreasing = p.ObserveTransition(
                (s, sp) => sp.Count >= s.Count, "NonDecreasing");

            // Every real edge increments; the terminal stutter has s' == s, so
            // the property holds everywhere.
            var result = root.Check(p.Always(nonDecreasing));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void TargetProposition_NonDecreasing_Fails_WhenDecrementPresent()
        {
            var root = IncrementAndDecrement(max: 3);
            var p = Formula.For<CounterState>().WithoutStutterGuarantee();
            var nonDecreasing = p.ObserveTransition(
                (s, sp) => sp.Count >= s.Count, "NonDecreasing");

            // A decrement edge takes s' = s - 1 < s, violating the property.
            var result = root.Check(p.Always(nonDecreasing));
            Assert.That(result.Valid, Is.False);
            Assert.That(result.Trace, Is.Not.Null);
        }

        #endregion

        #region Action propositions p(s, a, s')

        [Test]
        public void ActionProposition_NoDecrementTaken_Holds_WhenOnlyIncrementing()
        {
            var root = IncrementOnly(max: 4);
            var p = Formula.For<CounterState>().WithoutStutterGuarantee();
            var decrementTaken = p.ObserveTransition(
                (s, a, sp) => a.ActionId == "Decrement", "DecrementTaken");

            // No Decrement edge exists; the stutter action is not "Decrement".
            var result = root.Check(p.Always(!decrementTaken));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void ActionProposition_DetectsDecrement_Fails_WhenDecrementPresent()
        {
            var root = IncrementAndDecrement(max: 3);
            var p = Formula.For<CounterState>().WithoutStutterGuarantee();
            var decrementTaken = p.ObserveTransition(
                (s, a, sp) => a.ActionId == "Decrement", "DecrementTaken");

            var result = root.Check(p.Always(!decrementTaken));
            Assert.That(result.Valid, Is.False);
            Assert.That(result.Trace, Is.Not.Null);
        }

        [Test]
        public void ActionProposition_EdgeMetadata_IsObservable()
        {
            var p = Formula.For<CounterState>().WithoutStutterGuarantee();
            var notDecMetadata = p.ObserveTransition(
                (s, a, sp) => (a.Metadata as string) != "dec", "NotDecMetadata");

            // Increment edges carry "inc"; stutter carries null; neither is "dec".
            var incOnly = IncrementOnly(max: 4).Check(p.Always(notDecMetadata));
            Assert.That(incOnly.Valid, Is.True);

            // The decrement edge carries "dec", violating the property.
            var withDec = IncrementAndDecrement(max: 3).Check(p.Always(notDecMetadata));
            Assert.That(withDec.Valid, Is.False);
        }

        [Test]
        public void ActionProposition_StutterAction_IsPresentedAtTerminal()
        {
            // The increment-only model terminates at Count == max, where a
            // stutter self-loop is emitted. A proposition that is true exactly
            // on the stutter action must eventually hold on that model.
            var root = IncrementOnly(max: 3);
            var p = Formula.For<CounterState>().WithoutStutterGuarantee();
            var stutter = p.ObserveTransition(
                (s, a, sp) => a.IsStutter, "Stutter");

            var result = root.Check(p.Eventually(stutter));
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
        public void StateProposition_And_TransitionProposition_Compose()
        {
            var root = IncrementAndDecrement(max: 3);
            var p = Formula.For<CounterState>().WithoutStutterGuarantee();
            var atZero = p.Observe(s => s.Count == 0, "AtZero");
            var decrementTaken = p.ObserveTransition(
                (s, a, sp) => a.ActionId == "Decrement", "DecrementTaken");

            // You cannot take a Decrement from Count == 0 (it is disabled), so
            // "at zero AND decrementing" never happens.
            var result = root.Check(p.Always(!(atZero & decrementTaken)));
            Assert.That(result.Valid, Is.True);
        }

        #endregion
    }
}
