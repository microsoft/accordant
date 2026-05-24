// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Tests;

/// <summary>
/// Configuration for client-side fault injection.
/// Faults injected here simulate network failures where we don't know
/// if the request reached the server.
/// </summary>
public class ClientFaultConfig
{
    /// <summary>
    /// Probability (0.0 to 1.0) of simulating a network failure before the request is sent.
    /// If this triggers, the request definitely did NOT reach the server.
    /// </summary>
    public double PreRequestFaultProbability { get; set; } = 0.0;

    /// <summary>
    /// Probability (0.0 to 1.0) of simulating a network failure after a successful response.
    /// If this triggers for a write operation, we don't know if the server processed it
    /// (the real response is lost, and we report a timeout).
    /// This creates true client-side indefinite failures.
    /// </summary>
    public double PostResponseFaultProbability { get; set; } = 0.0;

    /// <summary>
    /// When true, faults are injected. When false, normal operation.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Random seed for reproducible fault patterns.
    /// </summary>
    public int? Seed { get; set; }
}
