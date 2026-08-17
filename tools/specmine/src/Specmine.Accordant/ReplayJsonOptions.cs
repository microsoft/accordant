// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Accordant;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Produces the default <see cref="JsonSerializerOptions"/> <see cref="TraceReplayer"/> uses
/// to deserialize a trace call's request/response <see cref="System.Text.Json.JsonElement"/>
/// into an operation's declared <c>RequestType</c>/<c>ResponseType</c> when a caller does not
/// supply its own options.
/// </summary>
public static class ReplayJsonOptions
{
    /// <summary>
    /// Creates a fresh, unlocked <see cref="JsonSerializerOptions"/> instance with sensible
    /// "web" defaults: camelCase property names, case-insensitive property matching, and
    /// numbers readable from strings (<see cref="JsonSerializerDefaults.Web"/>), plus
    /// camelCase string enum support to match how <c>Specmine</c> itself snapshots enum
    /// values into a trace (see <c>TraceJson</c>). A caller with a different wire format
    /// (e.g. a non-.NET recorder) can pass its own <see cref="JsonSerializerOptions"/> to
    /// <see cref="TraceReplayer"/> instead of this default.
    ///
    /// A new instance is returned on every call - <see cref="JsonSerializerOptions"/>
    /// becomes read-only after its first use, so sharing a single static instance would
    /// prevent a caller from ever customizing a copy of the default.
    /// </summary>
    public static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
