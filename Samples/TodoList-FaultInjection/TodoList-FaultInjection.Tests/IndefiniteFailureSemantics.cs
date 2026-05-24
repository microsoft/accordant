// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Tests;

using System;
using System.Threading;

/// <summary>
/// Controls whether operations include indefinite failure outcomes in the model.
/// </summary>
public static class IndefiniteFailureSemantics
{
    /// <summary>
    /// AsyncLocal flag to control whether Apply() adds indefinite failure outcomes.
    /// Defaults to true (included). Use Suppress() for scoped suppression.
    /// AsyncLocal flows correctly across async/await continuations.
    /// </summary>
    private static readonly AsyncLocal<bool?> _enabled = new();

    public static bool Enabled
    {
        get => _enabled.Value ?? true;
        set => _enabled.Value = value;
    }

    /// <summary>
    /// Executes the given action with indefinite failure outcomes suppressed.
    /// Useful for baseline tests that assume no failures.
    /// </summary>
    public static void Suppress(Action action)
    {
        var previous = Enabled;
        try
        {
            Enabled = false;
            action();
        }
        finally
        {
            Enabled = previous;
        }
    }

    /// <summary>
    /// Executes the given function with indefinite failure outcomes suppressed.
    /// </summary>
    public static T Suppress<T>(Func<T> func)
    {
        var previous = Enabled;
        try
        {
            Enabled = false;
            return func();
        }
        finally
        {
            Enabled = previous;
        }
    }
}
