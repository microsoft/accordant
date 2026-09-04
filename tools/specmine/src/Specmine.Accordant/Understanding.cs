// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

using System.Linq;
using System.Threading;
using Microsoft.Accordant;

/// <summary>
/// Marks incomplete knowledge in an executable Accordant model.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Unknown{TResponse}"/> and <see cref="Provisional"/> wrap an outcome's
/// <see cref="ResponseValidator"/>, not the <see cref="ExpectedOutcome"/> type itself, so the
/// marker is evaluated exactly once - inside whatever single call
/// (<c>Spec&lt;TState&gt;.Allows</c>, Accordant's own verification, or a hand-written check)
/// happens to invoke it. There is no separate inspection pass and no reliance on
/// <c>IExpectedOutcomesProvider</c> or any other side channel: the marker's behavior and its
/// reporting both live in the validator closure itself, governed by the ambient
/// <see cref="CurrentStrictness"/> for the duration of that one call.
/// </para>
/// <para>
/// Because the ambient state is <see cref="AsyncLocal{T}"/>, it flows with the logical call
/// (including across <c>await</c>) but never leaks across independent call chains (e.g.
/// parallel test cases), and a nested <see cref="UseStrictness"/> scope is restored on
/// <see cref="System.IDisposable.Dispose"/> even if the scope's body throws.
/// </para>
/// </remarks>
public static class Understanding
{
    private const string UnknownPrefix = "UNKNOWN";

    private static readonly AsyncLocal<UnderstandingStrictness> StrictnessLocal = new();

    private static readonly AsyncLocal<UnderstandingEncounter?> LastEncounterLocal = new();

    /// <summary>
    /// The strictness in effect for <see cref="Unknown{TResponse}"/>/<see cref="Provisional"/>
    /// markers evaluated on the current logical call. Defaults to
    /// <see cref="UnderstandingStrictness.Reject"/> when nothing has set it. Prefer
    /// <see cref="UseStrictness"/> over setting this directly, so the previous value is
    /// restored even if the scoped work throws.
    /// </summary>
    public static UnderstandingStrictness CurrentStrictness
    {
        get => StrictnessLocal.Value;
        set => StrictnessLocal.Value = value;
    }

    /// <summary>
    /// The most recent <see cref="Unknown{TResponse}"/>/<see cref="Provisional"/> marker
    /// evaluated on the current logical call (a real match, for <c>Provisional</c>), or
    /// <see langword="null"/> if none has been evaluated since the last
    /// <see cref="ClearLastEncounter"/>. Callers that need to know whether a given
    /// <c>Spec&lt;TState&gt;.Allows</c> call passed through an understanding marker (e.g.
    /// <see cref="TraceReplayer"/>) should call <see cref="ClearLastEncounter"/> immediately
    /// before that call and read this property immediately after.
    /// </summary>
    public static UnderstandingEncounter? LastEncounter => LastEncounterLocal.Value;

    /// <summary>
    /// Clears <see cref="LastEncounter"/>. Call this immediately before a validation call whose
    /// understanding encounters (if any) you intend to inspect afterward.
    /// </summary>
    public static void ClearLastEncounter() => LastEncounterLocal.Value = null;

    /// <summary>
    /// Runs with <see cref="CurrentStrictness"/> set to <paramref name="strictness"/> for the
    /// scope of the returned <see cref="System.IDisposable"/>, restoring the previous value on
    /// <c>Dispose</c> (including when the scoped work throws).
    /// </summary>
    public static IDisposable UseStrictness(UnderstandingStrictness strictness)
    {
        var previous = CurrentStrictness;
        CurrentStrictness = strictness;
        return new StrictnessScope(previous);
    }

    /// <summary>
    /// Restricts a model's claims to requests/states satisfying <paramref name="condition"/>.
    /// Call this at the top of an <c>Operation</c>'s model function before returning any
    /// <see cref="ExpectedOutcomes"/>. When <paramref name="condition"/> is <see langword="false"/>,
    /// throws <see cref="AssumptionViolatedException"/> instead of forcing the model to make a
    /// response/state-transition claim it hasn't actually investigated - this keeps the model a
    /// partial function over requests, the same way <see cref="Unknown{TResponse}"/> keeps it
    /// partial over responses. Unlike <see cref="Unknown{TResponse}"/>/<see cref="Provisional"/>,
    /// this always throws - there is no permissive mode for a request the model was never asked
    /// to cover.
    /// </summary>
    /// <param name="condition">The precondition the current request/state must satisfy.</param>
    /// <param name="id">Stable marker ID, reported alongside the failure for tracking.</param>
    /// <param name="reason">Human-readable explanation of the scoping boundary.</param>
    public static void Assume(bool condition, string id, string reason)
    {
        Validate(id, reason, nameof(reason));

        if (!condition)
        {
            throw new AssumptionViolatedException(id, reason);
        }
    }

