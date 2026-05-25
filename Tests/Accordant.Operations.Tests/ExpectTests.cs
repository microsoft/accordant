// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Operations.Tests
{
    using System;
    using Microsoft.Accordant;
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using System.Linq;
    using Microsoft.Accordant;
        using NUnit.Framework;

    /// <summary>
    /// Tests for the Expect fluent API.
    /// These tests verify that the Expect DSL correctly converts to ExpectedOutcome/ExpectedOutcomes.
    /// </summary>
    [TestFixture]
    public class ExpectTests
    {
        #region Basic Predicate Tests

        [Test]
        public void Expect_That_WithPredicate_CreatesValidOutcome()
        {
            var state = new CounterState(42);

            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "should be positive")
                                            .SameState();

            Assert.IsNotNull(outcome);
            Assert.IsNotNull(outcome.Validator);
            Assert.IsNotNull(outcome.NextStateGenerator);
            Assert.IsNotNull(outcome.NextStepFunctions);
        }

        [Test]
        public void Expect_That_WithPredicate_ValidatesCorrectly()
        {
            var state = new CounterState(42);

            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "should be positive")
                                            .SameState();

            // Positive number should pass
            var (isValid, _) = outcome.Matches(10, state);
            Assert.IsTrue(isValid);

            // Zero should fail
            (isValid, _) = outcome.Matches(0, state);
            Assert.IsFalse(isValid);

            // Negative should fail
            (isValid, _) = outcome.Matches(-5, state);
            Assert.IsFalse(isValid);
        }

        [Test]
        public void Expect_That_WithPredicate_ProvidesExplanationOnFailure()
        {
            var state = new CounterState(42);

            ExpectedOutcome outcome = Expect.That<int>(r => r > 100, "should be greater than 100")
                                            .SameState();

            var explanation = outcome.Explain(50);
            Assert.IsTrue(explanation.Contains("greater than 100"));
        }

        #endregion

        #region ValidationResult Overload Tests

        [Test]
        public void Expect_That_WithValidationResult_CreatesValidOutcome()
        {
            var state = new CounterState(42);

            ExpectedOutcome outcome = Expect.That<int>(r =>
                r > 0 ? ValidationResult.Valid() : ValidationResult.Invalid("must be positive"))
                .SameState();

            Assert.IsNotNull(outcome);
            Assert.IsNotNull(outcome.Validator);
            Assert.IsNotNull(outcome.NextStateGenerator);
        }

        [Test]
        public void Expect_That_WithValidationResult_ValidatesCorrectly()
        {
            var state = new CounterState(42);

            ExpectedOutcome outcome = Expect.That<int>(r =>
                r > 0 ? ValidationResult.Valid() : ValidationResult.Invalid("must be positive"))
                .SameState();

            // Positive number should pass
            var (isValid, _) = outcome.Matches(10, state);
            Assert.IsTrue(isValid);

            // Zero should fail
            (isValid, _) = outcome.Matches(0, state);
            Assert.IsFalse(isValid);

            // Negative should fail
            (isValid, _) = outcome.Matches(-5, state);
            Assert.IsFalse(isValid);
        }

        [Test]
        public void Expect_That_WithValidationResult_ProvidesRichErrorMessage()
        {
            var state = new CounterState(42);

            ExpectedOutcome outcome = Expect.That<int>(r =>
                r > 100
                    ? ValidationResult.Valid()
                    : ValidationResult.Invalid($"Expected value > 100 but got {r}"))
                .SameState();

            var explanation = outcome.Explain(50);
            Assert.IsTrue(explanation.Contains("50"), "Error message should include actual value");
            Assert.IsTrue(explanation.Contains("100"), "Error message should include threshold");
        }

        [Test]
        public void Expect_That_WithValidationResult_CanIncludeResponseDetails()
        {
            var state = new CounterState(42);

            // This demonstrates FluentAssertions-style detailed messages
            ExpectedOutcome outcome = Expect.That<int>(r =>
            {
                if (r < 0)
                    return ValidationResult.Invalid($"Value {r} is negative");
                if (r > 100)
                    return ValidationResult.Invalid($"Value {r} exceeds maximum of 100");
                return ValidationResult.Valid();
            }).SameState();

            // Check negative case
            var explanation = outcome.Explain(-5);
            Assert.IsTrue(explanation.Contains("-5"));
            Assert.IsTrue(explanation.Contains("negative"));

            // Check exceeds max case
            explanation = outcome.Explain(150);
            Assert.IsTrue(explanation.Contains("150"));
            Assert.IsTrue(explanation.Contains("100"));

            // Valid case should match
            var (isValid, _) = outcome.Matches(50, state);
            Assert.IsTrue(isValid);
        }

        [Test]
        public void Expect_That_WithValidationResult_NullValidatorThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>((Func<int, ValidationResult>)null);
            });
        }

        #endregion

        #region Action-Based ThenState Tests

        [Test]
        public void Expect_ThenState_ActionBased_SetsNextStateGenerator()
        {
            var originalState = new CounterState(10);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "always valid")
                                            .ThenState<CounterState>(nextState => nextState.Value = 20);

            Assert.IsNotNull(outcome.NextStateGenerator);
        }

        [Test]
        public void Expect_ThenState_ActionBased_ClonesAndModifiesState()
        {
            var currentState = new CounterState(10);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "always valid")
                                            .ThenState<CounterState>(nextState => nextState.Value = 42);

            var (isValid, stateProfile) = outcome.Matches(999, currentState);
            
            Assert.IsTrue(isValid);
            Assert.IsNotNull(stateProfile);
            Assert.AreEqual(1, stateProfile.StatesAndStepFunctions.Count);
            
            var resultState = (CounterState)stateProfile.StatesAndStepFunctions[0].Item1;
            Assert.AreEqual(42, resultState.Value);
            // Original state should be unchanged
            Assert.AreEqual(10, currentState.Value);
        }

        #endregion

        #region Response-Dependent State Tests (Action-Based)

        [Test]
        public void Expect_ThenState_WithResponseAction_SetsStateGenerator()
        {
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .ThenState<CounterState>(
                                                (resp, nextState) => nextState.Value = resp * 2,
                                                mock: () => 10);

            Assert.IsNotNull(outcome.NextStateGenerator);
            Assert.IsNotNull(outcome.MockResponseGenerator);
        }

        [Test]
        public void Expect_ThenState_WithResponseAction_GeneratesCorrectState()
        {
            var currentState = new CounterState(1);
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .ThenState<CounterState>(
                                                (resp, nextState) => nextState.Value = resp * 2,
                                                mock: () => 10);

            var (isValid, stateProfile) = outcome.Matches(5, currentState);
            
            Assert.IsTrue(isValid);
            Assert.IsNotNull(stateProfile);
            var resultState = (CounterState)stateProfile.StatesAndStepFunctions[0].Item1;
            Assert.AreEqual(10, resultState.Value); // 5 * 2 = 10
        }

        [Test]
        public void Expect_ThenState_WithResponseAction_MockIsAccessible()
        {
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .ThenState<CounterState>(
                                                (resp, nextState) => nextState.Value = resp,
                                                mock: () => 42);

            var mockValue = outcome.MockResponseGenerator();
            Assert.AreEqual(42, mockValue);
        }

        [Test]
        public void Expect_ThenState_WithResponseAction_RequiresMock()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => r > 0, "positive")
                      .ThenState<CounterState>(
                          (resp, nextState) => nextState.Value = resp,
                          mock: null);
            });
        }

        #endregion

        #region SameState Tests

        [Test]
        public void Expect_SameState_CreatesOutcomeWithStateGenerator()
        {
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .SameState();

            Assert.IsNotNull(outcome);
            Assert.IsNotNull(outcome.NextStateGenerator);
        }

        [Test]
        public void Expect_SameState_ReturnsCurrentState()
        {
            var currentState = new CounterState(42);
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .SameState();

            // Matches() returns the current state when SameState() is used
            var (isValid, stateProfile) = outcome.Matches(10, currentState);
            
            Assert.IsTrue(isValid);
            Assert.IsNotNull(stateProfile);
            Assert.AreEqual(1, stateProfile.StatesAndStepFunctions.Count);
            // State should be the current state
            Assert.AreEqual(currentState, stateProfile.StatesAndStepFunctions[0].Item1);
        }

        [Test]
        public void Expect_SameState_WorksWithTriggers()
        {
            var currentState = new CounterState(42);
            var stepFunction = new TestStepFunction(99);

            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .SameState()
                                            .Triggers(stepFunction);

            var (isValid, stateProfile) = outcome.Matches(10, currentState);
            
            Assert.IsTrue(isValid);
            // State should be the current state
            Assert.AreEqual(currentState, stateProfile.StatesAndStepFunctions[0].Item1);
            // Step function is still set
            Assert.Contains(stepFunction, (System.Collections.ICollection)stateProfile.StatesAndStepFunctions[0].Item2);
        }

        #endregion

        #region Triggers (Step Function) Tests

        [Test]
        public void Expect_Triggers_FixedStepFunction_SetsStepFunctions()
        {
            var stepFunction = new TestStepFunction();
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .Triggers(stepFunction);

            Assert.IsNotNull(outcome.NextStepFunctions);
        }

        [Test]
        public void Expect_Triggers_FixedStepFunction_IncludedInStateProfile()
        {
            var stepFunction = new TestStepFunction();
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .Triggers(stepFunction);

            var (isValid, stateProfile) = outcome.Matches(42, state);
            
            Assert.IsTrue(isValid);
            Assert.IsNotNull(stateProfile.StatesAndStepFunctions[0].Item2);
            Assert.Contains(stepFunction, (System.Collections.ICollection)stateProfile.StatesAndStepFunctions[0].Item2);
        }

        [Test]
        public void Expect_Triggers_ResponseDependentStepFunction_SetsGenerator()
        {
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .Triggers(resp => new TestStepFunction(resp));

            Assert.IsNotNull(outcome.NextStepFunctions);
        }

        [Test]
        public void Expect_Triggers_ResponseDependentStepFunction_GeneratesCorrectly()
        {
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .Triggers(resp => new TestStepFunction(resp));

            var (isValid, stateProfile) = outcome.Matches(99, state);
            
            Assert.IsTrue(isValid);
            var stepFunctions = stateProfile.StatesAndStepFunctions[0].Item2;
            Assert.AreEqual(1, stepFunctions.Count);
            Assert.AreEqual(99, ((TestStepFunction)stepFunctions[0]).Value);
        }

        #endregion

        #region Expect.OneOf Tests

        [Test]
        public void Expect_OneOf_CreatesExpectedOutcomes()
        {
            ExpectedOutcomes outcomes = Expect.OneOf(
                Expect.That<int>(r => r > 0, "positive").ThenState<CounterState>(nextState => nextState.Value = 1),
                Expect.That<int>(r => r <= 0, "non-positive").ThenState<CounterState>(nextState => nextState.Value = 2));

            Assert.IsNotNull(outcomes);
            Assert.AreEqual(2, outcomes.PossibleOutcomes.Count);
        }

        [Test]
        public void Expect_OneOf_MatchesFirstValidOutcome()
        {
            var currentState = new CounterState(0);

            ExpectedOutcomes outcomes = Expect.OneOf(
                Expect.That<int>(r => r > 0, "positive").ThenState<CounterState>(nextState => nextState.Value = 100),
                Expect.That<int>(r => r <= 0, "non-positive").ThenState<CounterState>(nextState => nextState.Value = 200));

            var (isValid, stateProfile) = outcomes.Matches(5, currentState);
            
            Assert.IsTrue(isValid);
            Assert.AreEqual(1, stateProfile.StatesAndStepFunctions.Count);
            var resultState = (CounterState)stateProfile.StatesAndStepFunctions[0].Item1;
            Assert.AreEqual(100, resultState.Value);
        }

        [Test]
        public void Expect_OneOf_MatchesSecondOutcomeWhenFirstFails()
        {
            var currentState = new CounterState(0);

            ExpectedOutcomes outcomes = Expect.OneOf(
                Expect.That<int>(r => r > 0, "positive").ThenState<CounterState>(nextState => nextState.Value = 100),
                Expect.That<int>(r => r <= 0, "non-positive").ThenState<CounterState>(nextState => nextState.Value = 200));

            var (isValid, stateProfile) = outcomes.Matches(-5, currentState);
            
            Assert.IsTrue(isValid);
            Assert.AreEqual(1, stateProfile.StatesAndStepFunctions.Count);
            var resultState = (CounterState)stateProfile.StatesAndStepFunctions[0].Item1;
            Assert.AreEqual(200, resultState.Value);
        }

        [Test]
        public void Expect_OneOf_MatchesBothWhenBothValid()
        {
            var currentState = new CounterState(0);

            // Both predicates accept values > 5
            ExpectedOutcomes outcomes = Expect.OneOf(
                Expect.That<int>(r => r > 5, "greater than 5").ThenState<CounterState>(nextState => nextState.Value = 100),
                Expect.That<int>(r => r > 0, "positive").ThenState<CounterState>(nextState => nextState.Value = 200));

            var (isValid, stateProfile) = outcomes.Matches(10, currentState);
            
            Assert.IsTrue(isValid);
            // Both outcomes match, so both states are possible
            Assert.AreEqual(2, stateProfile.StatesAndStepFunctions.Count);
        }

        [Test]
        public void Expect_OneOf_FailsWhenNoOutcomesMatch()
        {
            var currentState = new CounterState(0);

            ExpectedOutcomes outcomes = Expect.OneOf(
                Expect.That<int>(r => r > 100, "greater than 100").ThenState<CounterState>(nextState => nextState.Value = 100),
                Expect.That<int>(r => r < -100, "less than -100").ThenState<CounterState>(nextState => nextState.Value = 200));

            var (isValid, stateProfile) = outcomes.Matches(0, currentState);
            
            Assert.IsFalse(isValid);
            Assert.IsNull(stateProfile);
        }

        [Test]
        public void Expect_OneOf_WithBuilders_Works()
        {
            // Using builders directly (not calling .Build())
            ExpectedOutcomes outcomes = Expect.OneOf(
                Expect.That<int>(r => r > 0, "positive").ThenState<CounterState>(nextState => nextState.Value = 1),
                Expect.That<int>(r => r <= 0, "non-positive").ThenState<CounterState>(nextState => nextState.Value = 2));

            Assert.AreEqual(2, outcomes.PossibleOutcomes.Count);
        }

        [Test]
        public void Expect_OneOf_RequiresAtLeastOneOutcome()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                Expect.OneOf(Array.Empty<ExpectedOutcome>());
            });
        }

        #endregion

        #region Implicit Conversion Tests

        [Test]
        public void ExpectedOutcomeBuilder_ImplicitlyConvertsToExpectedOutcome()
        {
            // Implicit conversion
            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid").ThenState<CounterState>(nextState => nextState.Value = 1);
            
            Assert.IsNotNull(outcome);
            Assert.IsNotNull(outcome.NextStateGenerator);
        }

        [Test]
        public void ExpectedOutcomeBuilder_ImplicitlyConvertsToExpectedOutcomes()
        {
            // Implicit conversion to ExpectedOutcomes (wraps in collection)
            ExpectedOutcomes outcomes = Expect.That<int>(r => true, "valid").ThenState<CounterState>(nextState => nextState.Value = 1);
            
            Assert.IsNotNull(outcomes);
            Assert.AreEqual(1, outcomes.PossibleOutcomes.Count);
        }

        #endregion

        #region Error Handling Tests

        [Test]
        public void Expect_That_WithNullPredicate_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(null, "explanation");
            });
        }

        [Test]
        public void Expect_Build_WithoutThenState_Throws()
        {
            var builder = Expect.That<int>(r => true, "valid");
            
            Assert.Throws<InvalidOperationException>(() =>
            {
                builder.Build();
            });
        }

        #endregion

        #region WithNextState Tests

        [Test]
        public void Expect_WithNextState_SetsNextStateGenerator()
        {
            var nextState = new CounterState(42);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "always valid")
                                            .WithNextState(nextState);

            Assert.IsNotNull(outcome.NextStateGenerator);
        }

        [Test]
        public void Expect_WithNextState_ReturnsExactState()
        {
            var currentState = new CounterState(10);
            var nextState = new CounterState(42);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "always valid")
                                            .WithNextState(nextState);

            var (isValid, stateProfile) = outcome.Matches(999, currentState);
            
            Assert.IsTrue(isValid);
            Assert.IsNotNull(stateProfile);
            Assert.AreEqual(1, stateProfile.StatesAndStepFunctions.Count);
            
            // Should be the exact same instance we provided
            Assert.AreSame(nextState, stateProfile.StatesAndStepFunctions[0].Item1);
        }

        [Test]
        public void Expect_WithNextState_DoesNotModifyCurrentState()
        {
            var currentState = new CounterState(10);
            var nextState = new CounterState(42);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "always valid")
                                            .WithNextState(nextState);

            outcome.Matches(999, currentState);
            
            // Original state should be unchanged
            Assert.AreEqual(10, currentState.Value);
        }

        [Test]
        public void Expect_WithNextState_WorksWithTriggers()
        {
            var nextState = new CounterState(42);
            var stepFunction = new TestStepFunction(99);

            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .WithNextState(nextState)
                                            .Triggers(stepFunction);

            var (isValid, stateProfile) = outcome.Matches(10, new CounterState(0));
            
            Assert.IsTrue(isValid);
            Assert.AreSame(nextState, stateProfile.StatesAndStepFunctions[0].Item1);
            Assert.Contains(stepFunction, (System.Collections.ICollection)stateProfile.StatesAndStepFunctions[0].Item2);
        }

        [Test]
        public void Expect_WithNextState_NullThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => true, "valid").WithNextState((State)null);
            });
        }

        [Test]
        public void Expect_WithNextState_ResponseDependent_SetsStateGenerator()
        {
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .WithNextState(
                                                resp => new CounterState(resp * 2),
                                                mock: () => 10);

            Assert.IsNotNull(outcome.NextStateGenerator);
            Assert.IsNotNull(outcome.MockResponseGenerator);
        }

        [Test]
        public void Expect_WithNextState_ResponseDependent_GeneratesCorrectState()
        {
            var currentState = new CounterState(1);
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .WithNextState(
                                                resp => new CounterState(resp * 3),
                                                mock: () => 10);

            var (isValid, stateProfile) = outcome.Matches(5, currentState);
            
            Assert.IsTrue(isValid);
            Assert.IsNotNull(stateProfile);
            var resultState = (CounterState)stateProfile.StatesAndStepFunctions[0].Item1;
            Assert.AreEqual(15, resultState.Value); // 5 * 3 = 15
        }

        [Test]
        public void Expect_WithNextState_ResponseDependent_MockIsAccessible()
        {
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .WithNextState(
                                                resp => new CounterState(resp),
                                                mock: () => 42);

            var mockValue = outcome.MockResponseGenerator();
            Assert.AreEqual(42, mockValue);
        }

        [Test]
        public void Expect_WithNextState_ResponseDependent_RequiresMock()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => r > 0, "positive")
                      .WithNextState(resp => new CounterState(resp), mock: null);
            });
        }

        [Test]
        public void Expect_WithNextState_ResponseDependent_NullFuncThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => r > 0, "positive")
                      .WithNextState((Func<int, State>)null, mock: () => 10);
            });
        }

        #endregion

        #region Expect.Throws Tests

        [Test]
        public void Expect_Throws_CreatesValidOutcome()
        {
            ExpectedOutcome outcome = Expect.Throws<InvalidOperationException>()
                                            .SameState();

            Assert.IsNotNull(outcome);
            Assert.IsNotNull(outcome.Validator);
        }

        [Test]
        public void Expect_Throws_MatchesCorrectExceptionType()
        {
            var state = new CounterState(42);
            ExpectedOutcome outcome = Expect.Throws<InvalidOperationException>()
                                            .SameState();

            var ex = new InvalidOperationException("test");
            var (isValid, _) = outcome.Matches(ex, state);
            Assert.IsTrue(isValid);
        }

        [Test]
        public void Expect_Throws_RejectsWrongExceptionType()
        {
            var state = new CounterState(42);
            ExpectedOutcome outcome = Expect.Throws<InvalidOperationException>()
                                            .SameState();

            var ex = new ArgumentException("test");
            var (isValid, _) = outcome.Matches(ex, state);
            Assert.IsFalse(isValid);
        }

        [Test]
        public void Expect_Throws_RejectsNonException()
        {
            var state = new CounterState(42);
            ExpectedOutcome outcome = Expect.Throws<InvalidOperationException>()
                                            .SameState();

            var (isValid, _) = outcome.Matches("not an exception", state);
            Assert.IsFalse(isValid);
        }

        [Test]
        public void Expect_Throws_WithPredicate_ValidatesException()
        {
            var state = new CounterState(42);
            ExpectedOutcome outcome = Expect.Throws<InvalidOperationException>(
                ex => ex.Message.Contains("specific"),
                "should contain 'specific' in message")
                .SameState();

            var matchingEx = new InvalidOperationException("specific error");
            var (isValid, _) = outcome.Matches(matchingEx, state);
            Assert.IsTrue(isValid);

            var nonMatchingEx = new InvalidOperationException("other error");
            (isValid, _) = outcome.Matches(nonMatchingEx, state);
            Assert.IsFalse(isValid);
        }

        [Test]
        public void Expect_Throws_WorksWithNextState()
        {
            var nextState = new CounterState(99);
            var currentState = new CounterState(42);

            ExpectedOutcome outcome = Expect.Throws<InvalidOperationException>()
                                            .WithNextState(nextState);

            var ex = new InvalidOperationException("test");
            var (isValid, stateProfile) = outcome.Matches(ex, currentState);
            
            Assert.IsTrue(isValid);
            Assert.AreSame(nextState, stateProfile.StatesAndStepFunctions[0].Item1);
        }

        #endregion

        #region Expect.Unit Tests

        [Test]
        public void Expect_Unit_CreatesValidOutcome()
        {
            ExpectedOutcome outcome = Expect.Unit()
                                            .SameState();

            Assert.IsNotNull(outcome);
            Assert.IsNotNull(outcome.Validator);
        }

        [Test]
        public void Expect_Unit_MatchesUnitValue()
        {
            var state = new CounterState(42);
            ExpectedOutcome outcome = Expect.Unit()
                                            .SameState();

            var (isValid, _) = outcome.Matches(Unit.Value, state);
            Assert.IsTrue(isValid);
        }

        [Test]
        public void Expect_Unit_WithExplanation_HasExplanation()
        {
            ExpectedOutcome outcome = Expect.Unit("custom explanation")
                                            .SameState();

            Assert.IsNotNull(outcome);
        }

        [Test]
        public void Expect_Unit_WorksWithNextState()
        {
            var currentState = new CounterState(10);
            var nextState = new CounterState(20);

            ExpectedOutcome outcome = Expect.Unit()
                                            .WithNextState(nextState);

            var (isValid, stateProfile) = outcome.Matches(Unit.Value, currentState);
            
            Assert.IsTrue(isValid);
            Assert.AreSame(nextState, stateProfile.StatesAndStepFunctions[0].Item1);
        }

        #endregion

        #region ThenStateWithMap Tests

        [Test]
        public void Expect_ThenStateWithMap_SetsNextStateGenerator()
        {
            ExpectedOutcome outcome = Expect.That<int>(r => true, "always valid")
                                            .ThenStateWithMap<CounterState>((nextState, cloneMap) =>
                                            {
                                                nextState.Value = 42;
                                            });

            Assert.IsNotNull(outcome.NextStateGenerator);
        }

        [Test]
        public void Expect_ThenStateWithMap_ProvidesCloneMap()
        {
            var currentState = new CounterState(10);
            Dictionary<object, object> capturedMap = null;

            ExpectedOutcome outcome = Expect.That<int>(r => true, "always valid")
                                            .ThenStateWithMap<CounterState>((nextState, cloneMap) =>
                                            {
                                                capturedMap = cloneMap;
                                                nextState.Value = 42;
                                            });

            outcome.Matches(999, currentState);
            
            Assert.IsNotNull(capturedMap);
            Assert.IsTrue(capturedMap.ContainsKey(currentState));
        }

        [Test]
        public void Expect_ThenStateWithMap_NullModifierThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => true, "valid")
                      .ThenStateWithMap<CounterState>((Action<CounterState, Dictionary<object, object>>)null);
            });
        }

        [Test]
        public void Expect_ThenStateWithMap_WithResponse_SetsNextStateGenerator()
        {
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .ThenStateWithMap<CounterState>(
                                                (resp, nextState, cloneMap) => nextState.Value = resp,
                                                mock: () => 10);

            Assert.IsNotNull(outcome.NextStateGenerator);
            Assert.IsNotNull(outcome.MockResponseGenerator);
        }

        [Test]
        public void Expect_ThenStateWithMap_WithResponse_GeneratesCorrectState()
        {
            var currentState = new CounterState(1);
            ExpectedOutcome outcome = Expect.That<int>(r => r > 0, "positive")
                                            .ThenStateWithMap<CounterState>(
                                                (resp, nextState, cloneMap) => nextState.Value = resp * 4,
                                                mock: () => 10);

            var (isValid, stateProfile) = outcome.Matches(7, currentState);
            
            Assert.IsTrue(isValid);
            var resultState = (CounterState)stateProfile.StatesAndStepFunctions[0].Item1;
            Assert.AreEqual(28, resultState.Value); // 7 * 4 = 28
        }

        [Test]
        public void Expect_ThenStateWithMap_WithResponse_RequiresMock()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => r > 0, "positive")
                      .ThenStateWithMap<CounterState>(
                          (resp, nextState, cloneMap) => nextState.Value = resp,
                          mock: null);
            });
        }

        #endregion

        #region ResponseValidator Tests

        [Test]
        public void Expect_That_NonGenericResponseValidator_CreatesValidOutcome()
        {
            var validator = new ResponseValidator(r =>
            {
                var intVal = (int)r;
                return intVal > 0 ? ValidationResult.Valid() : ValidationResult.Invalid("must be positive");
            });

            ExpectedOutcome outcome = Expect.That(validator)
                                            .SameState();

            Assert.IsNotNull(outcome);
            Assert.IsNotNull(outcome.Validator);
        }

        [Test]
        public void Expect_That_NonGenericResponseValidator_ValidatesCorrectly()
        {
            var state = new CounterState(42);
            var validator = new ResponseValidator(r =>
            {
                var intVal = (int)r;
                return intVal > 0 ? ValidationResult.Valid() : ValidationResult.Invalid("must be positive");
            });

            ExpectedOutcome outcome = Expect.That(validator)
                                            .SameState();

            var (isValid, _) = outcome.Matches(10, state);
            Assert.IsTrue(isValid);

            (isValid, _) = outcome.Matches(-5, state);
            Assert.IsFalse(isValid);
        }

        [Test]
        public void Expect_That_GenericResponseValidator_CreatesValidOutcome()
        {
            var validator = new ResponseValidator(r =>
            {
                var intVal = (int)r;
                return intVal > 0 ? ValidationResult.Valid() : ValidationResult.Invalid("must be positive");
            });

            ExpectedOutcome outcome = Expect.That<int>(validator)
                                            .SameState();

            Assert.IsNotNull(outcome);
        }

        [Test]
        public void Expect_That_NullResponseValidator_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That((ResponseValidator)null);
            });
        }

        #endregion

        #region Multiple Triggers Tests

        [Test]
        public void Expect_Triggers_MultipleStepFunctions_SetsAllStepFunctions()
        {
            var stepFunction1 = new TestStepFunction(1);
            var stepFunction2 = new TestStepFunction(2);
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .Triggers(stepFunction1, stepFunction2);

            var (isValid, stateProfile) = outcome.Matches(42, state);
            
            Assert.IsTrue(isValid);
            var stepFunctions = stateProfile.StatesAndStepFunctions[0].Item2;
            Assert.AreEqual(2, stepFunctions.Count);
            Assert.Contains(stepFunction1, (System.Collections.ICollection)stepFunctions);
            Assert.Contains(stepFunction2, (System.Collections.ICollection)stepFunctions);
        }

        [Test]
        public void Expect_Triggers_EmptyArray_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                Expect.That<int>(r => true, "valid")
                      .SameState()
                      .Triggers(Array.Empty<IStepFunction>());
            });
        }

        [Test]
        public void Expect_Triggers_NullStepFunction_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => true, "valid")
                      .SameState()
                      .Triggers((IStepFunction)null);
            });
        }

        [Test]
        public void Expect_Triggers_ResponseDependentMultiple_Works()
        {
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .Triggers(resp => new[] { new TestStepFunction(resp), new TestStepFunction(resp * 2) });

            var (isValid, stateProfile) = outcome.Matches(5, state);
            
            Assert.IsTrue(isValid);
            var stepFunctions = stateProfile.StatesAndStepFunctions[0].Item2;
            Assert.AreEqual(2, stepFunctions.Count);
            Assert.AreEqual(5, ((TestStepFunction)stepFunctions[0]).Value);
            Assert.AreEqual(10, ((TestStepFunction)stepFunctions[1]).Value);
        }

        #endregion

        #region TriggersWhen Tests

        [Test]
        public void Expect_TriggersWhen_PredicateTrue_IncludesStepFunction()
        {
            var stepFunction = new TestStepFunction(42);
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .TriggersWhen(r => r > 0, stepFunction);

            var (isValid, stateProfile) = outcome.Matches(5, state);

            Assert.IsTrue(isValid);
            var stepFunctions = stateProfile.StatesAndStepFunctions[0].Item2;
            Assert.AreEqual(1, stepFunctions.Count);
            Assert.AreSame(stepFunction, stepFunctions[0]);
        }

        [Test]
        public void Expect_TriggersWhen_PredicateFalse_NoStepFunction()
        {
            var stepFunction = new TestStepFunction(42);
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .TriggersWhen(r => r < 0, stepFunction);  // false for positive response

            var (isValid, stateProfile) = outcome.Matches(5, state);

            Assert.IsTrue(isValid);
            var stepFunctions = stateProfile.StatesAndStepFunctions[0].Item2;
            Assert.AreEqual(0, stepFunctions.Count);  // No step function triggered
        }

        [Test]
        public void Expect_TriggersWhen_NullPredicateThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => true, "valid")
                      .SameState()
                      .TriggersWhen(null, new TestStepFunction(1));
            });
        }

        [Test]
        public void Expect_TriggersWhen_NullStepFunctionThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => true, "valid")
                      .SameState()
                      .TriggersWhen(r => true, (IStepFunction)null);
            });
        }

        [Test]
        public void Expect_TriggersWhen_MultipleStepFunctions_PredicateTrue_IncludesAll()
        {
            var sf1 = new TestStepFunction(1);
            var sf2 = new TestStepFunction(2);
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .TriggersWhen(r => r > 0, sf1, sf2);

            var (isValid, stateProfile) = outcome.Matches(5, state);

            Assert.IsTrue(isValid);
            var stepFunctions = stateProfile.StatesAndStepFunctions[0].Item2;
            Assert.AreEqual(2, stepFunctions.Count);
        }

        [Test]
        public void Expect_TriggersWhen_MultipleStepFunctions_PredicateFalse_NoStepFunctions()
        {
            var sf1 = new TestStepFunction(1);
            var sf2 = new TestStepFunction(2);
            var state = new CounterState(1);

            ExpectedOutcome outcome = Expect.That<int>(r => true, "valid")
                                            .SameState()
                                            .TriggersWhen(r => r < 0, sf1, sf2);

            var (isValid, stateProfile) = outcome.Matches(5, state);

            Assert.IsTrue(isValid);
            var stepFunctions = stateProfile.StatesAndStepFunctions[0].Item2;
            Assert.AreEqual(0, stepFunctions.Count);
        }

        #endregion

        #region Additional Null Validation Tests

        [Test]
        public void Expect_ThenState_ActionBased_NullModifierThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => true, "valid")
                      .ThenState<CounterState>((Action<CounterState>)null);
            });
        }

        [Test]
        public void Expect_ThenState_ResponseAction_NullModifierThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Expect.That<int>(r => true, "valid")
                      .ThenState<CounterState>(
                          (Action<int, CounterState>)null,
                          mock: () => 10);
            });
        }

        #endregion

        #region Integration with Operation Tests

        [Test]
        public void Expect_WorksInOperationApply()
        {
            var operation = new ExpectBasedOperation();
            var state = new CounterState(10);

            var outcomes = operation.Apply(5, state);
            
            Assert.IsNotNull(outcomes);
            Assert.AreEqual(1, outcomes.PossibleOutcomes.Count);

            var (isValid, stateProfile) = outcomes.Matches(15, state);
            Assert.IsTrue(isValid);
            
            var newState = (CounterState)stateProfile.StatesAndStepFunctions[0].Item1;
            Assert.AreEqual(15, newState.Value);
        }

        [Test]
        public void Expect_OneOf_WorksInOperationApply()
        {
            var operation = new ExpectOneOfBasedOperation();
            var state = new CounterState(10);

            var outcomes = operation.Apply(5, state);
            
            // Should have 2 possible outcomes
            Assert.AreEqual(2, outcomes.PossibleOutcomes.Count);

            // Success case
            var (isValid, stateProfile) = outcomes.Matches(15, state);
            Assert.IsTrue(isValid);

            // Error case
            (isValid, stateProfile) = outcomes.Matches(-1, state);
            Assert.IsTrue(isValid);
        }

        #endregion

        #region TypedExpectBuilder Extension Method Tests

        /// <summary>
        /// Tests that ThenStateWithMap works via extension method when TState derives from State.
        /// This verifies the extension method approach for State-specific CloneWithMap functionality.
        /// </summary>
        [Test]
        public void TypedExpectBuilder_ThenStateWithMap_WorksViaExtensionMethod()
        {
            // Create an operation that uses ThenState with clone map
            var operation = new CloneMapOperation();
            var state = new CounterState(10);

            // Apply should work - the extension method provides ThenStateWithMap(Action<TState, Dictionary<object,object>>)
            var outcomes = operation.Apply(5, state);

            Assert.IsNotNull(outcomes);
            Assert.AreEqual(1, outcomes.PossibleOutcomes.Count);

            // Verify the outcome works correctly
            var (isValid, stateProfile) = outcomes.Matches(15, state);
            Assert.IsTrue(isValid);

            // Verify the state was modified correctly
            var nextStates = stateProfile.StatesAndStepFunctions;
            Assert.AreEqual(1, nextStates.Count);
            var nextState = (CounterState)nextStates[0].Item1;
            Assert.AreEqual(15, nextState.Value);
        }

        /// <summary>
        /// Tests that ThenStateWithMap with response works via extension method.
        /// </summary>
        [Test]
        public void TypedExpectBuilder_ThenStateWithMap_WithResponse_WorksViaExtensionMethod()
        {
            var operation = new CloneMapWithResponseOperation();
            var state = new CounterState(10);

            var outcomes = operation.Apply(5, state);

            Assert.IsNotNull(outcomes);
            Assert.AreEqual(1, outcomes.PossibleOutcomes.Count);

            // Verify with a mock response
            var (isValid, stateProfile) = outcomes.Matches(15, state);
            Assert.IsTrue(isValid);
        }

        /// <summary>
        /// Operation that uses ThenStateWithMap (Dictionary parameter).
        /// This tests the extension method that requires TState : State.
        /// </summary>
        private class CloneMapOperation : Operation<int, int, CounterState>
        {
            public CloneMapOperation() : base("CloneMap") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                var expectedValue = state.Value + request;
                // Use the ThenStateWithMap overload - this uses the extension method
                return Expect.That(r => r == expectedValue, $"should equal {expectedValue}")
                             .ThenStateWithMap((nextState, cloneMap) =>
                             {
                                 // cloneMap is available for cycle-aware operations
                                 Assert.IsNotNull(cloneMap);
                                 nextState.Value = expectedValue;
                             });
            }
        }

        /// <summary>
        /// Operation that uses ThenStateWithMap with response.
        /// </summary>
        private class CloneMapWithResponseOperation : Operation<int, int, CounterState>
        {
            public CloneMapWithResponseOperation() : base("CloneMapWithResponse") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                var expectedValue = state.Value + request;
                return Expect.That(r => r == expectedValue, $"should equal {expectedValue}")
                             .ThenStateWithMap(
                                 (response, nextState, cloneMap) =>
                                 {
                                     Assert.IsNotNull(cloneMap);
                                     nextState.Value = response; // Use response directly
                                 },
                                 () => expectedValue); // Mock for exploration
            }
        }

        #endregion

        #region Helper Classes

        private class TestStepFunction : BaseStepFunction
        {
            public int Value { get; }

            public TestStepFunction(int value = 0)
            {
                Value = value;
            }

            protected override IList<StepResult> ApplyInternal(IState state, IReadOnlyList<(IStepFunction, StateGraphNode)> path)
            {
                return new List<StepResult> { new StepResult { State = state } };
            }
        }

        private class ExpectBasedOperation : Operation<int, int, CounterState>
        {
            public ExpectBasedOperation() : base("ExpectBased") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                var expectedValue = state.Value + request;
                return Expect.That(r => r == expectedValue, $"should equal {expectedValue}")
                             .ThenState(nextState => nextState.Value = expectedValue);
            }
        }

        private class ExpectOneOfBasedOperation : Operation<int, int, CounterState>
        {
            public ExpectOneOfBasedOperation() : base("ExpectOneOfBased") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                var expectedValue = state.Value + request;
                return Expect.OneOf(
                    Expect.That(r => r == expectedValue, "success")
                          .ThenState(nextState => nextState.Value = expectedValue),
                    Expect.That(r => r == -1, "error indicator")
                          .SameState());
            }
        }

        #endregion
    }
}
