// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>
/// Simple stack state - a minimal example of JsonState.
/// The state is pure data: no methods, no computed properties.
/// </summary>
public class StackState<TElement> : JsonState
{
    /// <summary>
    /// The items in the stack. Always initialize collections to avoid null reference issues.
    /// </summary>
    public List<TElement> Items { get; set; } = new List<TElement>();
}
