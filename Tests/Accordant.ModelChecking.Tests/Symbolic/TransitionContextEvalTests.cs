// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Unit tests for the transition-letter evaluation primitives that back
    /// propositions over transitions: <see cref="TransitionContext"/>,
    /// <see cref="StutterAction"/>, the transition-aware
    /// <see cref="StateProp"/> constructor, and the
    /// <see cref="IStatePredicate.IsTransitionAware"/> flag propagated through
    /// the Boolean combinators.
    /// </summary>
    [TestFixture]
    public class TransitionContextEvalTests
    {
        private sealed class TestState : State
        {
            public int V { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
                => clonedMap[this] = new TestState { V = this.V };

            protected override string StringRepresentationInternal(
                Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"V={this.V}";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        private sealed class NamedStep : IStepFunction
        {
            public NamedStep(string id) { StepFunctionId = id; }
            public string StepFunctionId { get; }
            public IList<StepResult> Apply(
                IState state, IReadOnlyList<(IStepFunction, StateGraphNode)> path)
                => new List<StepResult>();
        }

        private static TestState S(int v) => new TestState { V = v };

        [Test]
        public void StateOnlyProp_IsNotTransitionAware_AndReadsSourceOnly()
        {
            var prop = new StateProp("v1", s => ((TestState)s).V == 1);
            var atom = new StatePredAtom(prop);

            Assert.That(atom.IsTransitionAware, Is.False);

            // Reads From only: true when From.V == 1 regardless of To.
            var ctx = TransitionContext.Edge(S(1), new NamedStep("a"), null, S(2));
            Assert.That(atom.Eval(in ctx), Is.True);

            var ctx2 = TransitionContext.Edge(S(2), new NamedStep("a"), null, S(1));
            Assert.That(atom.Eval(in ctx2), Is.False);

            // State-only Eval overload agrees.
            Assert.That(atom.Eval(S(1)), Is.True);
            Assert.That(atom.Eval(S(2)), Is.False);
        }

        [Test]
        public void TransitionProp_IsTransitionAware_AndReadsTargetAndAction()
        {
            var incBy1 = StateProp.OverTransition(
                "inc1",
                ctx => ((TestState)ctx.To).V == ((TestState)ctx.From).V + 1);
            var atom = new StatePredAtom(incBy1);

            Assert.That(atom.IsTransitionAware, Is.True);

            var good = TransitionContext.Edge(S(1), new NamedStep("a"), null, S(2));
            Assert.That(atom.Eval(in good), Is.True);

            var bad = TransitionContext.Edge(S(1), new NamedStep("a"), null, S(1));
            Assert.That(atom.Eval(in bad), Is.False);
        }

        [Test]
        public void ActionProp_ObservesStepFunctionId_AndMetadata()
        {
            var isDec = StateProp.OverTransition(
                "isDec",
                ctx => ctx.Action?.StepFunctionId == "Decrement");
            var atom = new StatePredAtom(isDec);

            var dec = TransitionContext.Edge(S(3), new NamedStep("Decrement"), "dec", S(2));
            var inc = TransitionContext.Edge(S(2), new NamedStep("Increment"), "inc", S(3));

            Assert.That(atom.Eval(in dec), Is.True);
            Assert.That(atom.Eval(in inc), Is.False);
        }

        [Test]
        public void StutterContext_UsesStutterAction_AndCollapsesState()
        {
            var stutterCtx = TransitionContext.Stutter(S(5));

            Assert.That(stutterCtx.Action, Is.SameAs(StutterAction.Instance));
            Assert.That(((TestState)stutterCtx.From).V, Is.EqualTo(5));
            Assert.That(((TestState)stutterCtx.To).V, Is.EqualTo(5));

            var isStutter = new StatePredAtom(
                StateProp.OverTransition("stutter", ctx => ctx.Action is StutterAction));
            Assert.That(isStutter.Eval(in stutterCtx), Is.True);

            var edgeCtx = TransitionContext.Edge(S(1), new NamedStep("a"), null, S(2));
            Assert.That(isStutter.Eval(in edgeCtx), Is.False);
        }

        [Test]
        public void TransitionProp_StateOnlyView_IsTheStutterSelfLoop()
        {
            // p(s, s') := s' == s. Under the state-only view (stutter loop),
            // s' == s always holds.
            var idle = StateProp.OverTransition(
                "idle",
                ctx => ((TestState)ctx.To).V == ((TestState)ctx.From).V);
            var atom = new StatePredAtom(idle);

            Assert.That(atom.Eval(S(7)), Is.True);
            Assert.That(idle.Evaluate(S(7)), Is.True);
        }

        [Test]
        public void IsTransitionAware_PropagatesThroughCombinators()
        {
            var stateOnly = new StatePredAtom(new StateProp("s", s => true));
            var transition = new StatePredAtom(
                StateProp.OverTransition("t", ctx => ((TestState)ctx.To).V == 0));

            Assert.That(stateOnly.IsTransitionAware, Is.False);
            Assert.That(transition.IsTransitionAware, Is.True);

            Assert.That(new StatePredNot(stateOnly).IsTransitionAware, Is.False);
            Assert.That(new StatePredNot(transition).IsTransitionAware, Is.True);

            Assert.That(new StatePredAnd(stateOnly, stateOnly).IsTransitionAware, Is.False);
            Assert.That(new StatePredAnd(stateOnly, transition).IsTransitionAware, Is.True);
            Assert.That(new StatePredOr(transition, stateOnly).IsTransitionAware, Is.True);

            Assert.That(StatePredTrue.Instance.IsTransitionAware, Is.False);
            Assert.That(StatePredFalse.Instance.IsTransitionAware, Is.False);
        }

        [Test]
        public void Combinators_EvaluateOverTransitionContext()
        {
            var toIsTwo = new StatePredAtom(
                StateProp.OverTransition("to2", ctx => ((TestState)ctx.To).V == 2));
            var fromIsOne = new StatePredAtom(
                StateProp.OverTransition("from1", ctx => ((TestState)ctx.From).V == 1));

            var ctx = TransitionContext.Edge(S(1), new NamedStep("a"), null, S(2));

            Assert.That(new StatePredAnd(fromIsOne, toIsTwo).Eval(in ctx), Is.True);
            Assert.That(new StatePredNot(toIsTwo).Eval(in ctx), Is.False);
            Assert.That(new StatePredOr(new StatePredNot(fromIsOne), toIsTwo).Eval(in ctx), Is.True);
        }
    }
}

