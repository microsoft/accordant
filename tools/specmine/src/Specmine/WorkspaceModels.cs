// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Declares the single target adapter a workspace is configured against: which adapter
/// implementation to use, and the adapter's own configuration.
///
/// The workspace schema intentionally knows nothing about what a particular adapter type
/// needs: <see cref="Settings"/> is an opaque JSON value whose shape is entirely owned by
/// the adapter identified by <see cref="AdapterType"/> (for example an OpenAPI adapter's
/// base URL and document path, or a command adapter's executable and arguments). This
/// lets new adapter kinds define their own configuration without changing the workspace
/// schema or this type.
/// </summary>
public sealed record TargetAdapterDeclaration
{
    /// <summary>
    /// The adapter implementation this declaration selects (e.g. <c>"openapi"</c> or
    /// <c>"command"</c>). Interpreted only by adapter implementations, not by the
    /// workspace itself.
    /// </summary>
    public string AdapterType { get; }

    /// <summary>
    /// The adapter's own configuration, as an opaque JSON value. The workspace does not
    /// interpret, validate, or constrain its shape.
    /// </summary>
    public JsonElement Settings { get; }

    [JsonConstructor]
    public TargetAdapterDeclaration(string adapterType, JsonElement settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterType);

        if (settings.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Settings must be a valid JSON value.", nameof(settings));
        }

        AdapterType = adapterType;

        // Clone so this declaration owns independent memory: later mutation, reuse, or
        // disposal of whatever JsonDocument the caller's settings element came from can
        // never silently alter this declaration after construction.
        Settings = settings.Clone();
    }
}

/// <summary>
/// The full contents of a workspace's <c>workspace.json</c> declaration: a schema version
/// and the single target adapter the workspace investigates. Nothing else - no base URL,
/// credentials, capability taxonomy, reset model, or operation catalog belongs here; all
/// of that is either adapter-owned (inside <see cref="TargetAdapterDeclaration.Settings"/>)
/// or out of scope for this vertical slice entirely.
/// </summary>
public sealed record WorkspaceDocument
{
    /// <summary>
    /// The current schema version written by this library.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The schema version this workspace was written with.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// The declaration of the single target adapter this workspace investigates.
    /// </summary>
    public TargetAdapterDeclaration TargetAdapter { get; }

    [JsonConstructor]
    public WorkspaceDocument(int schemaVersion, TargetAdapterDeclaration targetAdapter)
    {
        ArgumentNullException.ThrowIfNull(targetAdapter);

        SchemaVersion = schemaVersion;
        TargetAdapter = targetAdapter;
    }
}