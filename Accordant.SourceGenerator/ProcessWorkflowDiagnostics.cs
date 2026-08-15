// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.SourceGenerator;

using Microsoft.CodeAnalysis;

/// <summary>
/// Diagnostics for the experimental structured process workflow surface.
/// </summary>
public static class ProcessWorkflowDiagnostics
{
    private const string Category =
        "Microsoft.Accordant.ModelChecking.Experimental.Coroutines";

    /// <summary>
    /// ACC1001: A ModelTask or ModelTask&lt;T&gt; is awaited directly instead of
    /// being invoked through ModelContext&lt;TState&gt;.Call.
    /// </summary>
    public static readonly DiagnosticDescriptor DirectModelTaskAwait = new(
        id: "ACC1001",
        title: "ModelTask helpers must be invoked through Call",
        messageFormat:
            "Do not await '{0}' directly; invoke checkpoint-bearing helpers through " +
            "ModelContext<TState>.Call to establish an explicit process frame",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Direct ModelTask awaits bypass the explicit structured frame required for nested " +
            "checkpoint-bearing helpers.");

    /// <summary>
    /// ACC1002: A ModelTask workflow awaits a non-Accordant awaitable.
    /// </summary>
    public static readonly DiagnosticDescriptor ForeignAwait = new(
        id: "ACC1002",
        title: "Foreign awaits are not deterministic process checkpoints",
        messageFormat:
            "Awaiting '{0}' inside a ModelTask workflow is unsupported; only Accordant " +
            "ModelAwaitable<T> checkpoints may be awaited",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Task, ValueTask, custom awaiters, and other external asynchronous work cannot be " +
            "re-executed deterministically by the structured process runtime.");

    /// <summary>
    /// ACC1003: A raw language loop contains an Accordant model checkpoint.
    /// </summary>
    public static readonly DiagnosticDescriptor RawCheckpointLoop = new(
        id: "ACC1003",
        title: "Use structured process iteration",
        messageFormat:
            "Raw '{0}' loop contains an Accordant model checkpoint; use ModelContext<TState>.Forever " +
            "or an appropriate structured process construct",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Checkpoint-bearing iteration needs an explicit structured frame so iteration-local " +
            "checkpoint history can be discarded safely.");

    /// <summary>
    /// ACC1004: A statically known checkpoint or frame value type is outside
    /// the immutable scalar whitelist.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedScalarType = new(
        id: "ACC1004",
        title: "Unsupported process checkpoint value type",
        messageFormat:
            "Type '{0}' is not supported for {1}; use null, string, a primitive scalar, enum, " +
            "DateTime, DateTimeOffset, TimeSpan, Guid, ModelUnit, or a nullable form",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Structured process checkpoint-history values and semantic subjects must use the runtime's " +
            "immutable scalar whitelist.");
}
