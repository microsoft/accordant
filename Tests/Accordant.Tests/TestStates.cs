// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests
{
    using System.Collections.Generic;
    using Microsoft.Accordant;

    /// <summary>
    /// Simple state with a single string value.
    /// </summary>
    [State]
    public partial class StringValueState : State
    {
        public string Value { get; set; }
    }

    /// <summary>
    /// Simple state with a single int value.
    /// </summary>
    [State]
    public partial class IntValueState : State
    {
        public int Value { get; set; }
    }

    /// <summary>
    /// State with a list of strings.
    /// </summary>
    [State]
    public partial class StringListState : State
    {
        public List<string> Items { get; set; } = new();
    }

    /// <summary>
    /// State with a list of nested states.
    /// </summary>
    [State]
    public partial class NestedListState : State
    {
        public List<StringValueState> Items { get; set; } = new();
    }

    /// <summary>
    /// State with a dictionary of strings.
    /// </summary>
    [State]
    public partial class StringDictState : State
    {
        public Dictionary<string, string> Map { get; set; } = new();
    }

    /// <summary>
    /// State with a dictionary of nested states.
    /// </summary>
    [State]
    public partial class NestedDictState : State
    {
        public Dictionary<string, StringValueState> Map { get; set; } = new();
    }
}
