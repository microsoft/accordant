// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TodoList.FaultInjection.Api.FaultInjection;

/// <summary>
/// Configuration for server-side fault injection.
/// Faults injected here simulate database/infrastructure failures
/// that manifest as 500 Internal Server Error to the client.
/// </summary>
public class ServerFaultConfig
{
    /// <summary>
    /// Probability (0.0 to 1.0) of throwing an exception before SaveChanges.
    /// This simulates: DB connection lost, timeout before write, etc.
    /// The operation did NOT happen.
    /// </summary>
    public double PreSaveFaultProbability { get; set; } = 0.0;

    /// <summary>
    /// Probability (0.0 to 1.0) of throwing an exception after SaveChanges.
    /// This simulates: connection lost after write committed, timeout after success.
    /// The operation DID happen, but client sees an error.
    /// This is the most dangerous case - creates true indefinite failures.
    /// </summary>
    public double PostSaveFaultProbability { get; set; } = 0.0;

    /// <summary>
    /// Probability (0.0 to 1.0) of throwing an exception during read operations.
    /// Reads are safe - no state change occurred.
    /// </summary>
    public double ReadFaultProbability { get; set; } = 0.0;

    /// <summary>
    /// When true, faults are injected. When false, normal operation.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Random seed for reproducible fault patterns. Null = non-deterministic.
    /// </summary>
    public int? Seed { get; set; }
}

/// <summary>
/// Exception thrown to simulate server-side failures.
/// </summary>
public class SimulatedServerFaultException : Exception
{
    public SimulatedServerFaultException(string message) : base(message) { }
}
