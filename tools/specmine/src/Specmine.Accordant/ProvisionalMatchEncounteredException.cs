// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

/// <summary>
/// Thrown by <see cref="Understanding.Provisional"/> when
/// <see cref="Understanding.CurrentStrictness"/> is <see cref="UnderstandingStrictness.Strict"/>
/// and the wrapped expectation actually matched the observed response. A non-matching response
/// is an ordinary rejection and does not throw this.
/// </summary>
public sealed class ProvisionalMatchEncounteredException : UnderstandingException
{
    public ProvisionalMatchEncounteredException(string id, string reason)
        : base(id, reason)
    {
    }
}
