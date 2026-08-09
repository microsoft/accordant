// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

[TestFixture]
public class FormulaBuilderTests
{
    private sealed class TestState : State
    {
        public bool Visible { get; set; }
        public int Phase { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new TestState
            {
                Visible = this.Visible,
                Phase = this.Phase,
            };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute)
            => $"Visible={this.Visible},Phase={this.Phase}";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AdvanceStep : BaseStepFunction
    {
        private readonly int fromPhase;
        private readonly int toPhase;
        private readonly bool visible;

        public AdvanceStep(int fromPhase, int toPhase, bool visible)
        {
            this.fromPhase = fromPhase;
            this.toPhase = toPhase;
            this.visible = visible;
        }

        public override string StepFunctionId => $"Advance_{this.fromPhase}_{this.toPhase}";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var current = (TestState)state;
            if (current.Phase != this.fromPhase)
            {
                return null;
            }

            var next = (TestState)current.Clone();
            next.Phase = this.toPhase;
            next.Visible = this.visible;
            return new[] { new StepResult { State = next } };
        }
    }

    [Test]
    public void DefaultBuilder_ExposesOnlyStutterSafeLanguage()
    {
        var methodNames = typeof(FormulaBuilder<TestState>)
            .GetMethods()
            .Select(method => method.Name)
            .ToHashSet();

        Assert.That(methodNames, Does.Not.Contain("Next"));
        Assert.That(methodNames, Does.Not.Contain("SeqPrefix"));
        Assert.That(methodNames, Does.Not.Contain("Trigger"));

        var observeOverloads = typeof(FormulaBuilder<TestState>)
            .GetMethods()
            .Where(method => method.Name == "Observe")
            .ToArray();
        Assert.That(observeOverloads, Has.Length.EqualTo(1));
    }

    [Test]
    public void FormulaTypes_PropagateSensitivityAtCompileTime()
    {
        var p = Formula.For<TestState>();
        var visible = p.Observe(state => state.Visible, "Visible");

        StutterSafeFormula safe = p.Always(p.Eventually(visible)) & !p.False;

        var sensitive = p.AllowStutterSensitiveFormulas();
        TemporalFormula next = sensitive.Next(visible);
        var changed = sensitive.ObserveTransition((state, nextState) =>
            state.Visible != nextState.Visible, "Changed");
        TemporalFormula transitionSensitive = sensitive.Always(changed);
        TemporalFormula mixed = safe & transitionSensitive;

        Assert.That(safe, Is.TypeOf<StutterSafeFormula>());
        Assert.That(next, Is.TypeOf<TemporalFormula>());
        Assert.That(transitionSensitive, Is.TypeOf<TemporalFormula>());
        Assert.That(mixed, Is.TypeOf<TemporalFormula>());
    }

    [Test]
    public void ObservationNames_AreInferred_AndCanBeOverridden()
    {
        var f = Formula.For<TestState>();

        var inferred = f.Observe(state => state.Visible);
        var explicitName = f.Observe(state => state.Visible, "Visible");
        var transitions = f.AllowStutterSensitiveFormulas();
        var inferredTransition = transitions.ObserveTransition(
            (state, nextState) => state.Visible != nextState.Visible);

        Assert.That(inferred.ToString(), Is.EqualTo("state => state.Visible"));
        Assert.That(explicitName.ToString(), Is.EqualTo("Visible"));
        Assert.That(
            inferredTransition.ToString(),
            Is.EqualTo("(state, nextState) => state.Visible != nextState.Visible"));
    }

    [Test]
    public void FormulaNames_AreOptional_AndPreserveSafeType()
    {
        var root = StateGraph.ExploreStateGraph(
            Array.Empty<IStepFunction>(),
            new TestState { Visible = true });
        var f = Formula.For<TestState>();
        var visible = f.Observe(state => state.Visible);

        StutterSafeFormula named = f.Always(visible).Named("Always visible");
        var namedResult = root.Check(named);
        var unnamedResult = root.Check(f.Always(visible));

        Assert.That(named.Name, Is.EqualTo("Always visible"));
        Assert.That(namedResult.PropertyName, Is.EqualTo("Always visible"));
        Assert.That(
            namedResult.GetTraceString(),
            Is.EqualTo("Property 'Always visible' holds - no counterexample."));
        Assert.That(unnamedResult.PropertyName, Is.Null);
    }

    [Test]
    public void NamedFailure_IdentifiesProperty_WithoutCombiningOperandNames()
    {
        var root = StateGraph.ExploreStateGraph(
            Array.Empty<IStepFunction>(),
            new TestState { Visible = false });
        var f = Formula.For<TestState>();
        var visible = f.Observe(state => state.Visible);
        var named = f.Always(visible).Named("Always visible");

        var result = root.Check(named);
        var combined = named & f.True.Named("True");

        Assert.That(result.Valid, Is.False);
        Assert.That(
            result.GetTraceString(),
            Does.StartWith("Counterexample for property 'Always visible':"));
        Assert.That(combined.Name, Is.Null);
    }

    [Test]
    public void SafeFormulas_AreUnaffectedByFiniteInvisibleRefinement()
    {
        var direct = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new AdvanceStep(0, 2, visible: true) },
            new TestState());
        var refined = StateGraph.ExploreStateGraph(
            new IStepFunction[]
            {
                new AdvanceStep(0, 1, visible: false),
                new AdvanceStep(1, 2, visible: true),
            },
            new TestState());

        var p = Formula.For<TestState>();
        var visible = p.Observe(state => state.Visible, "Visible");
        var formulas = new[]
        {
            p.Eventually(visible),
            p.Always(!visible | p.Eventually(visible)),
            p.Until(!visible, visible),
        };

        foreach (var formula in formulas)
        {
            Assert.That(
                refined.Check(formula).Valid,
                Is.EqualTo(direct.Check(formula).Valid),
                formula.ToString());
        }
    }
}
