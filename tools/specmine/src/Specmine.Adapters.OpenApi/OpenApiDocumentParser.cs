// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Adapters.OpenApi;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Parses an OpenAPI 3.0.x JSON document into the operation catalog
/// <see cref="OpenApiTargetAdapter"/> advertises and executes against: one
/// <see cref="OpenApiOperation"/> per path operation with a nonblank, unique
/// <c>operationId</c>, each carrying a composite <c>{ path, query, headers, body }</c>
/// request schema and a <c>{ status, body }</c> response schema built from the
/// operation's documented parameters, request body, and responses.
///
/// This parser is deliberately bounded to what the three committed benchmark documents
/// need: <c>path</c>/<c>query</c>/<c>header</c> parameters, <c>application/json</c>
/// request bodies, and local <c>#/components/schemas/...</c> references (see
/// <see cref="OpenApiSchemaResolver"/>). Anything outside that - a <c>cookie</c>
/// parameter, a non-JSON request body, a missing/duplicate <c>operationId</c>, an
/// unsupported or cyclic schema reference - fails clearly via
/// <see cref="OpenApiDocumentException"/> rather than being silently ignored or
/// misrepresented.
/// </summary>
internal static class OpenApiDocumentParser
{
    private static readonly HashSet<string> SupportedHttpMethods = new(StringComparer.Ordinal)
    {
        "get", "put", "post", "delete", "options", "head", "patch", "trace",
    };

