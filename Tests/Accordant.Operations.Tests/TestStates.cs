// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Operations.Tests;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>
/// Simple state holding an integer value.
/// </summary>
[State]
public partial class CounterState : State
{
    public int Value { get; set; }

    public CounterState() { }

    public CounterState(int value)
    {
        Value = value;
    }
}

/// <summary>
/// State for a single blog post entry.
/// </summary>
[State]
public partial class BlogPostEntryState : State
{
    public string Name { get; set; }
    public string Content { get; set; }
}

/// <summary>
/// State for a collection of blog posts, keyed by ID.
/// </summary>
[State]
public partial class BlogPostsState : State
{
    public Dictionary<string, BlogPostEntryState> Posts { get; set; } = new();
}

/// <summary>
/// State with a list of counter values.
/// </summary>
[State]
public partial class CounterListState : State
{
    public List<CounterState> Items { get; set; } = new();
}
