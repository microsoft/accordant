// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Adapters.OpenApi;

using System.Text.Json.Nodes;

/// <summary>
/// Builds the composite JSON Schemas <see cref="OpenApiDocumentParser"/> advertises for
/// each operation:
///
/// <list type="bullet">
/// <item>
/// a request schema shaped <c>{ path, query, headers, body }</c>, including only the
/// sections applicable to the operation (an operation with no query parameters, for
/// example, has no <c>query</c> property at all) and marking each section - and each
/// parameter/body within it - required exactly when OpenAPI requires it;
/// </item>
/// <item>
/// a response schema shaped <c>{ status, body }</c>, where <c>body</c> is a <c>oneOf</c>
/// union of every distinct documented JSON response schema plus an always-present
/// <c>null</c> option, since execution may return a response with no body (e.g. a 204) or
/// an undocumented status regardless of what the document happens to describe.
/// </item>
/// </list>
/// </summary>
internal static class OpenApiSchemaBuilder
{
    public static JsonNode BuildRequestSchema(
        IReadOnlyList<OpenApiDocumentParser.ParsedParameter> pathParameters,
        IReadOnlyList<OpenApiDocumentParser.ParsedParameter> queryParameters,
        IReadOnlyList<OpenApiDocumentParser.ParsedParameter> headerParameters,
        OpenApiDocumentParser.ParsedRequestBody? requestBody)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        if (pathParameters.Count > 0)
        {
            properties["path"] = BuildParameterSectionSchema(pathParameters);
            // Every OpenAPI path parameter is required, so a 'path' section is always
            // required whenever the operation has any path parameters at all.
            required.Add("path");
        }

        if (queryParameters.Count > 0)
        {
            properties["query"] = BuildParameterSectionSchema(queryParameters);
            if (queryParameters.Any(p => p.Required))
            {
                required.Add("query");
            }
        }

        if (headerParameters.Count > 0)
        {
            properties["headers"] = BuildParameterSectionSchema(headerParameters);
            if (headerParameters.Any(p => p.Required))
            {
                required.Add("headers");
            }
        }

        if (requestBody is not null)
        {
            properties["body"] = requestBody.Schema.DeepClone();
            if (requestBody.Required)
            {
                required.Add("body");
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    public static JsonNode BuildResponseSchema(IReadOnlyList<JsonNode> distinctBodySchemas)
    {
        var nullSchema = new JsonObject { ["type"] = "null" };
        JsonNode bodySchema;

        if (distinctBodySchemas.Count == 0)
        {
            bodySchema = nullSchema;
        }
        else
        {
            var oneOf = new JsonArray();
            foreach (var schema in distinctBodySchemas)
            {
                oneOf.Add(schema.DeepClone());
            }

            oneOf.Add(nullSchema);
            bodySchema = new JsonObject { ["oneOf"] = oneOf };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("status", "body"),
            ["properties"] = new JsonObject
            {
                ["status"] = new JsonObject { ["type"] = "integer" },
                ["body"] = bodySchema,
            },
        };
    }

    private static JsonNode BuildParameterSectionSchema(IReadOnlyList<OpenApiDocumentParser.ParsedParameter> parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var parameter in parameters)
        {
            properties[parameter.Name] = parameter.Schema.DeepClone();
            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }
}