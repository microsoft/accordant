// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Tests;

using System;
using System.Linq;
using Microsoft.Accordant;

/// <summary>
/// Base class for operations that automatically adds indefinite failure outcomes.
/// 
/// Operations only need to implement ApplyInternal() with happy-path logic.
/// This class automatically wraps the outcomes to add:
/// 1. Indefinite failure where state doesn't change (operation failed before effect)
/// 2. Indefinite failure where state DOES change (operation succeeded but response lost)
/// 
/// Operations can override GetIndefiniteSuccessOutcome() to customize the state
/// change for indefinite failures (e.g., adding to MaybeExists instead of definite state).
/// </summary>
public abstract class TodoOperation<TRequest, TResponse, TState>
    : Operation<TRequest, TResponse, TState>
    where TState : class, IState
    where TResponse : class
{
    protected TodoOperation(string name) : base(name)
    {
    }

    /// <summary>
    /// Sealed Apply() that wraps ApplyInternal() with indefinite failure handling.
    /// </summary>
    public sealed override ExpectedOutcomes Apply(TRequest request, TState state)
    {
        var baseOutcomes = ApplyInternal(request, state);

        if (!IndefiniteFailureSemantics.Enabled)
        {
            return baseOutcomes;
        }

        return AddIndefiniteFailureOutcomes(baseOutcomes, request, state);
    }

    /// <summary>
    /// Implement this with the "happy path" logic - just the normal expected outcomes.
    /// The base class will automatically add indefinite failure variants.
    /// </summary>
    protected abstract ExpectedOutcomes ApplyInternal(TRequest request, TState state);

    /// <summary>
    /// Adds indefinite failure outcomes (where IsIndefiniteFailure = true).
    /// For each base outcome:
    /// - Adds failure with no state change (operation never reached server)
    /// - Adds failure WITH state change via GetIndefiniteSuccessOutcome (customizable)
    /// 
    /// Note: This is only called when IndefiniteFailureSemantics.Enabled is true.
    /// During exploration, suppress faulty semantics to keep state space manageable.
    /// </summary>
    private ExpectedOutcomes AddIndefiniteFailureOutcomes(
        ExpectedOutcomes baseOutcomes,
        TRequest request,
        TState state)
    {
        var newOutcomes = baseOutcomes.PossibleOutcomes.ToList();

        // Add a single "indefinite failure, no state change" outcome
        newOutcomes.Add(new ExpectedOutcome(
            IndefiniteFailureValidator,
            state,
            IndefiniteFailureResponseGenerator));

        // For each base outcome, allow operations to specify indefinite success behavior
        // (e.g., adding to MaybeExists instead of definite state)
        foreach (var outcome in baseOutcomes.PossibleOutcomes)
        {
            var indefiniteOutcome = GetIndefiniteSuccessOutcome(outcome, request, state);
            if (indefiniteOutcome != null)
            {
                newOutcomes.Add(indefiniteOutcome);
            }
        }

        return new ExpectedOutcomes(newOutcomes.ToArray());
    }

    /// <summary>
    /// Override to customize the state change for indefinite failures where
    /// the operation may have succeeded on the server.
    /// 
    /// Default: returns null if state doesn't change (reads), otherwise
    /// uses the same state change as the success outcome.
    /// Override to track uncertainty differently (e.g., MaybeExists lists).
    /// 
    /// Return null to skip adding this outcome.
    /// </summary>
    protected virtual ExpectedOutcome? GetIndefiniteSuccessOutcome(
        ExpectedOutcome successOutcome,
        TRequest request,
        TState state)
    {
        try
        {
            // Check if this outcome actually changes state
            // Pass null response - if lambda needs response, it will throw
            var nextStates = successOutcome.NextStateGenerator(null!, state);
            var nextState = nextStates.FirstOrDefault();

            // If state doesn't change (e.g., SameState()), skip indefinite success variant
            // The "indefinite failure, no state change" outcome already covers this case
            if (nextState == null || nextState.GetStateHash() == state.GetStateHash())
            {
                return null;
            }

            // State changes without needing response: add indefinite failure variant
            return new ExpectedOutcome(
                IndefiniteFailureValidator,
                successOutcome.NextStateGenerator,
                successOutcome.NextStepFunctions,
                IndefiniteFailureResponseGenerator);
        }
        catch
        {
            // Lambda needs response to compute next state - this is a read or write
            // that learns server-generated values. Either way, we can't compute
            // the indefinite success state without a response, so skip it.
            // Override this method in writes to provide custom indefinite success logic.
            return null;
        }
    }

    /// <summary>
    /// Validator for indefinite failure responses.
    /// </summary>
    protected ResponseValidator IndefiniteFailureValidator =>
        ResponseValidator.FromPredicate<TResponse>(r =>
            IsIndefiniteFailure(r)
                ? ValidationResult.Valid()
                : ValidationResult.Invalid($"Expected indefinite failure, got: {r}"));

    /// <summary>
    /// Override to customize how indefinite failures are detected.
    /// </summary>
    protected abstract bool IsIndefiniteFailure(TResponse response);

    /// <summary>
    /// Override to provide the mock response for indefinite failures.
    /// </summary>
    protected abstract object IndefiniteFailureResponseGenerator();
}

/// <summary>
/// Specialized base for operations using ApiResult responses.
/// Provides default implementations for indefinite failure detection.
/// </summary>
public abstract class TodoApiOperation<TRequest, TData, TState>
    : TodoOperation<TRequest, ApiResult<TData>, TState>
    where TState : class, IState
{
    protected TodoApiOperation(string name) : base(name)
    {
    }

    protected override bool IsIndefiniteFailure(ApiResult<TData> response)
    {
        return response.IsIndefiniteFailure;
    }

    protected override object IndefiniteFailureResponseGenerator()
    {
        // Simulate a network error (which makes IsIndefiniteFailure true)
        return new ApiResult<TData>
        {
            IsNetworkError = true,
            FailureMessage = "Simulated network failure"
        };
    }
}
