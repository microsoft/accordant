// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;

/// <summary>
/// Advanced state example with [SharedState] for binary data.
/// Use [SharedState] when you have large data that's expensive to deep-copy.
/// </summary>
[State]
public partial class ImageState : State
{
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    
    /// <summary>
    /// Processing state for async operations: "Creating", "Created", "Failed"
    /// </summary>
    public string State { get; set; } = "Creating";

    /// <summary>
    /// Binary content with [SharedState] for shallow copy.
    /// The attribute requires a fingerprint property for state equality.
    /// </summary>
    [SharedState(Fingerprint = nameof(ContentFingerprint))]
    public List<byte> Content { get; set; } = new();

    /// <summary>
    /// Fingerprint property for [SharedState] - used for state equality comparison.
    /// </summary>
    public string ContentFingerprint => Content == null || Content.Count == 0
        ? string.Empty
        : string.Join(string.Empty, Content.Select(b => b.ToString("x2")));
}
