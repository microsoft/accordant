// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using NSwag;
using NSwag.CodeGeneration.CSharp;

namespace Microsoft.Accordant.Cli.Commands;

/// <summary>
/// Generates API client infrastructure from OpenAPI specs.
/// </summary>
public static class ApiGenerator
{
    /// <summary>
    /// Generates the base ApiRequest class.
    /// </summary>
    public static string GenerateApiRequestBase(string ns)
    {
        return $@"namespace {ns};

using System.Net.Http;
using System.Text;
using System.Text.Json;

/// <summary>
/// Base class for all API requests.
/// </summary>
public abstract record ApiRequest
{{
    /// <summary>HTTP method (GET, POST, PUT, DELETE, PATCH).</summary>
    public abstract string HttpMethod {{ get; }}
    
    /// <summary>URL path (without query string).</summary>
    public abstract string Path {{ get; }}
    
    /// <summary>Additional headers. Can be extended by caller.</summary>
    public Dictionary<string, string> Headers {{ get; init; }} = new();
    
    /// <summary>Query parameters. Populated by property setters.</summary>
    public Dictionary<string, string?> QueryParameters {{ get; }} = new();
    
    /// <summary>Serializes the request body. Override for custom serialization.</summary>
    public virtual HttpContent? SerializeBody() => null;
    
    /// <summary>Builds the full URL with query string.</summary>
    public string BuildUrl()
    {{
        var nonNullParams = QueryParameters.Where(kv => kv.Value != null);
        if (!nonNullParams.Any()) return Path;
        var qs = string.Join(""&"", nonNullParams.Select(kv => 
            $""{{Uri.EscapeDataString(kv.Key)}}={{Uri.EscapeDataString(kv.Value!)}}""));
        return $""{{Path}}?{{qs}}"";
    }}
    
    /// <summary>Helper to set query param from property setter.</summary>
    protected void SetQueryParam(string name, string? value) => QueryParameters[name] = value;
    protected void SetQueryParam(string name, int? value) => QueryParameters[name] = value?.ToString();
    protected void SetQueryParam(string name, bool? value) => QueryParameters[name] = value?.ToString().ToLower();
    protected void SetQueryParam(string name, Guid? value) => QueryParameters[name] = value?.ToString();
}}

/// <summary>
/// Base class for requests with a body.
/// </summary>
public abstract record ApiRequest<TBody> : ApiRequest
{{
    /// <summary>Request body.</summary>
    public TBody? Body {{ get; init; }}
    
    /// <summary>Default: JSON serialization.</summary>
    public override HttpContent? SerializeBody() => 
        Body is null ? null : new StringContent(
            JsonSerializer.Serialize(Body),
            Encoding.UTF8,
            ""application/json"");
}}
";
    }

    /// <summary>
    /// Generates the ApiResponse discriminated union.
    /// </summary>
    public static string GenerateApiResponseBase(string ns)
    {
        return $@"namespace {ns};

/// <summary>
/// Discriminated union for API responses.
/// Pattern match on Ok/Error for exhaustive handling.
/// </summary>
public abstract record ApiResponse<TSuccess>
{{
    /// <summary>HTTP status code.</summary>
    public abstract int StatusCode {{ get; }}
    
    /// <summary>Raw response body (always available for debugging).</summary>
    public string? RawBody {{ get; init; }}
    
    /// <summary>Response headers.</summary>
    public IReadOnlyDictionary<string, IEnumerable<string>>? Headers {{ get; init; }}
    
    private ApiResponse() {{ }}
    
    /// <summary>Successful response (2xx).</summary>
    public sealed record Ok(int StatusCode, TSuccess Value) : ApiResponse<TSuccess>;
    
    /// <summary>Error response (4xx/5xx).</summary>
    public sealed record Error(int StatusCode, ErrorInfo? ErrorInfo = null) : ApiResponse<TSuccess>;
    
    /// <summary>Check if response is successful.</summary>
    public bool IsSuccess => this is Ok;
    
    /// <summary>Get the success value or throw.</summary>
    public TSuccess GetValueOrThrow() => this switch
    {{
        Ok ok => ok.Value,
        Error err => throw new ApiException(err.StatusCode, err.ErrorInfo?.Message ?? ""API error"", err.RawBody),
        _ => throw new InvalidOperationException()
    }};
}}

/// <summary>
/// Standard error information (adapt to your API's error format).
/// </summary>
public record ErrorInfo(string? Code, string? Message, object? Details = null);

/// <summary>
/// Exception thrown when API call fails.
/// </summary>
public class ApiException : Exception
{{
    public int StatusCode {{ get; }}
    public string? RawBody {{ get; }}
    
    public ApiException(int statusCode, string message, string? rawBody = null) 
        : base(message)
    {{
        StatusCode = statusCode;
        RawBody = rawBody;
    }}
}}
";
    }

