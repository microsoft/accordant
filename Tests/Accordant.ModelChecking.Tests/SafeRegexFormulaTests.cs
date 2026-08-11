// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// Public-surface and end-to-end tests for the stutter-safe regular pattern
/// API — <see cref="SafeRegex"/> together with
/// <c>FormulaBuilder&lt;TState&gt;.After</c> and
/// <c>FormulaBuilder&lt;TState&gt;.Whenever</c>.
/// </summary>
[TestFixture]
public class SafeRegexFormulaTests
{
    #region Test infrastructure

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

    private sealed class Move : BaseStepFunction
    {
        private readonly string id;

        public Move(string id) { this.id = id; }

        public override string StepFunctionId => this.id;

        protected override IList<StepResult> ApplyInternal(IState state) => null;
    }

    /// <summary>
    /// A linear behaviour whose i-th edge goes from <c>counts[i]</c> to
    /// <c>counts[i + 1]</c>. Consecutive equal counts produce an <em>unchanged</em>
    /// edge; the final node is terminal, so the run ends in stutter.
    ///
    /// <para>Each node carries a distinct step function so that repeated states
    /// keep distinct node fingerprints — otherwise the product check would fold
    /// an inserted unchanged edge into an unchanged self-loop, which is an
    /// <em>infinite</em> stutter and thus a different behaviour.</para>
    /// </summary>
    private static StateGraphNode Chain(params int[] counts)
    {
        StateGraphNode next = null;
        for (int i = counts.Length - 1; i >= 0; i--)
        {
            var state = new CounterState { Count = counts[i] };
            state.Freeze();
            var move = new Move($"Move{i}");
            var node = new StateGraphNode
            {
                State = state,
                StepFunctions = next == null
                    ? new List<IStepFunction>()
                    : new List<IStepFunction> { move },
                Edges = next == null
                    ? new List<StateGraphEdge>()
                    : new List<StateGraphEdge>
                    {
                        new StateGraphEdge { Target = next, StepFunction = move },
                    },
            };
            next = node;
        }

        return next;
    }

    /// <summary>
    /// A step that increments the counter up to <paramref name="Max"/>. Used
    /// to build an <em>explored</em> graph (rather than a hand-built chain) so
    /// that unchanged edges are genuine named model edges.
    /// </summary>
    private sealed class IncrementStep : BaseStepFunction
    {
        private readonly int max;

        public IncrementStep(int max) { this.max = max; }

        public override string StepFunctionId => "Increment";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var counter = (CounterState)state;
            if (counter.Count >= this.max) return null;
            var next = (CounterState)counter.Clone();
            next.Count = counter.Count + 1;
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
    /// A named model edge that leaves the state semantically unchanged — the
    /// "no-op action" case, distinct from the synthetic terminal stutter.
    /// </summary>
    private sealed class IdleStep : BaseStepFunction
    {
        public override string StepFunctionId => "Idle";

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

    private static StateGraphNode Explored(bool withIdleEdges, int max = 2)
        => StateGraph.ExploreStateGraph(
            withIdleEdges
                ? new IStepFunction[] { new IncrementStep(max), new IdleStep() }
                : new IStepFunction[] { new IncrementStep(max) },
            new CounterState { Count = 0 });

    #endregion

    #region Public surface

    [Test]
    public void SafeBuilder_ExposesSafePatternOperators_ButNotSensitiveOnes()
    {
        var builderMethods = typeof(FormulaBuilder<CounterState>)
            .GetMethods()
            .Select(method => method.Name)
            .ToHashSet();

        Assert.That(builderMethods, Does.Contain("After"));
        Assert.That(builderMethods, Does.Contain("Whenever"));
        Assert.That(builderMethods, Does.Contain("ChangingStep"));
        Assert.That(builderMethods, Does.Not.Contain("Next"));
        Assert.That(builderMethods, Does.Not.Contain("SeqPrefix"));
        Assert.That(builderMethods, Does.Not.Contain("OvlPrefix"));
        Assert.That(builderMethods, Does.Not.Contain("Trigger"));
        Assert.That(builderMethods, Does.Not.Contain("Match"));
        Assert.That(builderMethods, Does.Not.Contain("Enabled"));

        var patternMethods = typeof(SafeRegex)
            .GetMethods()
            .Select(method => method.Name)
            .ToHashSet();

        Assert.That(patternMethods, Does.Contain("Then"));
        Assert.That(patternMethods, Does.Contain("Star"));
        Assert.That(patternMethods, Does.Contain("Plus"));
        Assert.That(patternMethods, Does.Contain("Optional"));
        Assert.That(patternMethods, Does.Contain("op_BitwiseOr"));
        Assert.That(patternMethods, Does.Contain("op_BitwiseAnd"));
        Assert.That(patternMethods, Does.Contain("op_LogicalNot"));
        Assert.That(patternMethods, Does.Not.Contain("Fusion"));
    }

