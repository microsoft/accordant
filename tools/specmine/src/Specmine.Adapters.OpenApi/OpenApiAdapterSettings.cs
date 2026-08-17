// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Adapters.OpenApi;

using System.Text.Json;

/// <summary>
/// The settings shape <see cref="OpenApiTargetAdapter"/> owns and validates:
/// <c>{ "document": string, "baseUrl": string }</c>, where <c>document</c> is a local JSON
/// file path (resolved relative to the current process's working directory when not
/// absolute - see <see cref="Path.GetFullPath(string)"/>) and <c>baseUrl</c> is an
/// absolute <c>http</c>/<c>https</c> URL the adapter's own <see cref="HttpClient"/> sends
/// requests against. No credential shape is defined yet - this slice only covers
/// unauthenticated targets.
/// </summary>
internal sealed record OpenApiAdapterSettings(string DocumentPath, string BaseUrl)
{
    /// <summary>
    /// Validates and interprets <paramref name="settings"/>, resolving <c>document</c> to
    /// a full path (without requiring the file to exist yet - existence is checked when
    /// actually reading it in <see cref="OpenApiTargetAdapter.ConnectAsync"/>).
    /// </summary>
    /// <exception cref="OpenApiSettingsException">The settings are missing or malformed.</exception>
    public static OpenApiAdapterSettings Parse(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            throw new OpenApiSettingsException(
                $"OpenAPI adapter settings must be a JSON object with 'document' and 'baseUrl' string " +
                $"properties; got '{settings.ValueKind}'.");
        }

        var documentPath = RequireNonBlankString(settings, "document");
        var baseUrlText = RequireNonBlankString(settings, "baseUrl");

        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new OpenApiSettingsException(
                $"OpenAPI adapter settings 'baseUrl' must be an absolute http(s) URL; got '{baseUrlText}'.");
        }

        return new OpenApiAdapterSettings(Path.GetFullPath(documentPath), baseUrlText);
    }

    private static string RequireNonBlankString(JsonElement settings, string propertyName)
    {
        if (!settings.TryGetProperty(propertyName, out var valueElement))
        {
            throw new OpenApiSettingsException($"OpenAPI adapter settings are missing required property '{propertyName}'.");
        }

        if (valueElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(valueElement.GetString()))
        {
            throw new OpenApiSettingsException($"OpenAPI adapter settings property '{propertyName}' must be a nonblank string.");
        }

        return valueElement.GetString()!;
    }
}
