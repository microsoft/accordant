// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

/// <summary>
/// A wrapper type that can validate responses. Supports implicit conversion from
/// <see cref="Func{T, TResult}"/> predicates.
/// </summary>
public class ResponseValidator
{
    internal Func<object, ValidationResult> ValidatorFunc { get; }

    /// <summary>
    /// Creates a ResponseValidator from a validation function.
    /// </summary>
    public ResponseValidator(Func<object, ValidationResult> func)
    {
        ValidatorFunc = func ?? throw new ArgumentNullException(nameof(func));
    }

    /// <summary>
    /// Validates the given response.
    /// </summary>
    public ValidationResult Validate(object response) => ValidatorFunc(response);

    /// <summary>
    /// Returns an explanation of why validation failed.
    /// </summary>
    public string Explain(object response)
    {
        var result = ValidatorFunc(response);
        return result.FailureMessage ?? "Validation failed (no explanation provided).";
    }

    /// <summary>
    /// Creates a typed ResponseValidator from a typed predicate function.
    /// </summary>
    public static ResponseValidator FromPredicate<TResponse>(Func<TResponse, ValidationResult> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        return new ResponseValidator(value => predicate((TResponse)value));
    }

    /// <summary>
    /// Creates a typed ResponseValidator from a typed boolean predicate function.
    /// </summary>
    public static ResponseValidator FromPredicate<TResponse>(Func<TResponse, bool> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        return new ResponseValidator(value => predicate((TResponse)value));
    }
}
