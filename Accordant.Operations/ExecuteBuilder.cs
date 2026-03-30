// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Fluent builder for binding execution logic to operations.
    /// Use <see cref="Spec{TState}.ExecuteWith{TTarget}"/> to obtain an instance.
    /// </summary>
    /// <typeparam name="TTarget">The type of the system under test.</typeparam>
    /// <typeparam name="TState">The type of state the spec operates on.</typeparam>
    public class ExecuteBuilder<TTarget, TState> where TState : State
    {
        private readonly Spec<TState> _spec;

        /// <summary>
        /// Creates a new ExecuteBuilder for the given spec.
        /// </summary>
        internal ExecuteBuilder(Spec<TState> spec)
        {
            _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        }

        /// <summary>
        /// Binds a synchronous execution function to an operation by reference.
        /// </summary>
        /// <typeparam name="TRequest">The type of request the operation accepts.</typeparam>
        /// <typeparam name="TResponse">The type of response the operation returns.</typeparam>
        /// <param name="operation">The operation to bind.</param>
        /// <param name="execute">The function that executes the operation against the target.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ExecuteBuilder<TTarget, TState> Bind<TRequest, TResponse>(
            Operation<TRequest, TResponse, TState> operation,
            Func<TTarget, TRequest, TResponse> execute)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            operation.ExecuteFunc = (context, request) =>
            {
                var target = context.Get<TTarget>();
                return Task.FromResult(execute(target, request));
            };

            return this;
        }

        /// <summary>
        /// Binds a synchronous execution function to an operation by name.
        /// </summary>
        /// <typeparam name="TRequest">The type of request the operation accepts.</typeparam>
        /// <typeparam name="TResponse">The type of response the operation returns.</typeparam>
        /// <param name="operationName">The name of the operation to bind.</param>
        /// <param name="execute">The function that executes the operation against the target.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ExecuteBuilder<TTarget, TState> Bind<TRequest, TResponse>(
            string operationName,
            Func<TTarget, TRequest, TResponse> execute)
        {
            if (string.IsNullOrEmpty(operationName)) 
                throw new ArgumentNullException(nameof(operationName));
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            var operation = _spec.GetOperation<TRequest, TResponse>(operationName);
            return Bind(operation, execute);
        }

        /// <summary>
        /// Binds an asynchronous execution function to an operation by reference.
        /// </summary>
        /// <typeparam name="TRequest">The type of request the operation accepts.</typeparam>
        /// <typeparam name="TResponse">The type of response the operation returns.</typeparam>
        /// <param name="operation">The operation to bind.</param>
        /// <param name="execute">The async function that executes the operation against the target.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ExecuteBuilder<TTarget, TState> BindAsync<TRequest, TResponse>(
            Operation<TRequest, TResponse, TState> operation,
            Func<TTarget, TRequest, Task<TResponse>> execute)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            operation.ExecuteFunc = async (context, request) =>
            {
                var target = context.Get<TTarget>();
                return await execute(target, request);
            };

            return this;
        }

        /// <summary>
        /// Binds an asynchronous execution function to an operation by name.
        /// </summary>
        /// <typeparam name="TRequest">The type of request the operation accepts.</typeparam>
        /// <typeparam name="TResponse">The type of response the operation returns.</typeparam>
        /// <param name="operationName">The name of the operation to bind.</param>
        /// <param name="execute">The async function that executes the operation against the target.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ExecuteBuilder<TTarget, TState> BindAsync<TRequest, TResponse>(
            string operationName,
            Func<TTarget, TRequest, Task<TResponse>> execute)
        {
            if (string.IsNullOrEmpty(operationName)) 
                throw new ArgumentNullException(nameof(operationName));
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            var operation = _spec.GetOperation<TRequest, TResponse>(operationName);
            return BindAsync(operation, execute);
        }

        /// <summary>
        /// Returns to the Spec for further configuration (e.g., ProvideTargetAndInitialState).
        /// </summary>
        /// <returns>The spec this builder was created from.</returns>
        public Spec<TState> Done() => _spec;

        /// <summary>
        /// Implicit conversion back to Spec for seamless chaining.
        /// Allows: spec.ExecuteWith&lt;T&gt;().Bind(...).ProvideTargetAndInitialState(...)
        /// </summary>
        public static implicit operator Spec<TState>(ExecuteBuilder<TTarget, TState> builder)
        {
            return builder._spec;
        }
    }
}
