// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Adapters.OpenApi;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The live <see cref="ITargetSession"/> <see cref="OpenApiTargetAdapter"/> connects: maps
/// a concrete <c>{ path, query, headers, body }</c> request into an actual HTTP call
/// against the adapter's configured <c>baseUrl</c>, and maps the actual HTTP response back
/// into a concrete <c>{ status, body }</c> response.
///
/// Validation is tight and happens entirely before anything is sent: an unexpected request
/// section, a missing required section/parameter, or a parameter value of an unsupported
/// JSON kind throws before any network call is made. Once a request is actually sent,
/// network/client failures and non-JSON response bodies are left as thrown exceptions -
/// this session never turns either into a fabricated response - while an ordinary HTTP
/// failure status (4xx/5xx) with a well-formed JSON or empty body is just another
/// <c>{ status, body }</c> response like any other.
/// </summary>
internal sealed class OpenApiTargetSession : TargetSessionBase
{
    private static readonly JsonSerializerOptions ResponseSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly IReadOnlyDictionary<string, OpenApiOperationSpec> _specsByName;

    public OpenApiTargetSession(HttpClient httpClient, string baseUrl, IReadOnlyList<OpenApiOperation> operations)
        : base(operations.Select(o => o.Definition).ToList())
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _specsByName = operations.ToDictionary(o => o.Definition.Name, o => o.ExecutionSpec, StringComparer.Ordinal);
    }

    protected override async Task<JsonElement> ExecuteOperationAsync(
        OperationDefinition operation, JsonElement request, CancellationToken cancellationToken)
    {
        var spec = _specsByName[operation.Name];

        if (request.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"Operation '{operation.Name}' request must be a JSON object with 'path'/'query'/'headers'/'body' " +
                $"sections as applicable to this operation.",
                nameof(request));
        }

        ValidateNoUnexpectedSections(operation.Name, spec, request);

        var pathAndQuery = BuildPathAndQuery(operation.Name, spec, request);

        using var httpRequest = new HttpRequestMessage(new HttpMethod(spec.HttpMethod), _baseUrl + pathAndQuery);

        ApplyHeaders(operation.Name, spec, request, httpRequest);
        ApplyBody(operation.Name, spec, request, httpRequest);

        using var httpResponse = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        return await BuildResponseAsync(operation.Name, httpResponse, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateNoUnexpectedSections(string operationName, OpenApiOperationSpec spec, JsonElement request)
    {
        var applicable = new HashSet<string>(StringComparer.Ordinal);
        if (spec.PathParameters.Count > 0)
        {
            applicable.Add("path");
        }

        if (spec.QueryParameters.Count > 0)
        {
            applicable.Add("query");
        }

        if (spec.HeaderParameters.Count > 0)
        {
            applicable.Add("headers");
        }

        if (spec.RequestBody is not null)
        {
            applicable.Add("body");
        }

        foreach (var property in request.EnumerateObject())
        {
            if (!applicable.Contains(property.Name))
            {
                var expected = applicable.Count == 0 ? "none" : string.Join(", ", applicable);
                throw new ArgumentException(
                    $"Operation '{operationName}' does not accept a request '{property.Name}' section " +
                    $"(expected sections: {expected}).",
                    nameof(request));
            }
        }
    }

    /// <summary>
    /// Extracts and validates one of the request's <c>path</c>/<c>query</c>/<c>headers</c>
    /// sections against <paramref name="parameters"/>: the section itself must be present
    /// exactly when <paramref name="sectionRequired"/> (true for <c>path</c> whenever the
    /// operation has any path parameter, since OpenAPI path parameters are always
    /// required; true for <c>query</c>/<c>headers</c> only when at least one of their
    /// parameters is required), must be a JSON object when present, must not declare any
    /// parameter this operation does not know about, and must include every parameter of
    /// its own that is required.
    /// </summary>
    private static JsonElement? ExtractSection(
        string operationName,
        JsonElement request,
        string sectionName,
        bool sectionRequired,
        IReadOnlyList<OpenApiParameterSpec> parameters)
    {
        if (!request.TryGetProperty(sectionName, out var section))
        {
            if (sectionRequired)
            {
                throw new ArgumentException(
                    $"Operation '{operationName}' request is missing its required '{sectionName}' section.");
            }

            return null;
        }

        if (section.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"Operation '{operationName}' request '{sectionName}' section must be a JSON object.");
        }

        var declaredNames = new HashSet<string>(parameters.Select(p => p.Name), StringComparer.Ordinal);
        foreach (var property in section.EnumerateObject())
        {
            if (!declaredNames.Contains(property.Name))
            {
                throw new ArgumentException(
                    $"Operation '{operationName}' request '{sectionName}' section declares unexpected " +
                    $"parameter '{property.Name}'.");
            }
        }

        foreach (var parameter in parameters)
        {
            if (parameter.Required && !section.TryGetProperty(parameter.Name, out _))
            {
                throw new ArgumentException(
                    $"Operation '{operationName}' request '{sectionName}' section is missing required " +
                    $"parameter '{parameter.Name}'.");
            }
        }

        return section;
    }

    private static string BuildPathAndQuery(string operationName, OpenApiOperationSpec spec, JsonElement request)
    {
        var path = spec.PathTemplate;

        if (spec.PathParameters.Count > 0)
        {
            var pathSection = ExtractSection(operationName, request, "path", sectionRequired: true, spec.PathParameters)!.Value;

            foreach (var parameter in spec.PathParameters)
            {
                var valueElement = pathSection.GetProperty(parameter.Name);
                var stringValue = ConvertParameterValue(operationName, "path", parameter.Name, valueElement);
                path = path.Replace($"{{{parameter.Name}}}", Uri.EscapeDataString(stringValue), StringComparison.Ordinal);
            }
        }

        if (spec.QueryParameters.Count == 0)
        {
            return path;
        }

        var querySection = ExtractSection(
            operationName, request, "query", sectionRequired: spec.QueryParameters.Any(p => p.Required), spec.QueryParameters);

        if (querySection is null)
        {
            return path;
        }

        var pairs = new List<string>();
        foreach (var parameter in spec.QueryParameters)
        {
            if (!querySection.Value.TryGetProperty(parameter.Name, out var valueElement))
            {
                continue;
            }

            var stringValue = ConvertParameterValue(operationName, "query", parameter.Name, valueElement);
            pairs.Add($"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(stringValue)}");
        }

        return pairs.Count == 0 ? path : $"{path}?{string.Join("&", pairs)}";
    }

    private static void ApplyHeaders(string operationName, OpenApiOperationSpec spec, JsonElement request, HttpRequestMessage httpRequest)
    {
        if (spec.HeaderParameters.Count == 0)
        {
            return;
        }

        var headerSection = ExtractSection(
            operationName, request, "headers", sectionRequired: spec.HeaderParameters.Any(p => p.Required), spec.HeaderParameters);

        if (headerSection is null)
        {
            return;
        }

        foreach (var parameter in spec.HeaderParameters)
        {
            if (!headerSection.Value.TryGetProperty(parameter.Name, out var valueElement))
            {
                continue;
            }

            var stringValue = ConvertParameterValue(operationName, "header", parameter.Name, valueElement);
            if (!httpRequest.Headers.TryAddWithoutValidation(parameter.Name, stringValue))
            {
                throw new ArgumentException(
                    $"Operation '{operationName}' header parameter '{parameter.Name}' has an invalid value.");
            }
        }
    }

    private static void ApplyBody(string operationName, OpenApiOperationSpec spec, JsonElement request, HttpRequestMessage httpRequest)
    {
        if (spec.RequestBody is null)
        {
            return;
        }

        if (!request.TryGetProperty("body", out var bodyElement))
        {
            if (spec.RequestBody.Required)
            {
                throw new ArgumentException($"Operation '{operationName}' request is missing its required 'body'.");
            }

            return;
        }

        httpRequest.Content = new StringContent(bodyElement.GetRawText(), Encoding.UTF8, "application/json");
    }

    private static string ConvertParameterValue(string operationName, string location, string parameterName, JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw new ArgumentException(
                $"Operation '{operationName}' {location} parameter '{parameterName}' must be a string, number, " +
                $"or boolean value; got '{value.ValueKind}'."),
        };

    private static async Task<JsonElement> BuildResponseAsync(
        string operationName, HttpResponseMessage httpResponse, CancellationToken cancellationToken)
    {
        var status = (int)httpResponse.StatusCode;
        var contentBytes = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        JsonElement body;
        if (contentBytes.Length == 0)
        {
            body = JsonDocument.Parse("null").RootElement;
        }
        else
        {
            try
            {
                using var parsedContent = JsonDocument.Parse(contentBytes);
                body = parsedContent.RootElement.Clone();
            }
            catch (JsonException jsonException)
            {
                throw new OpenApiExecutionException(
                    $"Operation '{operationName}' received a response with status {status} whose body is not " +
                    $"valid JSON.",
                    jsonException);
            }
        }

        return JsonSerializer.SerializeToElement(new OpenApiCallResponse(status, body), ResponseSerializerOptions);
    }

    public override ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private readonly record struct OpenApiCallResponse(int Status, JsonElement Body);
}