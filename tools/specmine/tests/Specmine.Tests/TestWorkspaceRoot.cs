// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using NUnit.Framework;

/// <summary>
/// Reserves an isolated, non-existent directory path (nested under the test assembly's
/// own output directory) for a single test's workspace root, and removes anything
/// created there when disposed.
/// </summary>
internal sealed class TestWorkspaceRoot : IDisposable
{
    public string Path { get; }

    public TestWorkspaceRoot()
    {
        Path = System.IO.Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "specmine-workspaces",
            Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}