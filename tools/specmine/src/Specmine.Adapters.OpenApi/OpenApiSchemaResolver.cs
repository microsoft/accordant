// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Adapters.OpenApi;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Recursively resolves local <c>#/components/schemas/...</c> references in an OpenAPI
/// 3.0.x schema into a fully inlined <see cref="JsonNode"/> tree, so every other part of
/// this adapter can treat a schema as a plain, self-contained JSON Schema document with no
/// remaining <c>$ref</c> indirection.
///
/// Only local component schema references are supported: an external, non-local, or
/// otherwise-shaped reference is reported clearly via <see cref="OpenApiDocumentException"/>
/// rather than silently ignored or misinterpreted. A reference cycle (directly or through
/// intermediate schemas) is detected and reported the same way rather than recursing
/// forever - this adapter has no need to represent a genuinely recursive schema, since
/// none of the benchmark documents it targets define one.
/// </summary>
internal static class OpenApiSchemaResolver
{
    private const string ComponentSchemaRefPrefix = "#/components/schemas/";

    /// <summary>
    /// Resolves <paramref name="schema"/> - which may itself be a <c>$ref</c>, or may
    /// contain one anywhere nested within <c>properties</c>/<c>items</c>/
    /// <c>additionalProperties</c>/<c>oneOf</c>/<c>anyOf</c>/<c>allOf</c>/<c>not</c> - into
    /// a fully inlined, independent <see cref="JsonNode"/> tree.
    /// </summary>
    /// <param name="schema">The schema to resolve, as authored inline in the document.</param>
    /// <param name="componentsSchemas">
    /// The document's <c>components.schemas</c> object (or <see cref="JsonValueKind.Undefined"/>
    /// if the document declares none), used to look up any <c>$ref</c> encountered.
    /// </param>
    public static JsonNode Resolve(JsonElement schema, JsonElement componentsSchemas) =>
        Resolve(schema, componentsSchemas, resolutionStack: new List<string>(), cache: new Dictionary<string, JsonNode>(StringComparer.Ordinal));

    private static JsonNode Resolve(
        JsonElement schema, JsonElement componentsSchemas, List<string> resolutionStack, Dictionary<string, JsonNode> cache)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            // A boolean JSON Schema (`true`/`false`) or another non-object literal - not
            // used by any OpenAPI 3.0.x schema these benchmarks author, but there is no
            // reason to reject it: clone it verbatim since it cannot contain a '$ref'.
            return JsonNode.Parse(schema.GetRawText())!;
        }

        if (schema.TryGetProperty("$ref", out var refElement))
        {
            return ResolveRef(refElement, componentsSchemas, resolutionStack, cache);
        }

        var result = new JsonObject();
        foreach (var property in schema.EnumerateObject())
        {
            result[property.Name] = property.Name switch
            {
                "properties" => ResolvePropertiesMap(property.Value, componentsSchemas, resolutionStack, cache),
                "items" => Resolve(property.Value, componentsSchemas, resolutionStack, cache),
                "additionalProperties" when property.Value.ValueKind == JsonValueKind.Object =>
                    Resolve(property.Value, componentsSchemas, resolutionStack, cache),
                "oneOf" or "anyOf" or "allOf" => ResolveSchemaArray(property.Value, componentsSchemas, resolutionStack, cache),
                "not" => Resolve(property.Value, componentsSchemas, resolutionStack, cache),
                _ => JsonNode.Parse(property.Value.GetRawText()),
            };
        }

        return result;
    }

    private static JsonNode ResolveRef(
        JsonElement refElement, JsonElement componentsSchemas, List<string> resolutionStack, Dictionary<string, JsonNode> cache)
    {
        if (refElement.ValueKind != JsonValueKind.String)
        {
            throw new OpenApiDocumentException("A '$ref' value must be a string.");
        }

        var refPath = refElement.GetString()!;
        if (!refPath.StartsWith(ComponentSchemaRefPrefix, StringComparison.Ordinal))
        {
            throw new OpenApiDocumentException(
                $"Unsupported schema reference '{refPath}'; only local '{ComponentSchemaRefPrefix}...' " +
                $"references are supported.");
        }

        var schemaName = refPath[ComponentSchemaRefPrefix.Length..];

        // A cache hit means this name was already fully resolved on some earlier,
        // unrelated branch of the walk - safe to reuse regardless of the current
        // resolution stack, since it cannot itself be part of an active cycle.
        if (cache.TryGetValue(schemaName, out var cached))
        {
            return cached.DeepClone();
        }

        if (resolutionStack.Contains(schemaName, StringComparer.Ordinal))
        {
            throw new OpenApiDocumentException(
                $"Cyclic schema reference detected while resolving '{schemaName}' (reference chain: " +
                $"{string.Join(" -> ", resolutionStack)} -> {schemaName}).");
        }

        if (componentsSchemas.ValueKind != JsonValueKind.Object ||
            !componentsSchemas.TryGetProperty(schemaName, out var targetSchema))
        {
            throw new OpenApiDocumentException(
                $"Schema reference '{refPath}' does not resolve to a defined component schema.");
        }

        resolutionStack.Add(schemaName);
        var resolved = Resolve(targetSchema, componentsSchemas, resolutionStack, cache);
        resolutionStack.RemoveAt(resolutionStack.Count - 1);

        cache[schemaName] = resolved;
        return resolved.DeepClone();
    }

    private static JsonNode ResolvePropertiesMap(
        JsonElement propertiesElement, JsonElement componentsSchemas, List<string> resolutionStack, Dictionary<string, JsonNode> cache)
    {
        if (propertiesElement.ValueKind != JsonValueKind.Object)
        {
            throw new OpenApiDocumentException("A schema's 'properties' must be a JSON object.");
        }

        var result = new JsonObject();
        foreach (var property in propertiesElement.EnumerateObject())
        {
            result[property.Name] = Resolve(property.Value, componentsSchemas, resolutionStack, cache);
        }

        return result;
    }

    private static JsonNode ResolveSchemaArray(
        JsonElement arrayElement, JsonElement componentsSchemas, List<string> resolutionStack, Dictionary<string, JsonNode> cache)
    {
        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            throw new OpenApiDocumentException(
                "A schema composition keyword ('oneOf'/'anyOf'/'allOf') must be a JSON array.");
        }

        var result = new JsonArray();
        foreach (var item in arrayElement.EnumerateArray())
        {
            result.Add(Resolve(item, componentsSchemas, resolutionStack, cache));
        }

        return result;
    }
}