    [Test]
    public void AfterAndWhenever_ReturnStutterSafeFormulas()
    {
        var f = Formula.For<CounterState>();
        var atZero = f.Observe(state => state.Count == 0);

        StutterSafeFormula after = f.After(f.AnyChangingStep, atZero);
        StutterSafeFormula whenever = f.Whenever(f.AnyChangingStep, atZero);

        Assert.That(after, Is.TypeOf<StutterSafeFormula>());
        Assert.That(whenever, Is.TypeOf<StutterSafeFormula>());
        Assert.That(
            f.Whenever(f.AnyChangingStep, atZero).Named("named"),
            Is.TypeOf<StutterSafeFormula>());
    }

    [Test]
    public void PatternOperators_RejectNullOperands()
    {
        var f = Formula.For<CounterState>();
        var step = f.AnyChangingStep;

        Assert.Throws<ArgumentNullException>(() => step.Then(null));
        Assert.Throws<ArgumentNullException>(() => f.ChangingStep((Observation)null));
        Assert.Throws<ArgumentNullException>(
            () => f.ChangingStep((TransitionObservation)null));
        Assert.Throws<ArgumentNullException>(() => f.After(null, f.True));
        Assert.Throws<ArgumentNullException>(() => f.Whenever(step, null));
    }

    #endregion

    #region End-to-end semantics

    private static IEnumerable<(string Name, StutterSafeFormula Formula, bool Expected)>
        Behaviours()
    {
        var f = Formula.For<CounterState>();
        var atZero = f.Observe(state => state.Count == 0, "Count==0");
        var atOne = f.Observe(state => state.Count == 1, "Count==1");
        var atTwo = f.Observe(state => state.Count == 2, "Count==2");
        var increments = f.ObserveTransition(
            (state, next) => next.Count == state.Count + 1, "Increment");

        var any = f.AnyChangingStep;
        var fromZero = f.ChangingStep(atZero);
        var fromOne = f.ChangingStep(atOne);
        var inc = f.ChangingStep(increments);

        yield return ("after 1 step ⇒ Count==1", f.After(any, atOne), true);
        yield return ("whenever 1 step ⇒ Count==1", f.Whenever(any, atOne), true);
        yield return ("whenever 2 steps ⇒ Count==2", f.Whenever(any.Then(any), atTwo), true);
        yield return ("after 3 steps ⇒ anything", f.After(any.Then(any).Then(any), f.True), false);
        yield return ("after 0·1 ⇒ Count==2", f.After(fromZero.Then(fromOne), atTwo), true);
        yield return ("whenever no steps ⇒ Count==0", f.Whenever(f.NoChangingSteps, atZero), true);
        yield return ("after no steps ⇒ Count==0", f.After(f.NoChangingSteps, atZero), true);
        yield return ("after ∅ ⇒ anything", f.After(f.NeverMatches, f.True), false);
        yield return ("whenever ∅ ⇒ false", f.Whenever(f.NeverMatches, f.False), true);
        yield return ("after ≥1 step ⇒ Count==2", f.After(!f.NoChangingSteps, atTwo), true);
        yield return ("whenever inc* ⇒ ◇Count==2",
            f.Whenever(inc.Star(), f.Eventually(atTwo)), true);
        yield return ("whenever inc+ ⇒ ¬Count==0",
            f.Whenever(inc.Plus(), !atZero), true);
        yield return ("whenever step? ⇒ Count≤1",
            f.Whenever(any.Optional(), atZero | atOne), true);

        // Visible-step counting: at every even-numbered changing step the
        // counter is 0 or 2, but not always 0.
        var evenSteps = any.Then(any).Star();
        yield return ("whenever even steps ⇒ Count∈{0,2}",
            f.Whenever(evenSteps, atZero | atTwo), true);
        yield return ("whenever even steps ⇒ Count==0",
            f.Whenever(evenSteps, atZero), false);

        // Boolean regex operators over visible steps.
        yield return ("whenever (0-step ∩ inc) ⇒ Count==1",
            f.Whenever(fromZero & inc, atOne), true);
        yield return ("after ¬(0-step) ⇒ Count==2",
            f.After(!fromZero, atTwo), true);
        yield return ("whenever (0-step | 1-step) ⇒ ¬Count==0",
            f.Whenever(fromZero | fromOne, !atZero), true);
    }

