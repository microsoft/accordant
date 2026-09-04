// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

/// <summary>
/// Thrown by <see cref="Understanding.Assume"/> when a request/state falls outside the subset
/// of cases a model currently claims to cover. Distinct from a model violation: the model is
/// not disagreeing with the observed response, it is declaring that it was never asked to
/// cover this case in the first place.
/// </summary>
public sealed class AssumptionViolatedException : UnderstandingException
{
    public AssumptionViolatedException(string id, string reason)
        : base(id, reason)
    {
    }
}
