// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using System.Text.Json;

/// <summary>
/// Builds the <c>{ "document": ..., "baseUrl": ... }</c> settings <see cref="Specmine.Adapters.OpenApi.OpenApiTargetAdapter"/>
/// expects, as a convenience for tests.
/// </summary>
internal static class OpenApiAdapterTestSettings
{
    public static JsonElement Create(string documentPath, string baseUrl) =>
        JsonSerializer.SerializeToElement(new { document = documentPath, baseUrl });
}