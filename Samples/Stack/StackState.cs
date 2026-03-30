// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Stack
{
    using System.Collections.Generic;
    using Microsoft.Accordant;

    /// <summary>
    /// Stack state using JsonState for simplicity.
    /// Just a plain data structure - no methods, no computed properties.
    /// </summary>
    public class StackState<TElement> : JsonState
    {
        public List<TElement> Items { get; set; } = new List<TElement>();
    }
}
