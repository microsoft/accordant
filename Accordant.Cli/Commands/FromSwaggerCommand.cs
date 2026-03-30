// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.Text.Json;
using NSwag;
using NSwag.CodeGeneration.CSharp;

namespace Microsoft.Accordant.Cli;

public static class FromSwaggerCommand
{
    public static Command Create()
    {
        var specArgument = new Argument<string>(
            "spec",
            "Path to OpenAPI/Swagger spec file (YAML or JSON), or directory containing spec files");

        var nameArgument = new Argument<string>(
            "name",
            "Name of the project to create");

        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            getDefaultValue: () => ".",
            description: "Output directory");

        var endpointOption = new Option<string?>(
            aliases: new[] { "--endpoint", "-e" },
            description: "Base URL endpoint for the API (e.g., https://api.example.com)");

        var command = new Command("from-swagger", "Create a Accordant project from an OpenAPI/Swagger spec")
        {
            specArgument,
            nameArgument,
            outputOption,
            endpointOption
        };

        command.SetHandler(HandleFromSwagger, specArgument, nameArgument, outputOption, endpointOption);

        return command;
    }

    private static async Task HandleFromSwagger(string specPath, string name, string output, string? endpoint)
    {
        var targetDir = Path.Combine(output, name);

        if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir).Length > 0)
        {
            Console.WriteLine($"Error: Directory '{targetDir}' already exists and is not empty.");
            return;
        }

        // Resolve spec path - support directory or file
        var fullSpecPath = Path.GetFullPath(specPath);
        string actualSpecFile;
        
        if (Directory.Exists(fullSpecPath))
        {
            // Find JSON spec file in directory (skip temp files starting with . or #)
            var jsonFiles = Directory.GetFiles(fullSpecPath, "*.json")
                .Where(f => !Path.GetFileName(f).StartsWith(".") && !Path.GetFileName(f).StartsWith("#"))
                .ToArray();
            if (jsonFiles.Length == 0)
            {
                Console.WriteLine($"Error: No JSON spec files found in: {fullSpecPath}");
                return;
            }
            actualSpecFile = jsonFiles[0];
            Console.WriteLine($"Found spec file: {Path.GetFileName(actualSpecFile)}");
        }
        else if (File.Exists(fullSpecPath))
        {
            actualSpecFile = fullSpecPath;
        }
        else
        {
            Console.WriteLine($"Error: Spec file or directory not found: {fullSpecPath}");
            return;
        }

        Console.WriteLine($"Loading OpenAPI spec from: {actualSpecFile}");

        try
        {
            // Load the OpenAPI document
            var document = await OpenApiDocument.FromFileAsync(actualSpecFile);
            
            Console.WriteLine($"  Title: {document.Info?.Title ?? "Unknown"}");
            Console.WriteLine($"  Version: {document.Info?.Version ?? "Unknown"}");
            Console.WriteLine($"  Operations: {document.Operations.Count()}");

            // Generate DTOs only (no clients) using NSwag
            var clientSettings = new CSharpClientGeneratorSettings
            {
                ClassName = "{controller}Client",
                CSharpGeneratorSettings =
                {
                    Namespace = name,
                    GenerateNullableReferenceTypes = true,
                },
                GenerateClientInterfaces = false,
                GenerateClientClasses = false,  // Don't generate NSwag clients
                InjectHttpClient = false,
                DisposeHttpClient = false,
                UseBaseUrl = false,
            };

            var clientGenerator = new CSharpClientGenerator(document, clientSettings);
            var dtoCode = clientGenerator.GenerateFile();

            // Create output directory and Contracts subfolder
            Directory.CreateDirectory(targetDir);
            var contractsDir = Path.Combine(targetDir, "Contracts");
            Directory.CreateDirectory(contractsDir);

            // Write generated DTOs to Contracts/Definitions.cs
            var dtosPath = Path.Combine(contractsDir, "Definitions.cs");
            await File.WriteAllTextAsync(dtosPath, dtoCode);
            Console.WriteLine($"  Created: Contracts/Definitions.cs");

            // Generate project file
            var csprojContent = GenerateCsproj(name);
            var csprojPath = Path.Combine(targetDir, $"{name}.csproj");
            await File.WriteAllTextAsync(csprojPath, csprojContent);
            Console.WriteLine($"  Created: {name}.csproj");

            // Generate NuGet.config
            var nugetContent = GenerateNuGetConfig();
            var nugetPath = Path.Combine(targetDir, "NuGet.config");
            await File.WriteAllTextAsync(nugetPath, nugetContent);
            Console.WriteLine($"  Created: NuGet.config");

            // Generate ApiInfrastructure (ApiRequest, ApiResponse, ApiClient) in Contracts/
            var infraContent = GenerateApiInfrastructure(name);
            var infraPath = Path.Combine(contractsDir, "ApiInfrastructure.cs");
            await File.WriteAllTextAsync(infraPath, infraContent);
            Console.WriteLine($"  Created: Contracts/ApiInfrastructure.cs");

            // Generate Request/Response contracts in Contracts/
            var requestsContent = GenerateRequestContracts(name, document);
            var requestsPath = Path.Combine(contractsDir, "Requests.cs");
            await File.WriteAllTextAsync(requestsPath, requestsContent);
            Console.WriteLine($"  Created: Contracts/Requests.cs");

            // Generate State
            var stateContent = GenerateState(name, document);
            var statePath = Path.Combine(targetDir, $"{name}State.cs");
            await File.WriteAllTextAsync(statePath, stateContent);
            Console.WriteLine($"  Created: {name}State.cs");

            // Generate Operation classes (proper Accordant Operations)
            var operationsContent = GenerateOperations(name, document);
            var operationsPath = Path.Combine(targetDir, $"Operations.cs");
            await File.WriteAllTextAsync(operationsPath, operationsContent);
            Console.WriteLine($"  Created: Operations.cs");

            // Generate Spec class
            var specContent = GenerateSpec(name, document);
            var specFilePath = Path.Combine(targetDir, $"{name}Spec.cs");
            await File.WriteAllTextAsync(specFilePath, specContent);
            Console.WriteLine($"  Created: {name}Spec.cs");

            // Generate Tests file
            var testsContent = GenerateTests(name, document, endpoint);
            var testsPath = Path.Combine(targetDir, $"{name}Tests.cs");
            await File.WriteAllTextAsync(testsPath, testsContent);
            Console.WriteLine($"  Created: {name}Tests.cs");

            // Generate README
            var readmeContent = GenerateReadme(name, document);
            var readmePath = Path.Combine(targetDir, "README.md");
            await File.WriteAllTextAsync(readmePath, readmeContent);
            Console.WriteLine($"  Created: README.md");

            Console.WriteLine();
            Console.WriteLine($"Project '{name}' created successfully from OpenAPI spec!");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine($"  cd {name}");
            Console.WriteLine("  dotnet restore");
            Console.WriteLine("  # Configure endpoint and auth in Tests file");
            Console.WriteLine("  # Define your state model");
            Console.WriteLine("  # Implement spec operations");
            Console.WriteLine("  dotnet test");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing spec: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return;
        }
    }

    private static string GenerateCsproj(string name)
    {
        return $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.Accordant"" Version=""*"" />
    <PackageReference Include=""Microsoft.Accordant.Testing"" Version=""*"" />
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.*"" />
    <PackageReference Include=""NUnit"" Version=""4.*"" />
    <PackageReference Include=""NUnit3TestAdapter"" Version=""4.*"" />
    <PackageReference Include=""System.Text.Json"" Version=""8.*"" />
  </ItemGroup>

</Project>
";
    }

    private static string GenerateNuGetConfig()
    {
        return @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""local"" value=""c:\nugetlocalfeed"" />
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" />
  </packageSources>
</configuration>
";
    }

    private static string GenerateApiInfrastructure(string name)
    {
        return $@"namespace {name}.Contracts;

using System.Net.Http;
using System.Text;
using System.Text.Json;

// =============================================================================
// API Infrastructure - Base classes for typed HTTP requests and responses
// =============================================================================

/// <summary>
/// Base class for all API responses. Includes StatusCode and Headers.
/// </summary>
public abstract record ApiResponse
{{
    public int StatusCode {{ get; init; }}
    public Dictionary<string, IEnumerable<string>> Headers {{ get; init; }} = new();
}}

/// <summary>
/// Base class for all API requests. Each operation generates a request class.
/// </summary>
public abstract record ApiRequest<TResponse> where TResponse : ApiResponse
{{
    public abstract string HttpMethod {{ get; }}
    public abstract string Path {{ get; }}
    public Dictionary<string, string> RequestHeaders {{ get; init; }} = new();
    
    /// <summary>Serialize body to JSON string, or null for no body</summary>
    public virtual string? SerializeBody() => null;

    /// <summary>Interpret HTTP response into typed response</summary>
    public abstract TResponse InterpretResponse(HttpResponseMessage httpResponse, string rawBody);
}}

/// <summary>
/// Simple API client that sends requests and returns typed responses.
/// </summary>
public class ApiClient
{{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiClient(HttpClient httpClient)
    {{
        _httpClient = httpClient;
        _jsonOptions = new JsonSerializerOptions
        {{
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }};
    }}

    public async Task<TResponse> SendAsync<TResponse>(ApiRequest<TResponse> request, CancellationToken ct = default)
        where TResponse : ApiResponse
    {{
        // Build the full URL with query parameters
        var url = request.Path;
        
        // Create HTTP request
        var httpRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), url);
        
        // Add headers
        foreach (var header in request.RequestHeaders)
        {{
            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }}

        // Add body if present
        var body = request.SerializeBody();
        if (body != null)
        {{
            httpRequest.Content = new StringContent(body, Encoding.UTF8, ""application/json"");
        }}

        // Send request
        var response = await _httpClient.SendAsync(httpRequest, ct);
        
        // Read body
        var rawBody = await response.Content.ReadAsStringAsync(ct);

        // Let the request interpret the response
        return request.InterpretResponse(response, rawBody);
    }}
}}
";
    }

    private static string GenerateState(string name, OpenApiDocument document)
    {
        return $@"namespace {name};

using Microsoft.Accordant;

/// <summary>
/// State tracks what the system remembers between operations.
/// </summary>
public class {name}State : JsonState
{{
}}
";
    }

    private static string GenerateRequestContracts(string name, OpenApiDocument document)
    {
        var requestClasses = new List<string>();
        var statusCodeNames = new Dictionary<int, string>
        {
            { 200, "Ok" }, { 201, "Created" }, { 202, "Accepted" }, { 204, "NoContent" },
            { 400, "BadRequest" }, { 401, "Unauthorized" }, { 403, "Forbidden" },
            { 404, "NotFound" }, { 409, "Conflict" }, { 500, "InternalServerError" }
        };

        // Build a lookup from schema object to definition name
        var schemaNames = new Dictionary<NJsonSchema.JsonSchema, string>();
        foreach (var def in document.Definitions)
        {
            schemaNames[def.Value] = def.Key;
        }

        foreach (var op in document.Operations)
        {
            var opId = op.Operation.OperationId;
            if (string.IsNullOrEmpty(opId)) continue;

            var opName = ToPascalCase(opId);
            var method = op.Method.ToString().ToUpper();
            var pathTemplate = op.Path;
            var summary = op.Operation.Summary?.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") ?? $"{method} {pathTemplate}";

            // Collect all responses (success, error, default)
            var allResponses = op.Operation.Responses
                .OrderBy(r => r.Key)
                .ToList();

            // Build response type with properties for each response schema
            var responseTypeName = $"{opName}Response";
            var responseTypeCode = GenerateResponseType(opName, allResponses, statusCodeNames, schemaNames);

            // Collect parameters
            var properties = new List<string>();
            var pathParams = new List<(string name, string propName)>();
            var queryParams = new List<(string name, string propName)>();

            foreach (var param in op.Operation.Parameters)
            {
                var actualParam = param.ActualParameter ?? param;
                var rawName = actualParam.Name ?? param.Name ?? "Unknown";
                var propName = ToPascalCase(rawName);
                if (string.IsNullOrEmpty(propName)) propName = "Param" + properties.Count;
                
                var paramType = GetParameterType(actualParam);
                var required = actualParam.IsRequired;
                var location = actualParam.Kind.ToString().ToLower();

                properties.Add($"    /// <summary>{location} parameter: {rawName}</summary>");
                if (required && !paramType.EndsWith("?"))
                {
                    properties.Add($"    public required {paramType} {propName} {{ get; init; }}");
                }
                else
                {
                    properties.Add($"    public {paramType} {propName} {{ get; init; }}");
                }

                if (actualParam.Kind == NSwag.OpenApiParameterKind.Path)
                {
                    pathParams.Add((rawName, propName));
                }
                else if (actualParam.Kind == NSwag.OpenApiParameterKind.Query)
                {
                    queryParams.Add((rawName, propName));
                }
            }

            // Body parameter
            string? bodyTypeName = null;
            if (op.Operation.RequestBody != null)
            {
                var bodyContent = op.Operation.RequestBody.Content.Values.FirstOrDefault();
                if (bodyContent?.Schema != null)
                {
                    bodyTypeName = GetTypeName(bodyContent.Schema, schemaNames);
                    properties.Add($"    /// <summary>Request body</summary>");
                    properties.Add($"    public {bodyTypeName}? Body {{ get; init; }}");
                }
            }

            var propertiesCode = properties.Count > 0 
                ? string.Join("\n", properties)
                : "    // No parameters";

            // Build Path property with interpolation
            var pathCode = BuildPathExpression(pathTemplate, pathParams, queryParams);

            // Build InterpretResponse method
            var interpretCode = BuildInterpretResponse(opName, allResponses, statusCodeNames, schemaNames);

            // Build SerializeBody override if needed
            var serializeBodyCode = bodyTypeName != null
                ? $@"
    public override string? SerializeBody() =>
        Body != null ? JsonSerializer.Serialize(Body) : null;"
                : "";

            requestClasses.Add($@"
// =============================================================================
// {opName}: {method} {pathTemplate}
// {summary}
// =============================================================================

{responseTypeCode}

/// <summary>
/// {summary}
/// </summary>
public record {opName}Request : ApiRequest<{responseTypeName}>
{{
    public override string HttpMethod => ""{method}"";
    
{propertiesCode}

{pathCode}
{serializeBodyCode}

{interpretCode}
}}");
        }

        return $@"namespace {name}.Contracts;

using System.Net.Http;
using System.Text.Json;

// =============================================================================
// Generated Request/Response Contracts for {document.Info?.Title ?? name}
// Each request specifies HTTP method, path, parameters, and response parsing.
// =============================================================================
{string.Join("\n", requestClasses)}
";
    }

    private static string GenerateOperations(string name, OpenApiDocument document)
    {
        var operations = new List<string>();

        foreach (var op in document.Operations)
        {
            var opId = op.Operation.OperationId;
            if (string.IsNullOrEmpty(opId)) continue;

            var opName = ToPascalCase(opId);
            var method = op.Method.ToString().ToUpper();
            var pathTemplate = op.Path;
            var summary = op.Operation.Summary?.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") ?? $"{method} {pathTemplate}";

            var responseTypeName = $"{opName}Response";
            var requestTypeName = $"{opName}Request";

            operations.Add($@"
/// <summary>
/// {summary}
/// </summary>
public class {opName}Operation : Operation<{requestTypeName}, {responseTypeName}, {name}State>
{{
    public {opName}Operation() : base(""{opName}"") {{ }}

    public override ExpectedOutcomes Apply({requestTypeName} request, {name}State state)
    {{
        throw new NotImplementedException();
    }}

    public override async Task<{responseTypeName}> ExecuteAsync(TestingContext context, {requestTypeName} request)
    {{
        var client = context.Get<ApiClient>();
        return await client.SendAsync(request);
    }}
}}");
        }

        return $@"namespace {name};

using {name}.Contracts;

// =============================================================================
// Generated Operations for {document.Info?.Title ?? name}
// Each operation wraps an API endpoint with Execute and Apply methods.
// =============================================================================
{string.Join("\n", operations)}
";
    }

    private static string GenerateResponseType(
        string opName,
        List<KeyValuePair<string, NSwag.OpenApiResponse>> allResponses,
        Dictionary<int, string> statusCodeNames,
        Dictionary<NJsonSchema.JsonSchema, string> schemaNames)
    {
        var properties = new List<string>();
        
        // Collect unique response types (named by their schema type)
        // This handles both success and error responses uniformly
        var seenTypes = new HashSet<string>();

        foreach (var resp in allResponses)
        {
            if (resp.Value?.Schema == null) continue;
            
            var typeName = GetTypeName(resp.Value.Schema, schemaNames);
            if (typeName == "object" || seenTypes.Contains(typeName)) continue;
            seenTypes.Add(typeName);
            
            properties.Add($"    public {typeName}? {typeName} {{ get; init; }}");
        }

        // Collect response headers from all responses
        var headerProps = new List<string>();
        var seenHeaders = new HashSet<string>();
        
        foreach (var resp in allResponses)
        {
            if (resp.Value?.Headers == null) continue;
            
            foreach (var header in resp.Value.Headers)
            {
                var headerName = header.Key;
                // Use x-ms-client-name if available, otherwise PascalCase the header name
                var propName = header.Value.ExtensionData?.TryGetValue("x-ms-client-name", out var clientName) == true
                    ? clientName?.ToString() ?? ToPascalCase(headerName.Replace("-", ""))
                    : ToPascalCase(headerName.Replace("-", ""));
                
                if (seenHeaders.Contains(propName)) continue;
                seenHeaders.Add(propName);
                
                var headerType = GetHeaderType(header.Value);
                var desc = header.Value.Description?.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") ?? "";
                
                if (!string.IsNullOrEmpty(desc))
                    headerProps.Add($"    /// <summary>{desc}</summary>");
                headerProps.Add($"    public {headerType}? {propName} {{ get; init; }}");
            }
        }

        var allProps = new List<string>();
        allProps.AddRange(properties);
        allProps.AddRange(headerProps);
        
        var propsCode = allProps.Count > 0 
            ? "\n" + string.Join("\n", allProps) 
            : "";

        return $@"public record {opName}Response : ApiResponse
{{{propsCode}
}}";
    }

    private static string GetHeaderType(NSwag.OpenApiHeader header)
    {
        var schema = header.Schema ?? header.ActualSchema;
        if (schema == null) return "string";

        return schema.Type switch
        {
            NJsonSchema.JsonObjectType.Integer when schema.Format == "int64" => "long",
            NJsonSchema.JsonObjectType.Integer => "int",
            NJsonSchema.JsonObjectType.Number => "double",
            NJsonSchema.JsonObjectType.Boolean => "bool",
            _ => "string"
        };
    }

    private static string BuildPathExpression(
        string pathTemplate,
        List<(string name, string propName)> pathParams,
        List<(string name, string propName)> queryParams)
    {
        // Replace {param} with string interpolation
        var pathExpr = pathTemplate;
        foreach (var (name, propName) in pathParams)
        {
            pathExpr = pathExpr.Replace($"{{{name}}}", $"{{{propName}}}");
        }

        if (queryParams.Count == 0)
        {
            return $@"    public override string Path => $""{pathExpr}"";";
        }

        // Build query string builder
        var queryCode = new List<string>();
        queryCode.Add("    public override string Path");
        queryCode.Add("    {");
        queryCode.Add("        get");
        queryCode.Add("        {");
        queryCode.Add($@"            var path = $""{pathExpr}"";");
        queryCode.Add("            var queryParts = new List<string>();");
        
        foreach (var (name, propName) in queryParams)
        {
            queryCode.Add($@"            if ({propName} != null) queryParts.Add($""{name}={{{propName}}}"");");
        }
        
        queryCode.Add(@"            if (queryParts.Count > 0) path += ""?"" + string.Join(""&"", queryParts);");
        queryCode.Add("            return path;");
        queryCode.Add("        }");
        queryCode.Add("    }");

        return string.Join("\n", queryCode);
    }

    private static string BuildInterpretResponse(
        string opName,
        List<KeyValuePair<string, NSwag.OpenApiResponse>> allResponses,
        Dictionary<int, string> statusCodeNames,
        Dictionary<NJsonSchema.JsonSchema, string> schemaNames)
    {
        // Find the primary success type for deserializing 2xx responses
        var successType = allResponses
            .Where(r => r.Key.StartsWith("2") && r.Value?.Schema != null)
            .Select(r => GetTypeName(r.Value.Schema, schemaNames))
            .FirstOrDefault(t => t != "object");

        // Find the error type from default or 4xx/5xx responses
        var errorType = allResponses
            .Where(r => r.Key == "default" || r.Key.StartsWith("4") || r.Key.StartsWith("5"))
            .Where(r => r.Value?.Schema != null)
            .Select(r => GetTypeName(r.Value.Schema, schemaNames))
            .FirstOrDefault(t => t != "object");

        // Collect response headers to populate as typed properties
        var headerAssignments = new List<string>();
        var seenHeaders = new HashSet<string>();
        
        foreach (var resp in allResponses)
        {
            if (resp.Value?.Headers == null) continue;
            
            foreach (var header in resp.Value.Headers)
            {
                var headerName = header.Key;
                var propName = header.Value.ExtensionData?.TryGetValue("x-ms-client-name", out var clientName) == true
                    ? clientName?.ToString() ?? ToPascalCase(headerName.Replace("-", ""))
                    : ToPascalCase(headerName.Replace("-", ""));
                
                if (seenHeaders.Contains(propName)) continue;
                seenHeaders.Add(propName);
                
                var headerType = GetHeaderType(header.Value);
                var parseExpr = headerType switch
                {
                    "int" => $"int.TryParse(GetHeader(httpResponse, \"{headerName}\"), out var _{propName}) ? _{propName} : null",
                    "long" => $"long.TryParse(GetHeader(httpResponse, \"{headerName}\"), out var _{propName}) ? _{propName} : null",
                    "double" => $"double.TryParse(GetHeader(httpResponse, \"{headerName}\"), out var _{propName}) ? _{propName} : null",
                    "bool" => $"bool.TryParse(GetHeader(httpResponse, \"{headerName}\"), out var _{propName}) ? _{propName} : null",
                    _ => $"GetHeader(httpResponse, \"{headerName}\")"
                };
                
                headerAssignments.Add($"                {propName} = {parseExpr},");
            }
        }
        
        var headerAssignmentsCode = headerAssignments.Count > 0 
            ? "\n" + string.Join("\n", headerAssignments) 
            : "";

        string successCase;
        if (successType != null)
        {
            successCase = $@"            >= 200 and < 300 => new {opName}Response
            {{
                StatusCode = (int)httpResponse.StatusCode,
                Headers = headers,{headerAssignmentsCode}
                {successType} = string.IsNullOrEmpty(rawBody) ? null : JsonSerializer.Deserialize<{successType}>(rawBody, _jsonOptions)
            }},";
        }
        else
        {
            var headersLine = headerAssignments.Count > 0
                ? $"Headers = headers,{headerAssignmentsCode}"
                : "Headers = headers";
            successCase = $@"            >= 200 and < 300 => new {opName}Response
            {{
                StatusCode = (int)httpResponse.StatusCode,
                {headersLine}
            }},";
        }

        var getHeaderHelper = headerAssignments.Count > 0
            ? @"

    private static string? GetHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;"
            : "";

        // Generate error case - use detected error type or just return status without body parsing
        string errorCase;
        if (errorType != null)
        {
            errorCase = $@"            _ => new {opName}Response
            {{
                StatusCode = (int)httpResponse.StatusCode,
                Headers = headers,
                {errorType} = TryParse<{errorType}>(rawBody)
            }}";
        }
        else
        {
            errorCase = $@"            _ => new {opName}Response
            {{
                StatusCode = (int)httpResponse.StatusCode,
                Headers = headers
            }}";
        }

        var tryParseHelper = errorType != null
            ? $@"

    private static T? TryParse<T>(string rawBody) where T : class
    {{
        try
        {{
            return string.IsNullOrEmpty(rawBody) ? null : JsonSerializer.Deserialize<T>(rawBody, _jsonOptions);
        }}
        catch
        {{
            return null;
        }}
    }}"
            : "";

        return $@"    private static readonly JsonSerializerOptions _jsonOptions = new() {{ PropertyNameCaseInsensitive = true }};

    public override {opName}Response InterpretResponse(HttpResponseMessage httpResponse, string rawBody)
    {{
        var headers = httpResponse.Headers.ToDictionary(h => h.Key, h => h.Value);
        
        return (int)httpResponse.StatusCode switch
        {{
{successCase}
{errorCase}
        }};
    }}{tryParseHelper}{getHeaderHelper}";
    }

    private static string GetTypeName(NJsonSchema.JsonSchema schema, Dictionary<NJsonSchema.JsonSchema, string> schemaNames)
    {
        // Try to get the actual schema if this is a reference
        var actualSchema = schema.HasReference ? schema.Reference : schema;
        
        // 1. Look up in the schemaNames dictionary (from document.Definitions)
        if (actualSchema != null && schemaNames.TryGetValue(actualSchema, out var definitionName))
        {
            return definitionName;
        }
        
        // 2. Check if it's a reference and try to find its name
        if (schema.HasReference && schema.Reference != null)
        {
            if (schemaNames.TryGetValue(schema.Reference, out var refName))
            {
                return refName;
            }
            
            // Try the schema's Id property
            if (!string.IsNullOrEmpty(schema.Reference.Id))
            {
                return schema.Reference.Id;
            }
        }
        
        // 3. Try to get the title/name from the actual schema
        if (!string.IsNullOrEmpty(actualSchema?.Title))
        {
            return actualSchema.Title;
        }
        
        // 4. Try the schema's own Id
        if (!string.IsNullOrEmpty(schema.Id))
        {
            return schema.Id;
        }

        // 5. Handle primitive types
        return (actualSchema?.Type ?? schema.Type) switch
        {
            NJsonSchema.JsonObjectType.String => "string",
            NJsonSchema.JsonObjectType.Integer => "int",
            NJsonSchema.JsonObjectType.Number => "double",
            NJsonSchema.JsonObjectType.Boolean => "bool",
            NJsonSchema.JsonObjectType.Array when schema.Item != null => $"ICollection<{GetTypeName(schema.Item, schemaNames)}>",
            NJsonSchema.JsonObjectType.Object => "object",
            _ => "object"
        };
    }

    private static string GetParameterType(NSwag.OpenApiParameter param)
    {
        var schema = param.Schema ?? param.ActualSchema;
        if (schema == null) return "string";

        var baseType = schema.Type switch
        {
            NJsonSchema.JsonObjectType.String when schema.Format == "uuid" => "Guid",
            NJsonSchema.JsonObjectType.String => "string",
            NJsonSchema.JsonObjectType.Integer when schema.Format == "int64" => "long",
            NJsonSchema.JsonObjectType.Integer => "int",
            NJsonSchema.JsonObjectType.Number => "double",
            NJsonSchema.JsonObjectType.Boolean => "bool",
            _ => "string"
        };

        return param.IsRequired ? baseType : $"{baseType}?";
    }

    private static string GenerateSpec(string name, OpenApiDocument document)
    {
        // Generate operation properties for each endpoint
        var operationProperties = new List<string>();
        
        foreach (var op in document.Operations)
        {
            var opId = op.Operation.OperationId;
            if (string.IsNullOrEmpty(opId)) continue;
            
            var opName = ToPascalCase(opId);
            operationProperties.Add($"    public {opName}Operation {opName} {{ get; }} = new();");
        }

        var propertiesCode = string.Join("\n", operationProperties);

        return $@"namespace {name};

using {name}.Contracts;

/// <summary>
/// Spec for {document.Info?.Title ?? name} API.
/// </summary>
public class {name}Spec : Spec<{name}State>
{{
{propertiesCode}

    public {name}Spec()
    {{
        RegisterOperationProperties();
    }}
}}
";
    }

    private static string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return char.ToLower(input[0]) + input.Substring(1);
    }

    private static string GenerateTests(string name, OpenApiDocument document, string? endpoint)
    {
        var endpointCode = endpoint != null 
            ? $@"new Uri(""{endpoint}"")"
            : @"new Uri(""https://your-api-endpoint.com"") // TODO: Set your endpoint";

        // Get first few operation names for examples
        var exampleOps = document.Operations
            .Take(3)
            .Select(op => ToPascalCase(op.Operation.OperationId ?? ""))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var exampleRequest = exampleOps.Count > 0 ? $"{exampleOps[0]}Request" : "SomeRequest";

        return $@"namespace {name};

using System.Net.Http;
using NUnit.Framework;
using {name}.Contracts;

/// <summary>
/// Tests for {document.Info?.Title ?? name} API using generated requests.
/// </summary>
[TestFixture]
public class {name}Tests
{{
    private HttpClient _httpClient = null!;
    private ApiClient _apiClient = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {{
        // Configure HttpClient with endpoint
        _httpClient = new HttpClient
        {{
            BaseAddress = {endpointCode}
        }};

        // TODO: Configure authentication
        // Option 1: Bearer token
        // _httpClient.DefaultRequestHeaders.Authorization = 
        //     new System.Net.Http.Headers.AuthenticationHeaderValue(""Bearer"", ""your-token"");
        
        // Option 2: API key header
        // _httpClient.DefaultRequestHeaders.Add(""X-Api-Key"", ""your-api-key"");

        // Create the API client
        _apiClient = new ApiClient(_httpClient);
    }}

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {{
        _httpClient?.Dispose();
    }}

    [Test]
    public async Task ExampleApiCall()
    {{
        // Example: Create a request and send it
        // var request = new {exampleRequest}
        // {{
        //     // Set parameters here
        // }};
        // 
        // var response = await _apiClient.SendAsync(request);
        // 
        // // Pattern match on the discriminated union response
        // switch (response)
        // {{
        //     case {exampleRequest.Replace("Request", "Response")}.Ok ok:
        //         Console.WriteLine($""Success: {{ok.Value}}"");
        //         break;
        //     case {exampleRequest.Replace("Request", "Response")}.Error err:
        //         Console.WriteLine($""Error {{err.StatusCode}}: {{err.ErrorDetails?.Error?.Message}}"");
        //         break;
        //     case {exampleRequest.Replace("Request", "Response")}.Unexpected u:
        //         Console.WriteLine($""Unexpected {{u.StatusCode}}: {{u.RawBody}}"");
        //         break;
        // }}

        await Task.CompletedTask; // Placeholder
    }}
}}
";
    }

    private static string GenerateReadme(string name, OpenApiDocument document)
    {
        return $@"# {name} - Accordant Tests

Generated from OpenAPI spec: **{document.Info?.Title ?? "Unknown"}** v{document.Info?.Version ?? "?"}

## Files

| File | Purpose |
|------|---------|
| `Contracts/Definitions.cs` | NSwag-generated DTOs (data types) |
| `Contracts/ApiInfrastructure.cs` | ApiClient, ApiRequest, ApiResponse base types |
| `Contracts/Requests.cs` | Request/Response contracts for each API operation |
| `Operations.cs` | Accordant Operation classes with Execute/Apply |
| `{name}State.cs` | State tracked by the spec |
| `{name}Spec.cs` | Spec class with registered operations |
| `{name}Tests.cs` | Test harness with auth configuration |

## Getting Started

1. **Configure endpoint** in `{name}Tests.cs`:
   ```csharp
   _httpClient = new HttpClient {{ BaseAddress = new Uri(""https://your-api"") }};
   ```

2. **Configure authentication** (bearer token, API key, or custom handler)

3. **Run tests**:
   ```bash
   dotnet test
   ```

## Usage Pattern

```csharp
// Create spec and testing context
var spec = new {name}Spec();
var context = new TestingContext();

// Register the API client
var httpClient = new HttpClient {{ BaseAddress = new Uri(""https://api.example.com"") }};
context.Register(new ApiClient(httpClient));

// Execute an operation
var request = new AccessConnectorsGetRequest
{{
    SubscriptionId = Guid.Parse(""...""),
    ResourceGroupName = ""my-rg"",
    ConnectorName = ""my-connector"",
    ApiVersion = ""2026-01-01""
}};

var response = await spec.AccessConnectorsGet.ExecuteAsync(context, request);

// Pattern match on response
switch (response)
{{
    case AccessConnectorsGetResponse.Ok ok:
        Console.WriteLine($""Success: {{ok.Value.Name}}"");
        break;
    case AccessConnectorsGetResponse.Error err:
        Console.WriteLine($""Error: {{err.ErrorDetails?.Error?.Message}}"");
        break;
}}
```

## Response Types

Each operation generates a discriminated union response type:
- `Ok` / `Created` / etc. - Success with typed value
- `Error` - 4xx with optional `ErrorResponse` and raw body
- `Unexpected` - Unhandled status code with raw body
";
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        // Remove non-alphanumeric, capitalize after separators
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
}
