// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoListExtended.Api.Controllers;

/// <summary>
/// Simple write lock for serializing write operations.
/// This ensures that concurrent writes are properly serialized for testing.
/// </summary>
public class WriteLock
{
    public object Lock { get; } = new object();
}