    private static readonly Regex PathPlaceholderRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    public static IReadOnlyList<OpenApiOperation> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new OpenApiDocumentException("The OpenAPI document must be a JSON object.");
        }

        var version = RequireNonBlankStringProperty(root, "openapi", "The OpenAPI document");
        if (!version.StartsWith("3.0.", StringComparison.Ordinal))
        {
            throw new OpenApiDocumentException(
                $"Unsupported OpenAPI version '{version}'; this adapter only supports OpenAPI 3.0.x documents.");
        }

        if (!root.TryGetProperty("paths", out var pathsElement) || pathsElement.ValueKind != JsonValueKind.Object)
        {
            throw new OpenApiDocumentException("The OpenAPI document does not declare a 'paths' object.");
        }

        var componentsSchemas = root.TryGetProperty("components", out var componentsElement) &&
            componentsElement.ValueKind == JsonValueKind.Object &&
            componentsElement.TryGetProperty("schemas", out var schemasElement) &&
            schemasElement.ValueKind == JsonValueKind.Object
                ? schemasElement
                : default;

        var operationsById = new Dictionary<string, OpenApiOperation>(StringComparer.Ordinal);

        foreach (var pathProperty in pathsElement.EnumerateObject())
        {
            ParsePathItem(pathProperty.Name, pathProperty.Value, componentsSchemas, operationsById);
        }

        return operationsById.Values.ToList();
    }

    private static void ParsePathItem(
        string pathTemplate,
        JsonElement pathItem,
        JsonElement componentsSchemas,
        Dictionary<string, OpenApiOperation> operationsById)
    {
        if (pathItem.ValueKind != JsonValueKind.Object)
        {
            throw new OpenApiDocumentException($"Path '{pathTemplate}' does not declare a JSON object path item.");
        }

        if (pathItem.TryGetProperty("$ref", out _))
        {
            throw new OpenApiDocumentException($"Path '{pathTemplate}': path item references ('$ref') are not supported.");
        }

        var sharedParameters = pathItem.TryGetProperty("parameters", out var sharedParametersElement)
            ? ParseParameterList(sharedParametersElement, componentsSchemas, $"Path '{pathTemplate}'")
            : new List<ParsedParameter>();

        foreach (var methodProperty in pathItem.EnumerateObject())
        {
            if (!SupportedHttpMethods.Contains(methodProperty.Name))
            {
                // Not an HTTP method keyword (e.g. "parameters", "summary", "description") - skip.
                continue;
            }

            var operation = ParseOperation(
                pathTemplate, methodProperty.Name, methodProperty.Value, sharedParameters, componentsSchemas);

            if (!operationsById.TryAdd(operation.ExecutionSpec.OperationId, operation))
            {
                throw new OpenApiDocumentException(
                    $"Duplicate operationId '{operation.ExecutionSpec.OperationId}' found at " +
                    $"'{operation.ExecutionSpec.HttpMethod} {pathTemplate}'.");
            }
        }
    }

    private static OpenApiOperation ParseOperation(
        string pathTemplate,
        string methodName,
        JsonElement operationElement,
        List<ParsedParameter> sharedParameters,
        JsonElement componentsSchemas)
    {
        var httpMethod = methodName.ToUpperInvariant();
        var context = $"Operation '{httpMethod} {pathTemplate}'";

        var operationId = operationElement.TryGetProperty("operationId", out var operationIdElement) &&
            operationIdElement.ValueKind == JsonValueKind.String
                ? operationIdElement.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new OpenApiDocumentException($"{context} does not declare a nonblank 'operationId'.");
        }

        var ownParameters = operationElement.TryGetProperty("parameters", out var ownParametersElement)
            ? ParseParameterList(ownParametersElement, componentsSchemas, context)
            : new List<ParsedParameter>();

        var effectiveParameters = MergeParameters(sharedParameters, ownParameters);
        ValidatePathPlaceholders(pathTemplate, effectiveParameters, context);

        var pathParams = effectiveParameters.Where(p => p.Location == OpenApiParameterLocation.Path).ToList();
        var queryParams = effectiveParameters.Where(p => p.Location == OpenApiParameterLocation.Query).ToList();
        var headerParams = effectiveParameters.Where(p => p.Location == OpenApiParameterLocation.Header).ToList();

        var requestBody = ParseRequestBody(operationElement, componentsSchemas, context);
        var responseBodySchemas = ParseResponses(operationElement, componentsSchemas, context);

        var requestSchema = OpenApiSchemaBuilder.BuildRequestSchema(pathParams, queryParams, headerParams, requestBody);
        var responseSchema = OpenApiSchemaBuilder.BuildResponseSchema(responseBodySchemas);

        var definition = new OperationDefinition(
            operationId,
            JsonSerializer.SerializeToElement(requestSchema),
            JsonSerializer.SerializeToElement(responseSchema));

        var executionSpec = new OpenApiOperationSpec(
            operationId,
            httpMethod,
            pathTemplate,
            pathParams.Select(p => new OpenApiParameterSpec(p.Name, p.Required)).ToList(),
            queryParams.Select(p => new OpenApiParameterSpec(p.Name, p.Required)).ToList(),
            headerParams.Select(p => new OpenApiParameterSpec(p.Name, p.Required)).ToList(),
            requestBody is null ? null : new OpenApiRequestBodySpec(requestBody.Required));

        return new OpenApiOperation(definition, executionSpec);
    }

    private static List<ParsedParameter> ParseParameterList(
        JsonElement parametersElement, JsonElement componentsSchemas, string context)
    {
        if (parametersElement.ValueKind != JsonValueKind.Array)
        {
            throw new OpenApiDocumentException($"{context}: 'parameters' must be a JSON array.");
        }

        var parameters = new List<ParsedParameter>();
        var seen = new HashSet<(OpenApiParameterLocation, string)>();

        foreach (var parameterElement in parametersElement.EnumerateArray())
        {
            var parameter = ParseParameter(parameterElement, componentsSchemas, context);
            if (!seen.Add((parameter.Location, parameter.Name)))
            {
                throw new OpenApiDocumentException(
                    $"{context}: parameter '{parameter.Name}' ({parameter.Location}) is declared more than once.");
            }

            parameters.Add(parameter);
        }

        return parameters;
    }

    private static ParsedParameter ParseParameter(JsonElement parameterElement, JsonElement componentsSchemas, string context)
    {
        if (parameterElement.TryGetProperty("$ref", out _))
        {
            throw new OpenApiDocumentException($"{context}: parameter references ('$ref') are not supported.");
        }

        var name = RequireNonBlankStringProperty(parameterElement, "name", context);
        var location = RequireNonBlankStringProperty(parameterElement, "in", context);
        var required = parameterElement.TryGetProperty("required", out var requiredElement) &&
            requiredElement.ValueKind == JsonValueKind.True;

        var parsedLocation = location switch
        {
            "path" => OpenApiParameterLocation.Path,
            "query" => OpenApiParameterLocation.Query,
            "header" => OpenApiParameterLocation.Header,
            "cookie" => throw new OpenApiDocumentException(
                $"{context}: parameter '{name}' uses unsupported location 'cookie'; only 'path', 'query', " +
                $"and 'header' parameters are supported."),
            _ => throw new OpenApiDocumentException($"{context}: parameter '{name}' declares unrecognized location '{location}'."),
        };

        if (parsedLocation == OpenApiParameterLocation.Path && !required)
        {
            throw new OpenApiDocumentException($"{context}: path parameter '{name}' must declare 'required: true'.");
        }

        var schema = parameterElement.TryGetProperty("schema", out var schemaElement)
            ? OpenApiSchemaResolver.Resolve(schemaElement, componentsSchemas)
            : new JsonObject();

        return new ParsedParameter(name, parsedLocation, required, schema);
    }

    private static List<ParsedParameter> MergeParameters(List<ParsedParameter> shared, List<ParsedParameter> own)
    {
        if (shared.Count == 0)
        {
            return own;
        }

        // Operation-level parameters override a path-item-level parameter with the same
        // name and location, per the OpenAPI 3.0.x specification.
        var ownKeys = own.Select(p => (p.Location, p.Name)).ToHashSet();
        var merged = shared.Where(p => !ownKeys.Contains((p.Location, p.Name))).ToList();
        merged.AddRange(own);
        return merged;
    }

    private static void ValidatePathPlaceholders(string pathTemplate, List<ParsedParameter> parameters, string context)
    {
        var placeholders = PathPlaceholderRegex.Matches(pathTemplate)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var declaredPathParams = parameters
            .Where(p => p.Location == OpenApiParameterLocation.Path)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = placeholders.Except(declaredPathParams).ToList();
        if (undeclared.Count > 0)
        {
            throw new OpenApiDocumentException(
                $"{context}: path template '{pathTemplate}' references undeclared path parameter(s): " +
                $"{string.Join(", ", undeclared)}.");
        }

        var unused = declaredPathParams.Except(placeholders).ToList();
        if (unused.Count > 0)
        {
            throw new OpenApiDocumentException(
                $"{context}: declares path parameter(s) not present in path template '{pathTemplate}': " +
                $"{string.Join(", ", unused)}.");
        }
    }

    private static ParsedRequestBody? ParseRequestBody(JsonElement operationElement, JsonElement componentsSchemas, string context)
    {
        if (!operationElement.TryGetProperty("requestBody", out var requestBodyElement))
        {
            return null;
        }

        if (requestBodyElement.TryGetProperty("$ref", out _))
        {
            throw new OpenApiDocumentException($"{context}: requestBody references ('$ref') are not supported.");
        }

        var required = requestBodyElement.TryGetProperty("required", out var requiredElement) &&
            requiredElement.ValueKind == JsonValueKind.True;

        if (!requestBodyElement.TryGetProperty("content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.Object)
        {
            throw new OpenApiDocumentException($"{context}: requestBody does not declare a 'content' object.");
        }

        if (!contentElement.TryGetProperty("application/json", out var jsonMediaType))
        {
            var declaredTypes = string.Join(", ", contentElement.EnumerateObject().Select(p => p.Name));
            throw new OpenApiDocumentException(
                $"{context}: requestBody does not declare an 'application/json' content type " +
                $"(found: {(declaredTypes.Length == 0 ? "none" : declaredTypes)}); only JSON request bodies are supported.");
        }

        var schema = jsonMediaType.TryGetProperty("schema", out var schemaElement)
            ? OpenApiSchemaResolver.Resolve(schemaElement, componentsSchemas)
            : new JsonObject();

        return new ParsedRequestBody(required, schema);
    }

    private static List<JsonNode> ParseResponses(JsonElement operationElement, JsonElement componentsSchemas, string context)
    {
        var distinctSchemas = new List<JsonNode>();
        var seenCanonical = new HashSet<string>(StringComparer.Ordinal);

        if (!operationElement.TryGetProperty("responses", out var responsesElement) ||
            responsesElement.ValueKind != JsonValueKind.Object)
        {
            throw new OpenApiDocumentException($"{context} does not declare a 'responses' object.");
        }

        foreach (var responseProperty in responsesElement.EnumerateObject())
        {
            var responseElement = responseProperty.Value;

            if (responseElement.TryGetProperty("$ref", out _))
            {
                throw new OpenApiDocumentException(
                    $"{context}: response '{responseProperty.Name}' references ('$ref') are not supported.");
            }

            if (!responseElement.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.Object ||
                !contentElement.TryGetProperty("application/json", out var jsonMediaType) ||
                !jsonMediaType.TryGetProperty("schema", out var schemaElement))
            {
                // No documented JSON body for this response (e.g. a 204 No Content, or a
                // response documented only for a non-JSON content type) - it contributes
                // nothing beyond the null option every response schema always allows.
                continue;
            }

            var resolved = OpenApiSchemaResolver.Resolve(schemaElement, componentsSchemas);
            AddDistinctResponseSchema(distinctSchemas, seenCanonical, resolved);
        }

        return distinctSchemas;
    }

    private static void AddDistinctResponseSchema(List<JsonNode> target, HashSet<string> seenCanonical, JsonNode schema)
    {
        // Flatten a response schema that is itself nothing but a top-level 'oneOf' (as
        // PaymentProcessing's AuthorizePayment 200 response is) into the aggregate union,
        // rather than nesting a redundant 'oneOf' inside the operation's overall 'oneOf'.
        if (schema is JsonObject { Count: 1 } singleKeyObject &&
            singleKeyObject.TryGetPropertyValue("oneOf", out var oneOfNode) &&
            oneOfNode is JsonArray oneOfArray)
        {
            foreach (var item in oneOfArray)
            {
                if (item is not null)
                {
                    AddDistinctResponseSchema(target, seenCanonical, item.DeepClone());
                }
            }

            return;
        }

        var canonical = schema.ToJsonString();
        if (seenCanonical.Add(canonical))
        {
            target.Add(schema);
        }
    }

    private static string RequireNonBlankStringProperty(JsonElement element, string propertyName, string context)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement) ||
            valueElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(valueElement.GetString()))
        {
            throw new OpenApiDocumentException($"{context} does not declare a nonblank '{propertyName}'.");
        }

        return valueElement.GetString()!;
    }

    internal sealed record ParsedParameter(string Name, OpenApiParameterLocation Location, bool Required, JsonNode Schema);

    internal sealed record ParsedRequestBody(bool Required, JsonNode Schema);
}