// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;

/// <summary>
/// Testing context that provides access to the system under test during test execution.
/// Provides common functionality for all testing contexts.
/// </summary>
public sealed class TestingContext
{
    private readonly Dictionary<Type, object> _services = new();

    public Func<object, string> RequestPrinter { get; set; } =
        (request) => request == null ? "<null>" : request.ToString();

    public Func<object, string> ResponsePrinter { get; set; } =
        (response) => response == null ? "<null>" : response.ToString();

    /// <summary>
    /// The spec being tested.
    /// </summary>
    public ISpec Spec { get; set; }

    public TestingContext(
        ISpec spec,
        string testDirectoryPath = null)
    {
        Spec = spec;
        _ = new Logger(outputDirectory: testDirectoryPath);
    }

    /// <summary>
    /// Registers a target instance that can be retrieved via <see cref="Get{T}"/>.
    /// This is typically called during test initialization to register the system under test.
    /// </summary>
    /// <typeparam name="T">The type of the target.</typeparam>
    /// <param name="instance">The target instance to register.</param>
    public void Register<T>(T instance)
    {
        _services[typeof(T)] = instance;
    }

    /// <summary>
    /// Gets the registered service of the specified type.
    /// Use this in <see cref="Operation{TRequest, TResponse, TState}.ExecuteAsync"/> 
    /// to access the system under test or other registered services.
    /// </summary>
    /// <typeparam name="T">The type of the service to retrieve.</typeparam>
    /// <returns>The registered service instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no service of type T has been registered.</exception>
    public T Get<T>()
    {
        if (!_services.TryGetValue(typeof(T), out var instance))
        {
            throw new InvalidOperationException(
                $"No service of type '{typeof(T).Name}' has been registered. " +
                $"Call Register<{typeof(T).Name}>() on the context before running tests.");
        }

        return (T)instance;
    }

    /// <summary>
    /// Gets the registered target of the specified type.
    /// This is an alias for <see cref="Get{T}"/> for backwards compatibility.
    /// </summary>
    /// <typeparam name="T">The type of the target to retrieve.</typeparam>
    /// <returns>The registered target instance.</returns>
    [Obsolete("Use Get<T>() instead. This method will be removed in a future version.")]
    public T Target<T>() => Get<T>();

    /// <summary>
    /// Clears all registered services/targets.
    /// Called internally before each test case initialization.
    /// </summary>
    internal void ClearServices()
    {
        _services.Clear();
    }
}
