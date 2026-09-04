// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

/// <summary>
/// Thrown by <see cref="Understanding.Unknown{TResponse}"/> when
/// <see cref="Understanding.CurrentStrictness"/> is <see cref="UnderstandingStrictness.Strict"/>.
/// Reaching an unknown region at all - independent of the observed response - is the signal, so
/// this throws unconditionally.
/// </summary>
public sealed class UnknownRegionEncounteredException : UnderstandingException
{
    public UnknownRegionEncounteredException(string id, string reason)
        : base(id, reason)
    {
    }
}
