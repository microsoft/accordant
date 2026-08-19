// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

using Microsoft.Accordant;

/// <summary>
/// Marks incomplete knowledge in an executable Accordant model.
/// </summary>
/// <remarks>
/// These annotations are intended for trace replay. Unknown regions are not suitable for
/// Accordant state exploration or generated conformance tests until they are refined.
/// </remarks>
public static class Research
{
    private const string UnknownPrefix = "UNKNOWN";

    /// <summary>
    /// Marks a region for which the model makes no response or transition claim.
    /// Direct Accordant validation fails safely; <see cref="TraceReplayer"/> reports the
    /// call as unknown and stops before its state becomes unreliable.
    /// </summary>
    public static ExpectedOutcomes Unknown<TResponse>(string id, string reason)
    {
        Validate(id, reason, nameof(reason));

        var message = $"{UnknownPrefix}[{id}]: {reason}";
        var outcome = Expect.That<TResponse>(_ => false, message).SameState().Build();
        return new ExpectedOutcomes(new ResearchExpectedOutcome(
            outcome,
            ResearchExpectationKind.Unknown,
            id,
            reason));
    }

    /// <summary>
    /// Marks an executable expectation as useful but still requiring refinement.
    /// Accordant verifies the wrapped outcomes normally.
    /// </summary>
    public static ExpectedOutcomes Provisional(
        string id,
        string question,
        ExpectedOutcomes expectation)
    {
        Validate(id, question, nameof(question));
        ArgumentNullException.ThrowIfNull(expectation);

        if (expectation.PossibleOutcomes.Any(outcome => outcome is ResearchExpectedOutcome))
        {
            throw new ArgumentException(
                "A research expectation cannot be nested inside Provisional.",
                nameof(expectation));
        }

        return new ExpectedOutcomes(expectation.PossibleOutcomes
            .Select(outcome => new ResearchExpectedOutcome(
                outcome,
                ResearchExpectationKind.Provisional,
                id,
                question))
            .Cast<ExpectedOutcome>()
            .ToArray());
    }

    private static void Validate(string id, string text, string textParameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(text, textParameterName);
    }
}

/// <summary>
/// The research status attached to an <see cref="ExpectedOutcomes"/> instance.
/// </summary>
public enum ResearchExpectationKind
{
    Unknown,
    Provisional,
}

/// <summary>
/// One expected outcome carrying model-mining metadata. Accordant treats this exactly as
/// <see cref="ExpectedOutcome"/>.
/// </summary>
public sealed class ResearchExpectedOutcome : ExpectedOutcome
{
    public ResearchExpectationKind Kind { get; }

    public string Id { get; }

    public string Detail { get; }

    internal ResearchExpectedOutcome(
        ExpectedOutcome inner,
        ResearchExpectationKind kind,
        string id,
        string detail)
        : base(
            inner.Validator,
            inner.NextStateGenerator,
            inner.NextStepFunctions,
            inner.MockResponseGenerator)
    {
        Kind = kind;
        Id = id;
        Detail = detail;
    }
}