    [Test]
    public void SafePatterns_HaveTheExpectedMeaningOnTheDirectBehaviour()
    {
        var direct = Chain(0, 1, 2);

        foreach (var (name, formula, expected) in Behaviours())
        {
            Assert.That(direct.Check(formula).Valid, Is.EqualTo(expected), name);
        }
    }

    [TestCase(new[] { 0, 0, 1, 2 }, TestName = "LeadingStutter")]
    [TestCase(new[] { 0, 1, 1, 2 }, TestName = "InterleavedStutter")]
    [TestCase(new[] { 0, 1, 2, 2 }, TestName = "TrailingStutter")]
    [TestCase(new[] { 0, 0, 0, 1, 1, 1, 2, 2 }, TestName = "RepeatedStutter")]
    public void SafePatterns_AreInvariantUnderInsertedUnchangedSteps(int[] counts)
    {
        var direct = Chain(0, 1, 2);
        var stuttered = Chain(counts);

        foreach (var (name, formula, _) in Behaviours())
        {
            Assert.That(
                stuttered.Check(formula).Valid,
                Is.EqualTo(direct.Check(formula).Valid),
                $"{name} on [{string.Join(",", counts)}]");
        }
    }

    [Test]
    public void RawRegexPatterns_CanStillDistinguishStutterEquivalentBehaviours()
    {
        var direct = Chain(0, 1, 2);
        var stuttered = Chain(0, 0, 1, 2);

        var f = Formula.For<CounterState>();
        var sensitive = f.AllowStutterSensitiveFormulas();
        var atZero = f.Observe(state => state.Count == 0);
        var atOne = f.Observe(state => state.Count == 1);
        var atTwo = f.Observe(state => state.Count == 2);

        // Two *physical* letters whose sources are Count 0 then Count 1.
        RegexPattern raw = ((RegexPattern)atZero).Then(atOne);
        var sensitiveFormula = sensitive.SeqPrefix(raw, atTwo);

        Assert.That(direct.Check(sensitiveFormula).Valid, Is.True);
        Assert.That(stuttered.Check(sensitiveFormula).Valid, Is.False);

        // The safe counterpart is insensitive to the inserted unchanged edge.
        var safeFormula = f.After(
            f.ChangingStep(atZero).Then(f.ChangingStep(atOne)), atTwo);
        Assert.That(direct.Check(safeFormula).Valid, Is.True);
        Assert.That(stuttered.Check(safeFormula).Valid, Is.True);
    }

    [Test]
    public void ChangingStep_NeverMatchesAnUnchangedTransition()
    {
        // Every edge is unchanged, so no changing-step pattern can match and
        // the trigger holds vacuously while the existential prefix fails.
        var allUnchanged = Chain(0, 0, 0);
        var f = Formula.For<CounterState>();
        var stationary = f.ObserveTransition(
            (state, next) => next.Count == state.Count, "Stationary");

        Assert.That(
            allUnchanged.Check(f.After(f.ChangingStep(stationary), f.True)).Valid,
            Is.False);
        Assert.That(
            allUnchanged.Check(f.After(f.AnyChangingStep, f.True)).Valid,
            Is.False);
        Assert.That(
            allUnchanged.Check(f.Whenever(f.AnyChangingStep, f.False)).Valid,
            Is.True);
        Assert.That(
            allUnchanged.Check(
                f.Whenever(f.NoChangingSteps, f.Observe(state => state.Count == 0))).Valid,
            Is.True);
    }

    #endregion

