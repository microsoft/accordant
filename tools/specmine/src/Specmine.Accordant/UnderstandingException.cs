// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

/// <summary>
/// Base type for exceptions thrown by <see cref="Understanding"/> scoping markers. These
/// signal that a model was never asked to make a claim for the current request/state - not
/// that it made a claim and got it wrong (that is a <c>SpecException</c> or a model violation
/// from <c>Spec&lt;TState&gt;.Allows</c>).
/// </summary>
public abstract class UnderstandingException : Exception
{
    /// <summary>
    /// Stable marker ID, matching the <c>id</c> passed to the <see cref="Understanding"/>
    /// method that threw this exception.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human-readable explanation of why this region is out of the model's declared scope.
    /// </summary>
    public string Reason { get; }

    private protected UnderstandingException(string id, string reason)
        : base($"{id}: {reason}")
    {
        Id = id;
        Reason = reason;
    }
}
