// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

/// <summary>
/// Governs what happens when an <see cref="Understanding.Unknown{TResponse}"/> or
/// <see cref="Understanding.Provisional"/> marker is evaluated. Set via
/// <see cref="Understanding.CurrentStrictness"/>/<see cref="Understanding.UseStrictness"/>.
/// </summary>
public enum UnderstandingStrictness
{
    /// <summary>
    /// Default. <c>Unknown</c> fails validation with an <c>UNKNOWN[id]</c> explanation;
    /// <c>Provisional</c> validates its wrapped expectation honestly. Neither throws.
    /// </summary>
    Reject,

    /// <summary>
    /// <c>Unknown</c> passes validation (state unchanged); <c>Provisional</c> validates its
    /// wrapped expectation honestly. Neither throws.
    /// </summary>
    Accept,

    /// <summary>
    /// <c>Unknown</c> throws <see cref="UnknownRegionEncounteredException"/> unconditionally.
    /// <c>Provisional</c> throws <see cref="ProvisionalMatchEncounteredException"/> only when
    /// its wrapped expectation actually matches - a non-matching response still surfaces as an
    /// ordinary rejection.
    /// </summary>
    Strict,
}
