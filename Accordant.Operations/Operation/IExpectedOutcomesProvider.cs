// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

/// <summary>
/// Exposes the expected outcomes produced for a concrete request and state.
/// </summary>
/// <remarks>
/// This optional interface lets tooling inspect annotations carried by an
/// <see cref="ExpectedOutcomes"/> instance without changing verification semantics.
/// Callers may invoke it before verification, so an operation's model application must be
/// deterministic, side-effect free, and inexpensive.
/// </remarks>
public interface IExpectedOutcomesProvider
{
    /// <summary>
    /// Applies the operation's behavioral model to <paramref name="request"/> and
    /// <paramref name="state"/>.
    /// </summary>
    ExpectedOutcomes GetExpectedOutcomes(object request, IState state);
}
