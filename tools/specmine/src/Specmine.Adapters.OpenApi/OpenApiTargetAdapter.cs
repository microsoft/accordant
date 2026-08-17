// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Adapters.OpenApi;

using System.Text.Json;

/// <summary>
/// A built-in <see cref="ITargetAdapter"/> that turns a local OpenAPI 3.0.x JSON document
/// into a live <see cref="ITargetSession"/> talking to a real HTTP target: it parses the
/// document (see <see cref="OpenApiDocumentParser"/>) into a stable operation catalog keyed
/// by each operation's declared <c>operationId</c>, and connects an <see cref="HttpClient"/>
/// against the configured <c>baseUrl</c> for <see cref="OpenApiTargetSession"/> to execute
/// calls with.
///
/// This adapter owns its own settings shape entirely: <c>{ "document": string, "baseUrl": string }</c>,
/// where <c>document</c> is a local JSON file path (resolved relative to the current
/// process's working directory when not already absolute) and <c>baseUrl</c> is an
/// absolute <c>http</c>/<c>https</c> URL. No credential shape is defined by this slice;
/// only unauthenticated targets are supported so far.
/// </summary>
public sealed class OpenApiTargetAdapter : ITargetAdapter
{
    /// <inheritdoc/>
    public string AdapterType => "openapi";

    /// <inheritdoc/>
    /// <exception cref="OpenApiSettingsException">
    /// <paramref name="settings"/> is not a JSON object, or its <c>document</c>/<c>baseUrl</c>
    /// properties are missing or malformed.
    /// </exception>
    /// <exception cref="FileNotFoundException"><c>document</c> does not resolve to an existing file.</exception>
    /// <exception cref="JsonException"><c>document</c>'s contents are not well-formed JSON.</exception>
    /// <exception cref="OpenApiDocumentException">
    /// <c>document</c> is well-formed JSON but is not an OpenAPI 3.0.x document this
    /// adapter can interpret - see <see cref="OpenApiDocumentParser"/>.
    /// </exception>
    public async Task<ITargetSession> ConnectAsync(JsonElement settings, CancellationToken cancellationToken = default)
    {
        if (settings.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Settings must be a valid JSON value.", nameof(settings));
        }

        var adapterSettings = OpenApiAdapterSettings.Parse(settings);

        if (!File.Exists(adapterSettings.DocumentPath))
        {
            throw new FileNotFoundException(
                $"OpenAPI document was not found at '{adapterSettings.DocumentPath}'.", adapterSettings.DocumentPath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<OpenApiOperation> operations;
        await using (var stream = File.OpenRead(adapterSettings.DocumentPath))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            operations = OpenApiDocumentParser.Parse(document.RootElement);
        }

        var httpClient = new HttpClient();
        return new OpenApiTargetSession(httpClient, adapterSettings.BaseUrl, operations);
    }
}