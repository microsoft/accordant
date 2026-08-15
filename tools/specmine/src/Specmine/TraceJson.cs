// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Shared JSON serialization settings used to snapshot request/response values and to
/// persist whole trace files.
/// </summary>
internal static class TraceJson
{
    /// <summary>
    /// Options used when writing and reading a whole <see cref="RecordedTrace"/> file.
    /// The file is indented for readability, since a trace is a single artifact meant
    /// to be inspected by a person or a maintainer-side tool.
    /// </summary>
    public static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Options used to snapshot an individual request or response value into a
    /// <see cref="JsonElement"/> at the moment it is recorded, so later mutation of the
    /// original object cannot change the recorded value.
    /// </summary>
    public static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
