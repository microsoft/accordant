// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Adapters.OpenApi;

/// <summary>Where an OpenAPI parameter is bound. Only these three locations are supported.</summary>
internal enum OpenApiParameterLocation
{
    Path,
    Query,
    Header,
}

/// <summary>
/// The execution-time shape of one declared parameter: enough to map a runtime request's
/// <c>path</c>/<c>query</c>/<c>headers</c> section into an actual HTTP request. The
/// parameter's JSON Schema (used only to build the operation's advertised request schema)
/// lives separately in <see cref="OpenApiDocumentParser"/>'s own parsing types and does not
/// need to be kept around at execution time.
/// </summary>
internal sealed record OpenApiParameterSpec(string Name, bool Required);

/// <summary>The execution-time shape of a declared JSON request body: only whether it is required.</summary>
internal sealed record OpenApiRequestBodySpec(bool Required);

/// <summary>
/// The execution-time shape of one operation: enough for <see cref="OpenApiTargetSession"/>
/// to map a concrete <c>{ path, query, headers, body }</c> request into an actual HTTP call
/// and dispatch it, without needing to re-derive anything from the operation's advertised
/// JSON Schema.
/// </summary>
internal sealed record OpenApiOperationSpec(
    string OperationId,
    string HttpMethod,
    string PathTemplate,
    IReadOnlyList<OpenApiParameterSpec> PathParameters,
    IReadOnlyList<OpenApiParameterSpec> QueryParameters,
    IReadOnlyList<OpenApiParameterSpec> HeaderParameters,
    OpenApiRequestBodySpec? RequestBody);

/// <summary>
/// One fully parsed operation: the portable <see cref="OperationDefinition"/> a session
/// advertises, paired with the <see cref="OpenApiOperationSpec"/> used internally to
/// execute it.
/// </summary>
internal sealed record OpenApiOperation(OperationDefinition Definition, OpenApiOperationSpec ExecutionSpec);
