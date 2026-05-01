// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Provides type-inferred expect methods for use within an Operation.
    /// This class knows both the response type and state type, allowing methods
    /// like <see cref="That(Func{TResponse, bool}, string)"/> and 
    /// <see cref="TypedExpectBuilder{TResponse, TState}.ThenState(Action{TState})"/>
    /// to work without explicit generic type parameters.
    /// </summary>
    /// <typeparam name="TResponse">The response type of the operation.</typeparam>
    /// <typeparam name="TState">The state type of the operation.</typeparam>
    public class ExpectContext<TResponse, TState>
        where TState : class, IState
    {
        /// <summary>
        /// Creates a new ExpectContext instance.
        /// </summary>
        public ExpectContext() { }

        /// <summary>
        /// Creates an expected outcome with a predicate-based response validator.
        /// The response type is inferred from the operation context.
        /// </summary>
        /// <param name="predicate">A predicate that returns true if the response is valid.</param>
        /// <param name="explanation">A description of what the predicate checks (used in error messages).</param>
        /// <returns>A <see cref="TypedExpectBuilder{TResponse, TState}"/> for further configuration.</returns>
        /// <example>
        /// return Expect.That(r => r > 0, "should be positive")
        ///              .ThenState(nextState => nextState.Count++);
        /// </example>
        public TypedExpectBuilder<TResponse, TState> That(
            Func<TResponse, bool> predicate,
            string explanation = null)
        {
            return new TypedExpectBuilder<TResponse, TState>(
                Expect.That<TResponse>(predicate, explanation));
        }

        /// <summary>
        /// Creates an expected outcome with a ValidationResult-returning validator.
        /// This overload allows including the actual response value in error messages.
        /// </summary>
        /// <param name="validator">
        /// A function that validates the response and returns a <see cref="ValidationResult"/>.
        /// </param>
        /// <returns>A <see cref="TypedExpectBuilder{TResponse, TState}"/> for further configuration.</returns>
        public TypedExpectBuilder<TResponse, TState> That(
            Func<TResponse, ValidationResult> validator)
        {
            return new TypedExpectBuilder<TResponse, TState>(
                Expect.That<TResponse>(validator));
        }

        /// <summary>
        /// Creates an expected outcome with a ResponseValidator.
        /// </summary>
        /// <param name="validator">A ResponseValidator that validates responses.</param>
        /// <returns>A <see cref="TypedExpectBuilder{TResponse, TState}"/> for further configuration.</returns>
        public TypedExpectBuilder<TResponse, TState> That(ResponseValidator validator)
        {
            return new TypedExpectBuilder<TResponse, TState>(
                Expect.That<TResponse>(validator));
        }

        /// <summary>
        /// Creates an expected outcome for an operation that should throw an exception.
        /// Use this when the operation is expected to throw rather than return normally.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="explanation">Optional explanation for error messages.</param>
        /// <returns>A <see cref="TypedExpectBuilder{Exception, TState}"/> for further configuration.</returns>
        /// <example>
        /// return Expect.Throws&lt;InvalidOperationException&gt;()
        ///              .SameState();
        /// </example>
        public TypedExpectBuilder<Exception, TState> Throws<TException>(
            string explanation = null)
            where TException : Exception
        {
            return new TypedExpectBuilder<Exception, TState>(
                Expect.Throws<TException>(explanation));
        }

        /// <summary>
        /// Creates an expected outcome for an operation that should throw an exception,
        /// with additional validation on the exception.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="predicate">Predicate to validate the exception.</param>
        /// <param name="explanation">Optional explanation for error messages.</param>
        /// <returns>A <see cref="TypedExpectBuilder{Exception, TState}"/> for further configuration.</returns>
        public TypedExpectBuilder<Exception, TState> Throws<TException>(
            Func<TException, bool> predicate,
            string explanation = null)
            where TException : Exception
        {
            return new TypedExpectBuilder<Exception, TState>(
                Expect.Throws<TException>(predicate, explanation));
        }

        /// <summary>
        /// Creates an expected outcome for a void-returning operation.
        /// Use this when the operation returns <see cref="Microsoft.Accordant.Unit"/> (i.e., performs an action but returns no meaningful value).
        /// </summary>
        /// <param name="explanation">Optional explanation for error messages.</param>
        /// <returns>A <see cref="TypedExpectBuilder{Unit, TState}"/> for further configuration.</returns>
        /// <example>
        /// return Expect.Unit()
        ///              .ThenState(nextState => nextState.Items.Add(item));
        /// </example>
        public TypedExpectBuilder<Unit, TState> Unit(string explanation = null)
        {
            return new TypedExpectBuilder<Unit, TState>(
                Expect.Unit(explanation));
        }

        /// <summary>
        /// Creates multiple expected outcomes (non-deterministic behavior).
        /// Use when an operation can have multiple valid outcomes.
        /// </summary>
        /// <param name="outcomes">The possible outcomes.</param>
        /// <returns>An <see cref="ExpectedOutcomes"/> containing all possibilities.</returns>
        public ExpectedOutcomes OneOf(params ExpectedOutcome[] outcomes)
        {
            return Expect.OneOf(outcomes);
        }

        /// <summary>
        /// Creates multiple expected outcomes from typed builders (non-deterministic behavior).
        /// Use when an operation can have multiple valid outcomes.
        /// </summary>
        /// <param name="builders">The outcome builders.</param>
        /// <returns>An <see cref="ExpectedOutcomes"/> containing all possibilities.</returns>
        public ExpectedOutcomes OneOf(params TypedExpectBuilder<TResponse, TState>[] builders)
        {
            var outcomes = new ExpectedOutcome[builders.Length];
            for (int i = 0; i < builders.Length; i++)
            {
                outcomes[i] = builders[i].Build();
            }
            return new ExpectedOutcomes(outcomes);
        }
    }

    /// <summary>
    /// A typed builder for constructing <see cref="ExpectedOutcome"/> instances fluently.
    /// This builder knows both the response type and state type, allowing methods like
    /// <see cref="ThenState(Action{TState})"/> to work without explicit generic type parameters.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    public class TypedExpectBuilder<TResponse, TState>
        where TState : class, IState
    {
        private readonly ExpectedOutcomeBuilder<TResponse> _inner;

        /// <summary>
        /// Gets the inner builder. Used by extension methods for State-specific operations.
        /// </summary>
        internal ExpectedOutcomeBuilder<TResponse> Inner => _inner;

        /// <summary>
        /// Creates a new TypedExpectBuilder wrapping an ExpectedOutcomeBuilder.
        /// </summary>
        /// <param name="inner">The inner builder to wrap.</param>
        public TypedExpectBuilder(ExpectedOutcomeBuilder<TResponse> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        #region ThenState - Modifier Lambda (Auto-Clone)

        /// <summary>
        /// Specifies a next state using a modifier action that mutates a pre-cloned state.
        /// The framework automatically clones the current state and passes it to the modifier.
        /// The state type is inferred from the operation context.
        /// </summary>
        /// <param name="modifier">An action that modifies the cloned state.</param>
        /// <returns>This builder for method chaining.</returns>
        /// <example>
        /// return Expect.That(r => r > 0, "positive")
        ///              .ThenState(nextState => nextState.Items.Add(request));
        /// </example>
        public TypedExpectBuilder<TResponse, TState> ThenState(Action<TState> modifier)
        {
            _inner.ThenState<TState>(modifier);
            return this;
        }

        /// <summary>
        /// Specifies a response-dependent next state using a modifier action that mutates a pre-cloned state.
        /// The framework automatically clones the current state and passes it to the modifier.
        /// </summary>
        /// <param name="modifier">An action that receives the response and modifies the cloned state.</param>
        /// <param name="mock">A function that generates a mock response for state exploration.</param>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> ThenState(
            Action<TResponse, TState> modifier,
            Func<TResponse> mock)
        {
            _inner.ThenState<TState>(modifier, mock);
            return this;
        }

        #endregion

        #region WithNextState - Direct State Replacement

        /// <summary>
        /// Specifies the exact next state to use after this operation.
        /// Use this for simple state types where you construct a new state instance rather than mutating a clone.
        /// </summary>
        /// <param name="nextState">The state to use after this operation.</param>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> WithNextState(IState nextState)
        {
            _inner.WithNextState(nextState);
            return this;
        }

        /// <summary>
        /// Specifies a response-dependent next state.
        /// Use this when the next state depends on values in the response (e.g., server-generated IDs).
        /// </summary>
        /// <param name="nextStateFunc">A function that takes the response and returns the next state.</param>
        /// <param name="mock">A function that generates a mock response for state exploration.</param>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> WithNextState(
            Func<TResponse, State> nextStateFunc,
            Func<TResponse> mock)
        {
            _inner.WithNextState(nextStateFunc, mock);
            return this;
        }

        #endregion

        #region SameState

        /// <summary>
        /// Indicates that the state is unchanged by this operation.
        /// When the outcome is matched, the original input state will be used.
        /// </summary>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> SameState()
        {
            _inner.SameState();
            return this;
        }

        #endregion

        #region Triggers - Step Functions

        /// <summary>
        /// Specifies that this operation triggers background activity via a step function.
        /// The step function runs concurrently with subsequent operations.
        /// </summary>
        /// <param name="stepFunction">The step function to trigger.</param>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> Triggers(IStepFunction stepFunction)
        {
            _inner.Triggers(stepFunction);
            return this;
        }

        /// <summary>
        /// Specifies that this operation triggers multiple background activities via step functions.
        /// The step functions run concurrently with subsequent operations.
        /// </summary>
        /// <param name="stepFunctions">The step functions to trigger.</param>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> Triggers(params IStepFunction[] stepFunctions)
        {
            _inner.Triggers(stepFunctions);
            return this;
        }

        /// <summary>
        /// Specifies that this operation triggers response-dependent background activity.
        /// The step function runs concurrently with subsequent operations.
        /// </summary>
        /// <param name="stepFunctionGenerator">
        /// A function that creates the step function given the response.
        /// </param>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> Triggers(
            Func<TResponse, IStepFunction> stepFunctionGenerator)
        {
            _inner.Triggers(stepFunctionGenerator);
            return this;
        }

        /// <summary>
        /// Specifies that this operation triggers response-dependent background activity
        /// that may result in multiple concurrent step functions.
        /// </summary>
        /// <param name="stepFunctionsGenerator">
        /// A function that creates the step functions given the response.
        /// </param>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> Triggers(
            Func<TResponse, IList<IStepFunction>> stepFunctionsGenerator)
        {
            _inner.Triggers(stepFunctionsGenerator);
            return this;
        }

        /// <summary>
        /// Specifies that this operation conditionally triggers a step function based on the response.
        /// The step function is only triggered when the predicate returns true.
        /// </summary>
        /// <param name="predicate">A function that returns true when the step function should be triggered.</param>
        /// <param name="stepFunction">The step function to trigger when the predicate is true.</param>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> TriggersWhen(
            Func<TResponse, bool> predicate,
            IStepFunction stepFunction)
        {
            _inner.TriggersWhen(predicate, stepFunction);
            return this;
        }

        /// <summary>
        /// Specifies that this operation conditionally triggers multiple step functions based on the response.
        /// The step functions are only triggered when the predicate returns true.
        /// </summary>
        /// <param name="predicate">A function that returns true when the step functions should be triggered.</param>
        /// <param name="stepFunctions">The step functions to trigger when the predicate is true.</param>
        /// <returns>This builder for method chaining.</returns>
        public TypedExpectBuilder<TResponse, TState> TriggersWhen(
            Func<TResponse, bool> predicate,
            params IStepFunction[] stepFunctions)
        {
            _inner.TriggersWhen(predicate, stepFunctions);
            return this;
        }

        #endregion

        #region Build

        /// <summary>
        /// Builds the <see cref="ExpectedOutcome"/> from this builder's configuration.
        /// </summary>
        /// <returns>The constructed expected outcome.</returns>
        public ExpectedOutcome Build()
        {
            return _inner.Build();
        }

        #endregion

        #region Implicit Conversions

        /// <summary>
        /// Implicit conversion to <see cref="ExpectedOutcome"/> for cleaner syntax.
        /// </summary>
        public static implicit operator ExpectedOutcome(TypedExpectBuilder<TResponse, TState> builder)
        {
            return builder.Build();
        }

        /// <summary>
        /// Implicit conversion to <see cref="ExpectedOutcomes"/> for cleaner syntax.
        /// Wraps this single outcome in an ExpectedOutcomes collection.
        /// </summary>
        public static implicit operator ExpectedOutcomes(TypedExpectBuilder<TResponse, TState> builder)
        {
            return new ExpectedOutcomes(builder.Build());
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for <see cref="TypedExpectBuilder{TResponse, TState}"/> that require
    /// TState to derive from <see cref="State"/> (not just implement <see cref="IState"/>).
    /// These methods use CloneWithMap which is a State-specific feature for cycle-aware cloning.
    /// </summary>
    public static class TypedExpectBuilderStateExtensions
    {
        /// <summary>
        /// Specifies a next state using a modifier action with access to the state clone map.
        /// The framework automatically clones the current state with CloneWithMap() and passes
        /// both the clone and the mapping from original to cloned states.
        /// </summary>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <typeparam name="TState">The state type, must derive from State.</typeparam>
        /// <param name="builder">The builder to extend.</param>
        /// <param name="modifier">An action that modifies the cloned state, with access to the clone map.</param>
        /// <returns>The builder for method chaining.</returns>
        public static TypedExpectBuilder<TResponse, TState> ThenState<TResponse, TState>(
            this TypedExpectBuilder<TResponse, TState> builder,
            Action<TState, Dictionary<object, object>> modifier)
            where TState : State
        {
            builder.Inner.ThenState<TState>(modifier);
            return builder;
        }

        /// <summary>
        /// Specifies a response-dependent next state with access to the state clone map.
        /// The framework automatically clones the current state with CloneWithMap() and passes
        /// both the clone and the mapping from original to cloned states.
        /// </summary>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <typeparam name="TState">The state type, must derive from State.</typeparam>
        /// <param name="builder">The builder to extend.</param>
        /// <param name="modifier">An action that receives the response and modifies the cloned state, with access to the clone map.</param>
        /// <param name="mock">A function that generates a mock response for state exploration.</param>
        /// <returns>The builder for method chaining.</returns>
        public static TypedExpectBuilder<TResponse, TState> ThenState<TResponse, TState>(
            this TypedExpectBuilder<TResponse, TState> builder,
            Action<TResponse, TState, Dictionary<object, object>> modifier,
            Func<TResponse> mock)
            where TState : State
        {
            builder.Inner.ThenState<TState>(modifier, mock);
            return builder;
        }
    }
}
