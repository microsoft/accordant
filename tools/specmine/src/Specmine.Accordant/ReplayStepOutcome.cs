// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

/// <summary>
/// The outcome of replaying one <see cref="Specmine.RecordedCall"/> from a trace against an
/// Accordant <c>Spec&lt;TState&gt;</c>.
/// </summary>
public enum ReplayStepOutcome
{
    /// <summary>
    /// The call's operation is modeled, its request and response deserialized cleanly, and
    /// <c>Spec&lt;TState&gt;.Allows</c> accepted the response given the state at that point.
    /// </summary>
    Conforming,

    /// <summary>
    /// The observed response matched an executable expectation marked with
    /// <see cref="Research.Provisional"/>. The state transition is reliable, but the model
    /// explicitly identifies this claim as needing further refinement.
    /// </summary>
    ProvisionalMatch,

    /// <summary>
    /// The model explicitly marked this request/state region with
    /// <see cref="Research.Unknown{TResponse}"/>. No response or state-transition claim was
    /// made, so replay stops without classifying the observation as a model violation.
    /// </summary>
    Unknown,

    /// <summary>
    /// The call's operation is modeled and both its request and response deserialized
    /// cleanly, but <c>Spec&lt;TState&gt;.Allows</c> rejected the observed response: the
    /// model disagrees with what the trace recorded. See <see cref="ReplayStepResult.Message"/>
    /// for Accordant's explanation.
    /// </summary>
    ModelViolation,

    /// <summary>
    /// The call's <c>OperationName</c> does not match any operation registered in the spec.
    /// A partial model is legitimate: this is reported explicitly rather than silently
    /// accepted or treated as a <see cref="ModelViolation"/>.
    /// </summary>
    OperationNotModeled,

    /// <summary>
    /// The recorded call captured an execution error (its execution delegate failed to
    /// produce a response) rather than a response to validate.
    /// </summary>
    ExecutionError,

    /// <summary>
    /// The call's operation is modeled, but its recorded request JSON failed to deserialize
    /// into the operation's declared request type.
    /// </summary>
    RequestDeserializationFailed,

    /// <summary>
    /// The call's operation is modeled, but its recorded response JSON failed to deserialize
    /// into the operation's declared response type.
    /// </summary>
    ResponseDeserializationFailed
}