    /// <summary>
    /// Marks a region for which the model makes no response or transition claim. Behavior is
    /// governed by <see cref="CurrentStrictness"/> at the moment the wrapped validator actually
    /// runs:
    /// <list type="bullet">
    /// <item><see cref="UnderstandingStrictness.Reject"/> (default) - validation fails with an
    /// <c>UNKNOWN[id]</c> explanation.</item>
    /// <item><see cref="UnderstandingStrictness.Accept"/> - validation passes, state unchanged.</item>
    /// <item><see cref="UnderstandingStrictness.Strict"/> - throws
    /// <see cref="UnknownRegionEncounteredException"/> unconditionally.</item>
    /// </list>
    /// In the non-throwing modes, records an <see cref="UnderstandingEncounter"/> (see
    /// <see cref="LastEncounter"/>) every time the validator runs, since reaching this region at
    /// all - regardless of which way it resolves - means the model was asked about a
    /// request/state it has no rule for.
    /// </summary>
    public static ExpectedOutcomes Unknown<TResponse>(string id, string reason)
    {
        Validate(id, reason, nameof(reason));

        var validator = new ResponseValidator(_ =>
        {
            var strictness = CurrentStrictness;

            if (strictness == UnderstandingStrictness.Strict)
            {
                throw new UnknownRegionEncounteredException(id, reason);
            }

            RecordEncounter(UnderstandingKind.Unknown, id, reason);

            return strictness == UnderstandingStrictness.Accept
                ? ValidationResult.Valid()
                : ValidationResult.Invalid($"{UnknownPrefix}[{id}]: {reason}");
        });

        return Expect.That<TResponse>(validator).SameState().Build();
    }

    /// <summary>
    /// Marks an executable expectation as useful but still requiring refinement. The wrapped
    /// expectation is always evaluated honestly - a response that fails it is an ordinary
    /// rejection, not an understanding encounter. Only a response that satisfies it is governed
    /// by <see cref="CurrentStrictness"/>:
    /// <list type="bullet">
    /// <item><see cref="UnderstandingStrictness.Reject"/> (default) or
    /// <see cref="UnderstandingStrictness.Accept"/> - validation passes as usual, and an
    /// <see cref="UnderstandingEncounter"/> is recorded (see <see cref="LastEncounter"/>).</item>
    /// <item><see cref="UnderstandingStrictness.Strict"/> - throws
    /// <see cref="ProvisionalMatchEncounteredException"/> instead of passing.</item>
    /// </list>
    /// </summary>
    public static ExpectedOutcomes Provisional(
        string id,
        string question,
        ExpectedOutcomes expectation)
    {
        Validate(id, question, nameof(question));
        ArgumentNullException.ThrowIfNull(expectation);

        return new ExpectedOutcomes(expectation.PossibleOutcomes
            .Select(outcome => WrapForProvisional(outcome, id, question))
            .ToArray());
    }

    private static ExpectedOutcome WrapForProvisional(ExpectedOutcome inner, string id, string question)
    {
        var validator = new ResponseValidator(response =>
        {
            var result = inner.Validator.Validate(response);

            if (!result.IsValid)
            {
                // An ordinary rejection - not an understanding encounter, so it stays a plain
                // (eventual) model violation rather than being flagged as provisional.
                return result;
            }

            if (CurrentStrictness == UnderstandingStrictness.Strict)
            {
                throw new ProvisionalMatchEncounteredException(id, question);
            }

            RecordEncounter(UnderstandingKind.Provisional, id, question);
            return result;
        });

        return new ExpectedOutcome(
            validator,
            inner.NextStateGenerator,
            inner.NextStepFunctions,
            inner.MockResponseGenerator);
    }

    private static void RecordEncounter(UnderstandingKind kind, string id, string detail) =>
        LastEncounterLocal.Value = new UnderstandingEncounter(kind, id, detail);

    private static void Validate(string id, string text, string textParameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(text, textParameterName);
    }

    private sealed class StrictnessScope : IDisposable
    {
        private readonly UnderstandingStrictness previous;
        private bool disposed;

        public StrictnessScope(UnderstandingStrictness previous)
        {
            this.previous = previous;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CurrentStrictness = previous;
        }
    }
}

/// <summary>
/// The kind of understanding marker recorded in an <see cref="UnderstandingEncounter"/>.
/// </summary>
public enum UnderstandingKind
{
    Unknown,
    Provisional,
}

/// <summary>
/// Records that an <see cref="Understanding.Unknown{TResponse}"/> or
/// <see cref="Understanding.Provisional"/> marker was evaluated (and, for
/// <see cref="UnderstandingKind.Provisional"/>, matched) on the current logical call. See
/// <see cref="Understanding.LastEncounter"/>.
/// </summary>
public sealed record UnderstandingEncounter(UnderstandingKind Kind, string Id, string Detail);
