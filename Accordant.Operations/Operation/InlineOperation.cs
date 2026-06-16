// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;

/// <summary>
/// Marker interface for inline operations that support mutable derivations and polling.
/// </summary>
internal interface IInlineOperation
{
    /// <summary>
    /// Sets the derivations for this inline operation.
    /// </summary>
    void SetDerivedFrom(IReadOnlyList<RequestDerivation> derivations);

    /// <summary>
    /// Sets the polling setup for this inline operation.
    /// </summary>
    void SetPollingSetup(PollingSetup polling);
}

/// <summary>
/// An operation defined inline using a lambda expression.
/// This is useful for simple operations that don't need a separate class.
/// </summary>
/// <typeparam name="TRequest">The type of request this operation accepts.</typeparam>
/// <typeparam name="TResponse">The type of response this operation returns.</typeparam>
/// <typeparam name="TState">The type of state this operation operates on.</typeparam>
internal class InlineOperation<TRequest, TResponse, TState> : Operation<TRequest, TResponse, TState>, IInlineOperation
    where TState : class, IState
{
    private readonly Func<TRequest, TState, ExpectedOutcomes> _apply;
    private IReadOnlyList<RequestDerivation> _derivedFrom;
    private PollingSetup _polling;

    /// <summary>
    /// Constructs an inline operation with the given name and apply function.
    /// </summary>
    /// <param name="name">The name of the operation.</param>
    /// <param name="apply">The function that defines the operation's behavior.</param>
    public InlineOperation(string name, Func<TRequest, TState, ExpectedOutcomes> apply)
        : base(name)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    /// <inheritdoc/>
    public override ExpectedOutcomes Apply(TRequest request, TState state)
    {
        return _apply(request, state);
    }

    /// <summary>
    /// Gets the derivations for this inline operation.
    /// Returns any configured derivations, or empty list if none configured.
    /// </summary>
    public override IReadOnlyList<RequestDerivation> DerivedFrom =>
        _derivedFrom ?? base.DerivedFrom;

    /// <inheritdoc/>
    public void SetDerivedFrom(IReadOnlyList<RequestDerivation> derivations)
    {
        _derivedFrom = derivations;
    }

    /// <summary>
    /// Gets the polling setup for this inline operation.
    /// Returns any configured polling, or null if none configured.
    /// </summary>
    public override PollingSetup Polling => _polling;

    /// <inheritdoc/>
    public void SetPollingSetup(PollingSetup polling)
    {
        _polling = polling;
    }
}