    /// <summary>
    /// Generates the ApiClient class.
    /// </summary>
    public static string GenerateApiClient(string ns)
    {
        return $@"namespace {ns};

using System.Net.Http;
using System.Text.Json;

/// <summary>
/// Simple API client that sends requests and interprets responses.
/// </summary>
public class ApiClient
{{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public ApiClient(HttpClient http, JsonSerializerOptions? jsonOptions = null)
    {{
        _http = http;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions 
        {{ 
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }};
    }}
    
    /// <summary>
    /// Sends a request and interprets the response.
    /// </summary>
    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        TRequest request,
        Func<HttpResponseMessage, string, TResponse> interpretResponse,
        CancellationToken cancellationToken = default)
        where TRequest : ApiRequest
    {{
        using var httpRequest = new HttpRequestMessage(
            new HttpMethod(request.HttpMethod), 
            request.BuildUrl());
        
        // Add headers
        foreach (var (key, value) in request.Headers)
            httpRequest.Headers.TryAddWithoutValidation(key, value);
        
        // Add body
        httpRequest.Content = request.SerializeBody();
        
        // Send
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        
        return interpretResponse(response, rawBody);
    }}
    
    /// <summary>Deserialize JSON to type T.</summary>
    public T? Deserialize<T>(string json)
    {{
        if (string.IsNullOrWhiteSpace(json)) return default;
        try {{ return JsonSerializer.Deserialize<T>(json, _jsonOptions); }}
        catch (JsonException) {{ return default; }}
        catch (NotSupportedException) {{ return default; }}
    }}
}}
";
    }

    /// <summary>
    /// Gets a friendly name for HTTP status codes.
    /// </summary>
    public static string GetStatusCodeName(int code) => code switch
    {
        200 => "Ok",
        201 => "Created",
        202 => "Accepted",
        204 => "NoContent",
        400 => "BadRequest",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "NotFound",
        409 => "Conflict",
        422 => "UnprocessableEntity",
        429 => "TooManyRequests",
        500 => "InternalServerError",
        502 => "BadGateway",
        503 => "ServiceUnavailable",
        _ => $"Status{code}"
    };

    /// <summary>
    /// Converts a string to PascalCase.
    /// </summary>
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        var result = new System.Text.StringBuilder();
        var capitalizeNext = true;
        
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                result.Append(capitalizeNext ? char.ToUpper(c) : c);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }
        
        return result.ToString();
    }

    /// <summary>
    /// Analyzes an operation's responses and determines response type strategy.
    /// </summary>
    public static ResponseTypeInfo AnalyzeResponses(OpenApiOperation operation, OpenApiDocument document)
    {
        var successResponses = operation.Responses
            .Where(r => r.Key.StartsWith("2"))
            .ToList();
        
        var errorResponses = operation.Responses
            .Where(r => r.Key.StartsWith("4") || r.Key.StartsWith("5"))
            .ToList();
        
        // Find primary success type (first 2xx with a schema)
        string? successTypeName = null;
        foreach (var resp in successResponses.OrderBy(r => r.Key))
        {
            var schema = resp.Value?.Schema;
            if (schema != null)
            {
                successTypeName = GetSchemaTypeName(schema, document);
                if (successTypeName != "object") break;
            }
        }
        
        // Check if all 2xx are same type
        var allSameSuccessType = successResponses
            .Where(r => r.Value?.Schema != null)
            .Select(r => GetSchemaTypeName(r.Value.Schema, document))
            .Distinct()
            .Count() <= 1;
        
        return new ResponseTypeInfo
        {
            SuccessTypeName = successTypeName ?? "object",
            AllSuccessTypesSame = allSameSuccessType,
            HasMultipleSuccessTypes = !allSameSuccessType,
            SuccessResponses = successResponses.Select(r => (int.TryParse(r.Key, out var code) ? code : 0, GetSchemaTypeName(r.Value?.Schema, document))).ToList(),
            ErrorResponses = errorResponses.Select(r => (int.TryParse(r.Key, out var code) ? code : 0, GetSchemaTypeName(r.Value?.Schema, document))).ToList()
        };
    }

    /// <summary>
    /// Gets the C# type name for a schema.
    /// </summary>
    public static string GetSchemaTypeName(NJsonSchema.JsonSchema? schema, OpenApiDocument document)
    {
        if (schema == null) return "object";
        
        // Handle references
        if (schema.HasReference && schema.Reference != null)
        {
            // Try to find the definition name
            var refSchema = schema.Reference;
            if (!string.IsNullOrEmpty(refSchema.Title))
                return refSchema.Title;
            
            // Search in definitions
            foreach (var def in document.Definitions ?? new Dictionary<string, NJsonSchema.JsonSchema>())
            {
                if (ReferenceEquals(def.Value, refSchema) || def.Value == refSchema)
                    return def.Key;
            }
        }
        
        // Handle primitives
        return schema.Type switch
        {
            NJsonSchema.JsonObjectType.String => "string",
            NJsonSchema.JsonObjectType.Integer => schema.Format == "int64" ? "long" : "int",
            NJsonSchema.JsonObjectType.Number => "double",
            NJsonSchema.JsonObjectType.Boolean => "bool",
            NJsonSchema.JsonObjectType.Array when schema.Item != null => $"ICollection<{GetSchemaTypeName(schema.Item, document)}>",
            _ => "object"
        };
    }

    /// <summary>
    /// Gets C# type for an OpenAPI parameter.
    /// </summary>
    public static (string type, string name, string serializedName) GetParameterInfo(OpenApiParameter param)
    {
        var actualParam = param.ActualParameter ?? param;
        var serializedName = actualParam.Name ?? param.Name ?? "unknown";
        var name = ToPascalCase(serializedName);
        if (string.IsNullOrEmpty(name)) name = "Param";
        
        var schema = actualParam.Schema ?? actualParam.ActualSchema;
        string type = "string";
        
        if (schema != null)
        {
            type = schema.Type switch
            {
                NJsonSchema.JsonObjectType.String when schema.Format == "uuid" => "Guid",
                NJsonSchema.JsonObjectType.String => "string",
                NJsonSchema.JsonObjectType.Integer when schema.Format == "int64" => "long",
                NJsonSchema.JsonObjectType.Integer => "int",
                NJsonSchema.JsonObjectType.Number => "double",
                NJsonSchema.JsonObjectType.Boolean => "bool",
                _ => "string"
            };
        }
        
        if (!actualParam.IsRequired)
            type += "?";
        
        return (type, name, serializedName);
    }
}

/// <summary>
/// Information about an operation's response types.
/// </summary>
public class ResponseTypeInfo
{
    public string SuccessTypeName { get; set; } = "object";
    public bool AllSuccessTypesSame { get; set; } = true;
    public bool HasMultipleSuccessTypes { get; set; } = false;
    public List<(int StatusCode, string TypeName)> SuccessResponses { get; set; } = new();
    public List<(int StatusCode, string TypeName)> ErrorResponses { get; set; } = new();
}
