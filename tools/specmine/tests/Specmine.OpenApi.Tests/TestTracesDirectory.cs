// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using NUnit.Framework;

/// <summary>
/// Creates an isolated, empty directory (nested under the test assembly's own output
/// directory) for a single test's trace files, and removes it when disposed.
/// </summary>
internal sealed class TestTracesDirectory : IDisposable
{
    public string Path { get; }

    public TestTracesDirectory()
    {
        Path = System.IO.Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "specmine-openapi-traces",
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