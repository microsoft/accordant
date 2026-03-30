// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// This class a result of applying a step function.
    /// </summary>
    public class StepResult
    {
        /// <summary>
        /// The next state.
        /// </summary>
        public State State { get; set; }

        /// <summary>
        /// The list of step functions that should be applied in the next state.
        /// </summary>
        public IList<IStepFunction> StepFunctions { get; set; }

        /// <summary>
        /// Additional data to store in the edge leading to the next state.
        /// </summary>
        public object EdgeMetadata { get; set; }
    }

    /// <summary>
    /// A step function transitions the state of the system to a non-deterministic
    /// set of next states, if it is enabled in the source state.
    /// 
    /// A step function is _consumed_ when applied and can _produce_ new step functions in the set of
    /// next states.
    /// </summary>
    public interface IStepFunction
    {
        /// <summary>
        /// The unique identifier of the step function.
        /// </summary>
        public string StepFunctionId { get; }

        /// <summary>
        /// This method returns a list of next states, and new step functions to include
        /// in those states. Each such transition represents a non-deterministic evolution
        /// of the system from the current state to the target state.
        /// 
        /// No transition happens if this method returns an empty list
        /// or a null value.
        /// </summary>
        public IList<StepResult> Apply(State state, IReadOnlyList<(IStepFunction, StateGraphNode)> path);
    }

    /// <summary>
    /// BaseStepFunction is an intermediate class other steps can inherit from.
    /// It chooses a GUID as a StepFunctionId, effectively meaning that two 
    /// step functions are only equal if they are the same object.
    /// It also locks the input and output states to ensure implementations of the step
    /// don't inadvertently modify the input state and subsequent code doesn't
    /// modify the output states.
    /// </summary>
    public abstract class BaseStepFunction : IStepFunction
    {
        private string stepFunctionId = Guid.NewGuid().ToString();

        public virtual string StepFunctionId => stepFunctionId;

        /// <summary>
        /// This method locks the state, calls the derived class's
        /// <see cref="BaseStepFunction.ApplyInternal(State)"/> method
        /// and locks any updated states returned by that method.
        /// </summary>
        public IList<StepResult> Apply(State state, IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            state.Lock();

            var stepResults = ApplyInternal(state, path);

            // Validate that JsonState inputs were not mutated by user code
            if (state is JsonState jsonState)
            {
                jsonState.ValidateNotMutated();
            }

            if (stepResults != null)
            {
                foreach (var stepResult in stepResults)
                {
                    stepResult.State?.Lock();
                }
            }

            return stepResults;
        }

        protected virtual IList<StepResult> ApplyInternal(State state)
        {
            throw new NotImplementedException();
        }

        protected virtual IList<StepResult> ApplyInternal(State state, IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            return ApplyInternal(state);
        }
    }

    /// <summary>
    /// A step function that represents background work which will eventually complete.
    /// Subclasses must define <see cref="IsTerminalState"/> to indicate when the work is done,
    /// and <see cref="GetStepResults"/> to define how the state transitions.
    /// 
    /// This is used for:
    /// - Test execution: Polling continues until IsTerminalState returns true for all states
    /// - Test generation: Unwinding continues until IsTerminalState returns true
    /// 
    /// For daemon/fire-and-forget step functions that never terminate, use <see cref="BaseStepFunction"/> instead.
    /// 
    /// For simple cases, use <see cref="AsyncOperation.Create{TState}(Func{TState, bool}, Action{TState})"/> to create 
    /// an async operation inline without defining a class.
    /// </summary>
    public abstract class TerminatingStepFunction : BaseStepFunction
    {
        /// <summary>
        /// Returns true when this step function's background work is complete.
        /// The predicate is evaluated against the current state.
        /// 
        /// During polling: We keep polling until this returns true for all possible states.
        /// During unwinding: We keep applying step functions until this returns true.
        /// </summary>
        public abstract Func<State, bool> IsTerminalState { get; }

        /// <summary>
        /// Implement this method to define the possible state transitions.
        /// This method is only called when <see cref="IsTerminalState"/> returns false.
        /// 
        /// For a single deterministic outcome, return a single StepResult.
        /// For non-deterministic outcomes, return multiple StepResults.
        /// </summary>
        /// <param name="state">The current state (do not mutate directly; clone first).</param>
        /// <returns>The list of possible next states.</returns>
        protected abstract IList<StepResult> GetStepResults(State state);

        /// <summary>
        /// Sealed implementation that handles the terminal check automatically.
        /// Only calls <see cref="GetStepResults"/> when the state is not terminal.
        /// </summary>
        protected sealed override IList<StepResult> ApplyInternal(State state)
        {
            // Already terminal? Nothing to do.
            if (IsTerminalState(state))
            {
                return null;
            }

            return GetStepResults(state);
        }
    }

    /// <summary>
    /// Factory for creating inline async operations (terminating step functions).
    /// Use this for simple cases where you don't need to create a separate class.
    /// </summary>
    public static class AsyncOperation
    {
        /// <summary>
        /// Creates an async operation with a single outcome.
        /// 
        /// Example:
        /// <code>
        /// .Triggers(AsyncOperation.Create&lt;PetImagesState&gt;(
        ///     isTerminal: s => s.Images[name].State != "Creating",
        ///     transition: nextState => {
        ///         nextState.Images[name].State = "Created";
        ///     }
        /// ))
        /// </code>
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="isTerminal">Predicate that returns true when the background work is complete.</param>
        /// <param name="transition">Action that mutates a cloned state to the next state. Only called when isTerminal is false.</param>
        /// <param name="name">Optional name for logging/debugging.</param>
        public static TerminatingStepFunction Create<TState>(
            Func<TState, bool> isTerminal,
            Action<TState> transition,
            string name = null) where TState : State
        {
            return new AsyncOperation<TState>(
                isTerminal,
                new[] { transition },
                name);
        }

        /// <summary>
        /// Creates an async operation with multiple non-deterministic outcomes.
        /// 
        /// Example:
        /// <code>
        /// .Triggers(AsyncOperation.Create&lt;PetImagesState&gt;(
        ///     isTerminal: s => s.Images[name].State != "Creating",
        ///     transitions: new Action&lt;PetImagesState&gt;[] {
        ///         nextState => { nextState.Images[name].State = "Created"; },
        ///         nextState => { nextState.Images[name].State = "Failed"; }
        ///     }
        /// ))
        /// </code>
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="isTerminal">Predicate that returns true when the background work is complete.</param>
        /// <param name="transitions">Array of actions, each defining one possible outcome. Each receives its own cloned state.</param>
        /// <param name="name">Optional name for logging/debugging.</param>
        public static TerminatingStepFunction Create<TState>(
            Func<TState, bool> isTerminal,
            Action<TState>[] transitions,
            string name = null) where TState : State
        {
            return new AsyncOperation<TState>(
                isTerminal,
                transitions,
                name);
        }
    }

    /// <summary>
    /// Sealed implementation of TerminatingStepFunction for inline/factory usage.
    /// Use this for simple async operations. For complex cases, subclass TerminatingStepFunction directly.
    /// </summary>
    internal sealed class AsyncOperation<TState> : TerminatingStepFunction where TState : State
    {
        private readonly Func<TState, bool> _isTerminal;
        private readonly Action<TState>[] _transitions;
        private readonly string _name;

        internal AsyncOperation(
            Func<TState, bool> isTerminal,
            Action<TState>[] transitions,
            string name = null)
        {
            _isTerminal = isTerminal ?? throw new ArgumentNullException(nameof(isTerminal));
            _transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
            if (_transitions.Length == 0)
                throw new ArgumentException("At least one transition is required.", nameof(transitions));
            _name = name;
        }

        public override Func<State, bool> IsTerminalState => state => _isTerminal((TState)state);

        protected override IList<StepResult> GetStepResults(State state)
        {
            // Each transition gets its own cloned state
            var results = new List<StepResult>();
            foreach (var transition in _transitions)
            {
                var nextState = (TState)state.Clone();
                transition(nextState);
                results.Add(new StepResult { State = nextState });
            }
            return results;
        }

        public override string ToString()
        {
            return _name ?? $"AsyncOperation<{typeof(TState).Name}>";
        }
    }
}