    #region Stutter sources: named no-op edges and terminal stutter

    /// <summary>
    /// The two ways a run can stutter are covered separately by the tests
    /// below, both against graphs produced by real exploration:
    /// <list type="bullet">
    ///   <item>a <em>named unchanged model edge</em> — an action the model
    ///     really offers whose result is semantically equal to its source;</item>
    ///   <item>the <em>synthetic terminal stutter</em> — the self-loop the
    ///     checker adds at a node with no outgoing model edges, so that every
    ///     behaviour is infinite.</item>
    /// </list>
    /// </summary>
    [Test]
    public void NamedUnchangedModelEdges_AreInvisibleToSafePatterns()
    {
        var withIdle = Explored(withIdleEdges: true);
        var f = Formula.For<CounterState>();
        var atZero = f.Observe(state => state.Count == 0, "Count==0");
        var atTwo = f.Observe(state => state.Count == 2, "Count==2");
        var stationary = f.ObserveTransition(
            (state, next) => next.Count == state.Count, "Stationary");
        var any = f.AnyChangingStep;

        // The Idle edges exist in the graph, but no changing-step pattern can
        // consume one — not even a pattern that explicitly asks for a
        // stationary transition.
        Assert.That(
            withIdle.Check(f.After(f.ChangingStep(stationary), f.True)).Valid,
            Is.False,
            "a named unchanged model edge must not be matchable");
        Assert.That(
            withIdle.Check(f.Whenever(f.ChangingStep(stationary), f.False)).Valid,
            Is.True);

        // Counting is over changing steps only, so the Idle self-loops do not
        // shift the meaning of "after two steps".
        Assert.That(withIdle.Check(f.Whenever(any.Then(any), atTwo)).Valid, Is.True);
        Assert.That(withIdle.Check(f.Whenever(any, !atZero)).Valid, Is.True);
        Assert.That(
            withIdle.Check(f.Whenever(any.Then(any).Then(any), f.False)).Valid,
            Is.True,
            "the model has only two changing steps, however many Idle edges are taken");

        // The existential form needs the two changing steps to actually
        // happen. Idling forever is an admissible behaviour without fairness,
        // and weak fairness rules it out precisely because an unchanged edge
        // never counts as an occurrence.
        Assert.That(withIdle.Check(f.After(any.Then(any), atTwo)).Valid, Is.False);
        Assert.That(
            withIdle.Check(f.After(any.Then(any), atTwo), fairness: Fairness.WeakAll).Valid,
            Is.True);
    }

    /// <summary>
    /// Negative control for the previous test: the same Idle edges are plainly
    /// visible to a stutter-<em>sensitive</em> pattern, which counts physical
    /// letters.
    /// </summary>
    [Test]
    public void NamedUnchangedModelEdges_AreVisibleToSensitivePatterns()
    {
        var withIdle = Explored(withIdleEdges: true);
        var withoutIdle = Explored(withIdleEdges: false);
        var f = Formula.For<CounterState>();
        var sensitive = f.AllowStutterSensitiveFormulas();
        var atTwo = f.Observe(state => state.Count == 2, "Count==2");

        RegexPattern anyLetter = f.Observe(state => true, "AnyLetter");
        var twoLetters = anyLetter.Then(anyLetter);
        var afterTwoLetters = sensitive.Trigger(twoLetters, atTwo);

        Assert.That(
            withoutIdle.Check(afterTwoLetters).Valid,
            Is.True,
            "without no-op edges two physical letters always reach Count==2");
        Assert.That(
            withIdle.Check(afterTwoLetters).Valid,
            Is.False,
            "two Idle letters leave the counter at 0");

        // The safe reading of "after two changing steps" agrees on both graphs.
        var safe = f.Whenever(f.AnyChangingStep.Then(f.AnyChangingStep), atTwo);
        Assert.That(withoutIdle.Check(safe).Valid, Is.True);
        Assert.That(withIdle.Check(safe).Valid, Is.True);
    }

