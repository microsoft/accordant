// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

/// <summary>
/// The non-generic description of an operation whose execution is bound to a target.
/// </summary>
public interface IExecutableOperation
{
    /// <summary>The operation's stable name.</summary>
    string Name { get; }

    /// <summary>The request type accepted by the operation.</summary>
    Type RequestType { get; }

    /// <summary>The response type produced by the operation.</summary>
    Type ResponseType { get; }
}

/// <summary>
/// A named, typed operation with execution logic bound once for reuse across calls.
/// </summary>
public sealed class ExecutableOperation<TRequest, TResponse> : IExecutableOperation
{
    private readonly Func<TRequest, Task<TResponse>> _execute;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public Type RequestType => typeof(TRequest);

    /// <inheritdoc/>
    public Type ResponseType => typeof(TResponse);

    /// <summary>
    /// Creates an executable operation.
    /// </summary>
    /// <param name="name">The operation's stable name.</param>
    /// <param name="execute">The target-specific execution binding.</param>
    public ExecutableOperation(string name, Func<TRequest, Task<TResponse>> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(execute);

        Name = name;
        _execute = execute;
    }

    /// <summary>Executes the bound operation against its target.</summary>
    public Task<TResponse> ExecuteAsync(TRequest request) => _execute(request);
}
