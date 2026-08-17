// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;
using System.Text.Json.Schema;

/// <summary>
/// Describes one operation an <see cref="ITargetSession"/> exposes: a stable name plus
/// the request and response shapes, each expressed as JSON Schema.
///
/// JSON Schema is this SDK's portable operation contract - the shape every adapter,
/// trace, and tool ultimately agrees on. An adapter's internal implementation is free to
/// use whatever C# types (or no types at all) it likes; only the JSON Schema it declares
/// here and the JSON values it exchanges through <see cref="ITargetSession.ExecuteAsync"/>
/// need to be portable.
/// </summary>
public sealed class OperationDefinition
{
    /// <summary>The operation's stable, non-blank name (e.g. <c>"CreateTask"</c>).</summary>
    public string Name { get; }

    /// <summary>The JSON Schema describing the shape of a valid request.</summary>
    public JsonElement RequestSchema { get; }

    /// <summary>The JSON Schema describing the shape of a valid response.</summary>
    public JsonElement ResponseSchema { get; }

    /// <summary>
    /// Creates an operation definition from already-authored JSON Schemas.
    /// </summary>
    /// <param name="name">The operation's stable, non-blank name.</param>
    /// <param name="requestSchema">The JSON Schema describing a valid request.</param>
    /// <param name="responseSchema">The JSON Schema describing a valid response.</param>
    public OperationDefinition(string name, JsonElement requestSchema, JsonElement responseSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireDefinedSchema(requestSchema, nameof(requestSchema));
        RequireDefinedSchema(responseSchema, nameof(responseSchema));

        Name = name;

        // Clone so this definition owns independent memory: later mutation, reuse, or
        // disposal of whatever JsonDocument the caller's schema came from can never
        // silently alter this definition after construction.
        RequestSchema = requestSchema.Clone();
        ResponseSchema = responseSchema.Clone();
    }

    private static void RequireDefinedSchema(JsonElement schema, string paramName)
    {
        if (schema.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Schema must be a valid JSON value.", paramName);
        }
    }

    /// <summary>
    /// Derives an <see cref="OperationDefinition"/> from C# request/response types using
    /// .NET's built-in <see cref="JsonSchemaExporter"/>, so an adapter that already has
    /// concrete request/response types does not need to hand-author JSON Schema. The
    /// derived schema is still the operation's real, portable contract - the C# types are
    /// only used to produce it and play no further role once this definition exists.
    /// </summary>
    /// <typeparam name="TRequest">The C# type describing a valid request.</typeparam>
    /// <typeparam name="TResponse">The C# type describing a valid response.</typeparam>
    /// <param name="name">The operation's stable, non-blank name.</param>
    /// <param name="serializerOptions">
    /// The serializer options to derive schemas with (for example, a custom naming
    /// policy). Defaults to <see cref="JsonSerializerOptions.Default"/> when omitted; the
    /// options passed here must have (or default to) a type info resolver, per
    /// <see cref="JsonSchemaExporter"/>'s own requirement.
    /// </param>
    public static OperationDefinition Create<TRequest, TResponse>(
        string name, JsonSerializerOptions? serializerOptions = null)
    {
        var options = serializerOptions ?? JsonSerializerOptions.Default;

        // Non-nullable C# reference type properties (the common case for request/response
        // records) would otherwise be widened to a ["<type>", "null"] union purely because
        // the exporter cannot see nullable-reference-type annotations from reflection;
        // opting out of that keeps the derived schema close to what a person would author
        // by hand for an ordinary required property.
        var exporterOptions = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true };

        var requestSchema = JsonSchemaExporter.GetJsonSchemaAsNode(options, typeof(TRequest), exporterOptions);
        var responseSchema = JsonSchemaExporter.GetJsonSchemaAsNode(options, typeof(TResponse), exporterOptions);

        return new OperationDefinition(
            name,
            JsonSerializer.SerializeToElement(requestSchema),
            JsonSerializer.SerializeToElement(responseSchema));
    }
}