    /// <summary>
    /// The stutter-sensitive <see cref="RegexPattern"/> alphabet: a
    /// single physical letter is an <see cref="Observation"/> atom.
    /// <see cref="RegexPattern.Sigma"/> and <c>p | !p</c> both denote
    /// <em>every</em> word, because ERE complement is a whole-language
    /// complement.
    /// </summary>
    [Test]
    public void SinglePhysicalLetterPatterns_AreObservationAtoms()
    {
        var root = Explored(withIdleEdges: false);
        var f = Formula.For<CounterState>();
        var sensitive = f.AllowStutterSensitiveFormulas();
        var atZero = f.Observe(state => state.Count == 0, "Count==0");
        var atTwo = f.Observe(state => state.Count == 2, "Count==2");

        RegexPattern oneLetter = f.Observe(state => true, "AnyLetter");
        Assert.That(
            root.Check(sensitive.Trigger(oneLetter.Then(oneLetter), atTwo)).Valid,
            Is.True,
            "exactly two physical letters always reach Count==2");

        RegexPattern everyWord = atZero | !atZero;
        Assert.That(
            root.Check(sensitive.Trigger(everyWord.Then(everyWord), atTwo)).Valid,
            Is.False,
            "p | !p is every word, including the empty one");
        Assert.That(
            root.Check(sensitive.Trigger(RegexPattern.Sigma, atTwo)).Valid,
            Is.False,
            "Sigma is every word, not one letter");
    }

    [Test]
    public void TerminalSyntheticStutter_IsInvisibleToSafePatterns()
    {
        var root = Explored(withIdleEdges: false);
        var f = Formula.For<CounterState>();
        var atTwo = f.Observe(state => state.Count == 2, "Count==2");
        var stationary = f.ObserveTransition(
            (state, next) => next.Count == state.Count, "Stationary");
        var any = f.AnyChangingStep;

        // Count==2 is terminal, so the behaviour continues with an infinite
        // synthetic stutter. It contributes no visible steps.
        Assert.That(
            root.Check(f.After(any.Then(any).Then(any), f.True)).Valid,
            Is.False,
            "the terminal stutter must not supply a third changing step");
        Assert.That(
            root.Check(f.Whenever(any.Then(any).Then(any), f.False)).Valid,
            Is.True);
        Assert.That(
            root.Check(f.After(f.ChangingStep(stationary), f.True)).Valid,
            Is.False,
            "the terminal stutter must not be matchable either");
        Assert.That(root.Check(f.After(any.Then(any), atTwo)).Valid, Is.True);
        Assert.That(
            root.Check(f.Whenever(any.Star(), f.Eventually(atTwo))).Valid,
            Is.True);
    }

    /// <summary>
    /// Negative control: the synthetic terminal stutter is a real physical
    /// letter for the sensitive operators, which is why they can count past
    /// the end of the model's changing behaviour.
    /// </summary>
    [Test]
    public void TerminalSyntheticStutter_IsVisibleToSensitivePatterns()
    {
        var root = Explored(withIdleEdges: false);
        var f = Formula.For<CounterState>();
        var sensitive = f.AllowStutterSensitiveFormulas();
        var atZero = f.Observe(state => state.Count == 0, "Count==0");
        var atTwo = f.Observe(state => state.Count == 2, "Count==2");

        RegexPattern anyLetter = f.Observe(state => true, "AnyLetter");
        var threeLetters = anyLetter.Then(anyLetter).Then(anyLetter);

        Assert.That(
            root.Check(sensitive.SeqPrefix(threeLetters, atTwo)).Valid,
            Is.True,
            "the third physical letter is the terminal stutter");
        Assert.That(
            root.Check(f.After(
                f.AnyChangingStep.Then(f.AnyChangingStep).Then(f.AnyChangingStep),
                f.True)).Valid,
            Is.False);
    }

    #endregion

    #region Negative controls: the overlapping operators are not safe

