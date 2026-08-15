// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// Shared JSON serialization settings used to write <c>workspace.json</c>.
/// </summary>
internal static class WorkspaceJson
{
    /// <summary>
    /// Options used when writing <c>workspace.json</c>. The file is indented for
    /// readability, since it is a small artifact meant to be inspected by a person.
    /// </summary>
    public static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true
    };
}