// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Fluent API for defining expected outcomes in Operation.Apply methods.
    /// Provides a more readable way to construct <see cref="ExpectedOutcome"/> and <see cref="ExpectedOutcomes"/>.
    /// </summary>
    /// <example>
    /// // Simple case - predicate validation, state unchanged
    /// return Expect.That&lt;int&gt;(r => r == state.Value, "should equal current value");
    /// 
    /// // With state transition
    /// return Expect.That&lt;int&gt;(r => r == state.Value + 1, "should increment")
    ///              .ThenState(state with { Value = state.Value + 1 });
    /// 
    /// // Response-dependent state (requires mock for exploration)
    /// return Expect.That&lt;PutResponse&gt;(r => r.ETag != null, "should have ETag")
    ///              .ThenState(
    ///                  (resp, s) => s.WithETag(((PutResponse)resp).ETag),
    ///                  mock: () => new PutResponse { ETag = Guid.NewGuid().ToString() });
    /// 
    /// // With background activity
    /// return Expect.That&lt;CopyResponse&gt;(r => r.StatusCode == 202, "accepted")
    ///              .ThenState(state.WithPendingCopy())
    ///              .Triggers((resp, s) => new CopyCompletionStepFunction(...));
    /// 
    /// // Multiple possible outcomes
    /// return Expect.OneOf(
    ///     Expect.That&lt;int&gt;(r => r > 0, "positive").ThenState(positiveState),
    ///     Expect.That&lt;int&gt;(r => r == 0, "zero").ThenState(zeroState));
    /// </example>
    public static class Expect
    {
        /// <summary>
        /// Creates an expected outcome with a predicate-based response validator.
        /// </summary>
        /// <typeparam name="TResponse">The type of response to validate.</typeparam>
        /// <param name="predicate">A predicate that returns true if the response is valid.</param>
        /// <param name="explanation">A description of what the predicate checks (used in error messages).</param>
        /// <returns>An <see cref="ExpectedOutcomeBuilder{TResponse}"/> for further configuration.</returns>
        public static ExpectedOutcomeBuilder<TResponse> That<TResponse>(
            Func<TResponse, bool> predicate,
            string explanation = null)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            var validator = new ResponseValidator(value =>
            {
                // Check type compatibility before casting to avoid InvalidCastException
                // This is important when the system tries to match a response against
                // multiple possible outcomes (e.g., normal return vs exception)
                // Note: null is allowed for nullable types and reference types
                if (value != null && !(value is TResponse))
                {
                    return ValidationResult.Invalid(
                        $"Response type mismatch: expected {typeof(TResponse).Name} but got {value.GetType().Name}");
                }
                
                var result = predicate((TResponse)value);
                return result
                    ? ValidationResult.Valid()
                    : ValidationResult.Invalid(explanation ?? "Predicate not satisfied");
            });

            return new ExpectedOutcomeBuilder<TResponse>(validator);
        }

        /// <summary>
        /// Creates an expected outcome with a ValidationResult-returning validator.
        /// This overload allows including the actual response value in error messages.
        /// Works well with FluentAssertions or FluentValidation libraries.
        /// </summary>
        /// <typeparam name="TResponse">The type of response to validate.</typeparam>
        /// <param name="validator">
        /// A function that validates the response and returns a <see cref="ValidationResult"/>
        /// containing both the validity and an explanation message.
        /// </param>
        /// <returns>An <see cref="ExpectedOutcomeBuilder{TResponse}"/> for further configuration.</returns>
        /// <example>
        /// // Rich error message with actual value
        /// Expect.That&lt;int&gt;(r => r > 0 
        ///     ? ValidationResult.Valid() 
        ///     : ValidationResult.Invalid($"Expected positive but got {r}"))
        /// </example>
        public static ExpectedOutcomeBuilder<TResponse> That<TResponse>(
            Func<TResponse, ValidationResult> validator)
        {
            if (validator == null)
                throw new ArgumentNullException(nameof(validator));

            var responseValidator = new ResponseValidator(value =>
            {
                // Check type compatibility before casting to avoid InvalidCastException
                // Note: null is allowed for nullable types and reference types
                if (value != null && !(value is TResponse))
                {
                    return ValidationResult.Invalid(
                        $"Response type mismatch: expected {typeof(TResponse).Name} but got {value.GetType().Name}");
                }
                
                return validator((TResponse)value);
            });

            return new ExpectedOutcomeBuilder<TResponse>(responseValidator);
        }

        /// <summary>
        /// Creates an expected outcome with a ResponseValidator.
        /// This overload accepts any type that can be implicitly converted to ResponseValidator,
        /// including Descriptor types when Accordant.Descriptors is referenced.
        /// </summary>
        /// <param name="validator">A ResponseValidator (or type convertible to it) that validates responses.</param>
        /// <returns>An <see cref="ExpectedOutcomeBuilder{Object}"/> for further configuration.</returns>
        /// <example>
        /// // Works with Descriptors via implicit conversion (when Accordant.Descriptors is referenced)
        /// return Expect.That(HttpResponseDescriptor.Create(HttpStatusCode.OK))
        ///              .ThenState(updatedState);
        /// </example>
        public static ExpectedOutcomeBuilder<object> That(ResponseValidator validator)
        {
            if (validator == null)
                throw new ArgumentNullException(nameof(validator));

            return new ExpectedOutcomeBuilder<object>(validator);
        }

        /// <summary>
        /// Creates a typed expected outcome with a ResponseValidator.
        /// This overload accepts any type that can be implicitly converted to ResponseValidator,
        /// including Descriptor types when Accordant.Descriptors is referenced.
        /// </summary>
        /// <typeparam name="TResponse">The type of response to validate.</typeparam>
        /// <param name="validator">A ResponseValidator (or type convertible to it) that validates responses.</param>
        /// <returns>An <see cref="ExpectedOutcomeBuilder{TResponse}"/> for further configuration.</returns>
        /// <example>
        /// // Works with Descriptors via implicit conversion (when Accordant.Descriptors is referenced)
        /// return Expect.That&lt;MyResponse&gt;(myDescriptor)
        ///              .ThenState(updatedState);
        /// </example>
        public static ExpectedOutcomeBuilder<TResponse> That<TResponse>(ResponseValidator validator)
        {
            if (validator == null)
                throw new ArgumentNullException(nameof(validator));

            return new ExpectedOutcomeBuilder<TResponse>(validator);
        }

        /// <summary>
        /// Creates an expected outcome for an operation that should throw an exception.
        /// Use this when the operation is expected to throw rather than return normally.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="explanation">Optional explanation for error messages.</param>
        /// <returns>An <see cref="ExpectedOutcomeBuilder{TResponse}"/> for further configuration.</returns>
        /// <example>
        /// // Expect operation to throw InsufficientFundsException
        /// return Expect.Throws&lt;InsufficientFundsException&gt;()
        ///              .SameState();
        /// </example>
        public static ExpectedOutcomeBuilder<Exception> Throws<TException>(
            string explanation = null)
            where TException : Exception
        {
            return That<Exception>(
                ex => ex is TException,
                explanation ?? $"should throw {typeof(TException).Name}");
        }

        /// <summary>
        /// Creates an expected outcome for a void-returning operation.
        /// Use this when the operation returns <see cref="Unit"/> (i.e., performs an action but returns no meaningful value).
        /// </summary>
        /// <param name="explanation">Optional explanation for error messages.</param>
        /// <returns>An <see cref="ExpectedOutcomeBuilder{Unit}"/> for further configuration.</returns>
        /// <example>
        /// // Expect Push operation to succeed (returns Unit)
        /// return Expect.Unit()
        ///              .ThenState(updatedState);
        /// </example>
        public static ExpectedOutcomeBuilder<Unit> Unit(string explanation = null)
        {
            return That<Unit>(_ => true, explanation ?? "void operation");
        }

        /// <summary>
        /// Creates an expected outcome for an operation that should throw an exception,
        /// with additional validation on the exception.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="predicate">Predicate to validate the exception.</param>
        /// <param name="explanation">Optional explanation for error messages.</param>
        /// <returns>An <see cref="ExpectedOutcomeBuilder{TResponse}"/> for further configuration.</returns>
        /// <example>
        /// // Expect operation to throw with specific message
        /// return Expect.Throws&lt;InsufficientFundsException&gt;(
        ///              ex => ex.Message.Contains("insufficient"),
        ///              "should throw with 'insufficient' message")
        ///              .SameState();
        /// </example>
        public static ExpectedOutcomeBuilder<Exception> Throws<TException>(
            Func<TException, bool> predicate,
            string explanation = null)
            where TException : Exception
        {
            return That<Exception>(
                ex => ex is TException tex && predicate(tex),
                explanation ?? $"should throw {typeof(TException).Name}");
        }

        /// <summary>
        /// Creates multiple expected outcomes (non-deterministic behavior).
        /// Use when an operation can have multiple valid outcomes.
        /// </summary>
        /// <param name="outcomes">The possible outcomes.</param>
        /// <returns>An <see cref="ExpectedOutcomes"/> containing all possibilities.</returns>
        public static ExpectedOutcomes OneOf(params ExpectedOutcome[] outcomes)
        {
            if (outcomes == null || outcomes.Length == 0)
                throw new ArgumentException("At least one outcome must be provided.", nameof(outcomes));

            return new ExpectedOutcomes(outcomes);
        }

        /// <summary>
        /// Creates multiple expected outcomes from builders (non-deterministic behavior).
        /// Use when an operation can have multiple valid outcomes.
        /// </summary>
        /// <param name="builders">The outcome builders.</param>
        /// <returns>An <see cref="ExpectedOutcomes"/> containing all possibilities.</returns>
        public static ExpectedOutcomes OneOf<TResponse>(params ExpectedOutcomeBuilder<TResponse>[] builders)
        {
            if (builders == null || builders.Length == 0)
                throw new ArgumentException("At least one outcome must be provided.", nameof(builders));

            var outcomes = new ExpectedOutcome[builders.Length];
            for (int i = 0; i < builders.Length; i++)
            {
                outcomes[i] = builders[i].Build();
            }

            return new ExpectedOutcomes(outcomes);
        }
    }

    /// <summary>
    /// Builder for constructing <see cref="ExpectedOutcome"/> instances fluently.
    /// </summary>
    /// <typeparam name="TResponse">The type of response this outcome validates.</typeparam>
    public class ExpectedOutcomeBuilder<TResponse>
    {
        private readonly ResponseValidator _validator;
        private Func<object, State, StateList> _nextStateGenerator;
        private Func<object, StepFunctionList> _nextStepFunctions = (resp) => new StepFunctionList();
        private Func<object> _mockResponseGenerator;

        /// <summary>
        /// Creates a new ExpectedOutcomeBuilder with the given validator.
        /// </summary>
        /// <param name="validator">The validator to use for validating responses.</param>
        public ExpectedOutcomeBuilder(ResponseValidator validator)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        #region ThenState - Modifier Lambda (Auto-Clone)

        /// <summary>
        /// Specifies a next state using a modifier action that mutates a pre-cloned state.
        /// The framework automatically clones the current state and passes it to the modifier.
        /// </summary>
        /// <typeparam name="TState">The type of state.</typeparam>
        /// <param name="modifier">An action that modifies the cloned state.</param>
        /// <returns>This builder for method chaining.</returns>
        /// <example>
        /// return Expect.That&lt;int&gt;(r => r > 0, "positive")
        ///              .ThenState&lt;MyState&gt;(nextState => {
        ///                  nextState.Items.Add(request);
        ///              });
        /// </example>
        public ExpectedOutcomeBuilder<TResponse> ThenState<TState>(
            Action<TState> modifier) where TState : State
        {
            if (modifier == null)
                throw new ArgumentNullException(nameof(modifier));

            _nextStateGenerator = (resp, currentState) =>
            {
                var clone = (TState)currentState.Clone();
                modifier(clone);
                return [clone];
            };
            return this;
        }

        /// <summary>
        /// Specifies a response-dependent next state using a modifier action that mutates a pre-cloned state.
        /// The framework automatically clones the current state and passes it to the modifier.
        /// </summary>
        /// <typeparam name="TState">The type of state.</typeparam>
        /// <param name="modifier">An action that receives the response and modifies the cloned state.</param>
        /// <param name="mock">A function that generates a mock response for state exploration.</param>
        /// <returns>This builder for method chaining.</returns>
        /// <example>
        /// return Expect.That&lt;ApiResult&gt;(r => r.IsSuccess, "success")
        ///              .ThenState&lt;MyState&gt;((response, nextState) => {
        ///                  nextState.ETag = response.ETag;
        ///              }, mock: () => new ApiResult { ETag = "mock" });
        /// </example>
        public ExpectedOutcomeBuilder<TResponse> ThenState<TState>(
            Action<TResponse, TState> modifier,
            Func<TResponse> mock) where TState : State
        {
            if (modifier == null)
                throw new ArgumentNullException(nameof(modifier));
            if (mock == null)
                throw new ArgumentNullException(nameof(mock),
                    "Mock response generator is required when state depends on response (for state exploration).");

            _nextStateGenerator = (resp, currentState) =>
            {
                var clone = (TState)currentState.Clone();
                modifier((TResponse)resp, clone);
                return [clone];
            };
            _mockResponseGenerator = () => mock();
            return this;
        }

        /// <summary>
        /// Specifies a next state using a modifier action with access to the state clone map.
        /// The framework automatically clones the current state with CloneWithMap() and passes
        /// both the clone and the mapping from original to cloned states.
        /// </summary>
        /// <typeparam name="TState">The type of state.</typeparam>
        /// <param name="modifier">An action that modifies the cloned state, with access to the clone map.</param>
        /// <returns>This builder for method chaining.</returns>
        /// <example>
        /// return Expect.That&lt;int&gt;(r => r > 0, "positive")
        ///              .ThenState&lt;MyState&gt;((nextState, map) => {
        ///                  var clonedChild = (ChildState)map[state.Child];
        ///                  clonedChild.Value = 42;
        ///              });
        /// </example>
        public ExpectedOutcomeBuilder<TResponse> ThenState<TState>(
            Action<TState, Dictionary<object, object>> modifier) where TState : State
        {
            if (modifier == null)
                throw new ArgumentNullException(nameof(modifier));

            _nextStateGenerator = (resp, currentState) =>
            {
                var (clone, map) = currentState.CloneWithMap<TState>();
                modifier(clone, map);
                return [clone];
            };
            return this;
        }

        /// <summary>
        /// Specifies a response-dependent next state with access to the state clone map.
        /// The framework automatically clones the current state with CloneWithMap() and passes
        /// both the clone and the mapping from original to cloned states.
        /// </summary>
        /// <typeparam name="TState">The type of state.</typeparam>
        /// <param name="modifier">An action that receives the response and modifies the cloned state, with access to the clone map.</param>
        /// <param name="mock">A function that generates a mock response for state exploration.</param>
        /// <returns>This builder for method chaining.</returns>
        /// <example>
        /// return Expect.That&lt;ApiResult&gt;(r => r.IsSuccess, "success")
        ///              .ThenState&lt;MyState&gt;((response, nextState, map) => {
        ///                  var clonedUser = (UserState)map[state.Users[response.UserId]];
        ///                  clonedUser.ETag = response.ETag;
        ///              }, mock: () => new ApiResult { UserId = "user1", ETag = "mock" });
        /// </example>
        public ExpectedOutcomeBuilder<TResponse> ThenState<TState>(
            Action<TResponse, TState, Dictionary<object, object>> modifier,
            Func<TResponse> mock) where TState : State
        {
            if (modifier == null)
                throw new ArgumentNullException(nameof(modifier));
            if (mock == null)
                throw new ArgumentNullException(nameof(mock),
                    "Mock response generator is required when state depends on response (for state exploration).");

            _nextStateGenerator = (resp, currentState) =>
            {
                var (clone, map) = currentState.CloneWithMap<TState>();
                modifier((TResponse)resp, clone, map);
                return [clone];
            };
            _mockResponseGenerator = () => mock();
            return this;
        }

        #endregion

        #region WithNextState - Direct State Replacement

        /// <summary>
        /// Specifies the exact next state to use after this operation.
        /// Use this for simple state types like <see cref="AtomicState{T}"/> where you construct
        /// a new state instance rather than mutating a clone.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is ideal for:
        /// <list type="bullet">
        ///   <item><description><see cref="AtomicState{T}"/> - simple value-based states</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// For complex states with nested objects where clone-and-mutate is more convenient,
        /// use <see cref="ThenState{TState}(Action{TState})"/> instead.
        /// </para>
        /// </remarks>
        /// <param name="nextState">The state to use after this operation.</param>
        /// <returns>This builder for method chaining.</returns>
        /// <example>
        /// return Expect.That&lt;int&gt;(r => r == expectedValue, "should equal expected")
        ///              .WithNextState(new AtomicState&lt;int&gt;(state.Value + request));
        /// </example>
        public ExpectedOutcomeBuilder<TResponse> WithNextState(State nextState)
        {
            if (nextState == null)
                throw new ArgumentNullException(nameof(nextState));

            _nextStateGenerator = (resp, currentState) => [nextState];
            return this;
        }

        /// <summary>
        /// Specifies a response-dependent next state.
        /// Use this when the next state depends on values in the response (e.g., server-generated IDs).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method requires a mock response generator for state exploration during test generation.
        /// The mock response provides values when the real response is not yet available.
        /// </para>
        /// <para>
        /// For complex states where clone-and-mutate is more convenient,
        /// use <see cref="ThenState{TState}(Action{TResponse, TState}, Func{TResponse})"/> instead.
        /// </para>
        /// </remarks>
        /// <param name="nextStateFunc">A function that takes the response and returns the next state.</param>
        /// <param name="mock">A function that generates a mock response for state exploration.</param>
        /// <returns>This builder for method chaining.</returns>
        /// <example>
        /// return Expect.That&lt;CreateResponse&gt;(r => r.Success, "created successfully")
        ///              .WithNextState(
        ///                  response => new MyState(response.Id),
        ///                  mock: () => new CreateResponse { Id = "mock-id" });
        /// </example>
        public ExpectedOutcomeBuilder<TResponse> WithNextState(
            Func<TResponse, State> nextStateFunc,
            Func<TResponse> mock)
        {
            if (nextStateFunc == null)
                throw new ArgumentNullException(nameof(nextStateFunc));
            if (mock == null)
                throw new ArgumentNullException(nameof(mock),
                    "Mock response generator is required when state depends on response (for state exploration).");

            _nextStateGenerator = (resp, currentState) => [nextStateFunc((TResponse)resp)];
            _mockResponseGenerator = () => mock();
            return this;
        }

        #endregion

        #region SameState

        /// <summary>
        /// Indicates that the state is unchanged by this operation.
        /// When the outcome is matched, the original input state will be used.
        /// </summary>
        /// <returns>This builder for method chaining.</returns>
        public ExpectedOutcomeBuilder<TResponse> SameState()
        {
            _nextStateGenerator = (resp, state) => [state];
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
        public ExpectedOutcomeBuilder<TResponse> Triggers(IStepFunction stepFunction)
        {
            if (stepFunction == null)
                throw new ArgumentNullException(nameof(stepFunction));

            _nextStepFunctions = (resp) => new StepFunctionList(stepFunction);
            return this;
        }

        /// <summary>
        /// Specifies that this operation triggers multiple background activities via step functions.
        /// The step functions run concurrently with subsequent operations.
        /// </summary>
        /// <param name="stepFunctions">The step functions to trigger.</param>
        /// <returns>This builder for method chaining.</returns>
        public ExpectedOutcomeBuilder<TResponse> Triggers(params IStepFunction[] stepFunctions)
        {
            if (stepFunctions == null || stepFunctions.Length == 0)
                throw new ArgumentException("At least one step function must be provided.", nameof(stepFunctions));

            _nextStepFunctions = (resp) => new StepFunctionList(stepFunctions);
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
        public ExpectedOutcomeBuilder<TResponse> Triggers(
            Func<TResponse, IStepFunction> stepFunctionGenerator)
        {
            if (stepFunctionGenerator == null)
                throw new ArgumentNullException(nameof(stepFunctionGenerator));

            _nextStepFunctions = (resp) =>
            {
                var sf = stepFunctionGenerator((TResponse)resp);
                return sf != null ? new StepFunctionList(sf) : new StepFunctionList();
            };
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
        public ExpectedOutcomeBuilder<TResponse> Triggers(
            Func<TResponse, IList<IStepFunction>> stepFunctionsGenerator)
        {
            if (stepFunctionsGenerator == null)
                throw new ArgumentNullException(nameof(stepFunctionsGenerator));

            _nextStepFunctions = (resp) =>
            {
                var funcs = stepFunctionsGenerator((TResponse)resp);
                var list = new StepFunctionList();
                if (funcs != null)
                {
                    foreach (var f in funcs)
                    {
                        if (f != null) list.Add(f);
                    }
                }
                return list;
            };
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
            if (_nextStateGenerator == null)
            {
                throw new InvalidOperationException(
                    "Expected outcome must specify a next state via ThenState(), ThenStates(), or SameState().");
            }

            return new ExpectedOutcome(
                _validator,
                _nextStateGenerator,
                _nextStepFunctions,
                _mockResponseGenerator);
        }

        #endregion

        #region Implicit Conversions

        /// <summary>
        /// Implicit conversion to <see cref="ExpectedOutcome"/> for cleaner syntax.
        /// </summary>
        public static implicit operator ExpectedOutcome(ExpectedOutcomeBuilder<TResponse> builder)
        {
            return builder.Build();
        }

        /// <summary>
        /// Implicit conversion to <see cref="ExpectedOutcomes"/> for cleaner syntax.
        /// Wraps this single outcome in an ExpectedOutcomes collection.
        /// </summary>
        public static implicit operator ExpectedOutcomes(ExpectedOutcomeBuilder<TResponse> builder)
        {
            return new ExpectedOutcomes(builder.Build());
        }

        #endregion
    }
}