    /// <summary>
    /// <c>OvlPrefix</c> hands the <em>last matched physical letter</em> to the
    /// suffix formula. One inserted unchanged step moves that letter, so the
    /// verdict flips — which is exactly why the stutter-safe builder offers no
    /// overlapping variant of <see cref="FormulaBuilder{TState}.After"/>.
    /// </summary>
    [Test]
    public void SensitiveOvlPrefix_IsNotStutterInvariant_WhileAfterIs()
    {
        var direct = Chain(0, 1, 2);
        var stuttered = Chain(0, 0, 1, 2);

        var f = Formula.For<CounterState>();
        var sensitive = f.AllowStutterSensitiveFormulas();
        var atZero = f.Observe(state => state.Count == 0, "Count==0");
        var atOne = f.Observe(state => state.Count == 1, "Count==1");
        var atTwo = f.Observe(state => state.Count == 2, "Count==2");

        RegexPattern anyLetter = f.Observe(state => true, "AnyLetter");
        var overlapping = sensitive.OvlPrefix(anyLetter.Then(anyLetter), atOne);

        Assert.That(
            direct.Check(overlapping).Valid,
            Is.True,
            "the second letter starts at Count==1");
        Assert.That(
            stuttered.Check(overlapping).Valid,
            Is.False,
            "one inserted unchanged step makes the second letter start at Count==0");

        // The safe, non-overlapping operator splits after the last *changing*
        // step and is unaffected.
        var safe = f.After(f.AnyChangingStep.Then(f.AnyChangingStep), atTwo);
        Assert.That(direct.Check(safe).Valid, Is.True);
        Assert.That(stuttered.Check(safe).Valid, Is.True);
    }

    /// <summary>
    /// The universal overlapping operator <c>Match</c> fails in the same way,
    /// while the safe <see cref="FormulaBuilder{TState}.Whenever"/> does not.
    /// </summary>
    [Test]
    public void SensitiveMatch_IsNotStutterInvariant_WhileWheneverIs()
    {
        var direct = Chain(0, 1, 2);
        var stuttered = Chain(0, 0, 1, 2);

        var f = Formula.For<CounterState>();
        var sensitive = f.AllowStutterSensitiveFormulas();
        var atZero = f.Observe(state => state.Count == 0, "Count==0");
        var atOne = f.Observe(state => state.Count == 1, "Count==1");
        var atTwo = f.Observe(state => state.Count == 2, "Count==2");

        RegexPattern anyLetter = f.Observe(state => true, "AnyLetter");
        var matching = sensitive.Match(anyLetter.Then(anyLetter), atOne);

        Assert.That(direct.Check(matching).Valid, Is.True);
        Assert.That(
            stuttered.Check(matching).Valid,
            Is.False,
            "the shared boundary letter moved with the inserted unchanged step");

        var safe = f.Whenever(f.AnyChangingStep.Then(f.AnyChangingStep), atTwo);
        Assert.That(direct.Check(safe).Valid, Is.True);
        Assert.That(stuttered.Check(safe).Valid, Is.True);
    }

    /// <summary>
    /// The overlapping operators are unsafe even when their pattern is a
    /// <see cref="SafeRegex"/>-shaped, changing-step-only requirement: it is
    /// the shared boundary letter, not the pattern, that leaks the physical
    /// position. Here the pattern asks for one changing step out of Count==0,
    /// and the overlapping suffix position is the last letter consumed.
    /// </summary>
    [Test]
    public void OverlappingOperators_LeakThePhysicalBoundary_EvenForChangingSteps()
    {
        var direct = Chain(0, 1, 2);
        var trailingStutter = Chain(0, 1, 1, 2);

        var f = Formula.For<CounterState>();
        var sensitive = f.AllowStutterSensitiveFormulas();
        var atZero = f.Observe(state => state.Count == 0, "Count==0");
        var atOne = f.Observe(state => state.Count == 1, "Count==1");

        // Σ-free two-letter prefix ending on a Count==1 letter.
        RegexPattern zeroThenOne = ((RegexPattern)atZero).Then(atOne);
        var overlapping = sensitive.OvlPrefix(zeroThenOne, sensitive.Next(f.Observe(
            state => state.Count == 2, "Count==2")));

        Assert.That(
            direct.Check(overlapping).Valid,
            Is.True,
            "the shared letter is 0→1, whose successor position has Count==2");
        Assert.That(
            trailingStutter.Check(overlapping).Valid,
            Is.False,
            "with a stutter after the first increment the successor position is Count==1");

        // The safe operators cannot express the overlap at all, and the
        // closest safe reading is invariant.
        var safe = f.After(
            f.ChangingStep(atZero),
            f.Eventually(f.Observe(state => state.Count == 2)));
        Assert.That(direct.Check(safe).Valid, Is.True);
        Assert.That(trailingStutter.Check(safe).Valid, Is.True);
    }

    #endregion
}
