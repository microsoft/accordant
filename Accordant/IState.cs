// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

/// <summary>
/// Base interface for all state objects used in specification and state exploration.
/// 
/// States can be either mutable (freeze-on-use) or immutable (always frozen).
/// The framework treats them uniformly through this interface.
/// </summary>
public interface IState
{
    /// <summary>
    /// Indicates whether the state is frozen (immutable).
    /// Once frozen, the state cannot be modified - any attempt should throw.
    /// Immutable states are always frozen from creation.
    /// </summary>
    bool IsFrozen { get; }

    /// <summary>
    /// Freezes the state, making it immutable.
    /// After freezing, any modification attempts should throw <see cref="StateFrozenException"/>.
    /// For already-immutable states, this is a no-op.
    /// </summary>
    void Freeze();

    /// <summary>
    /// Creates a clone of this state.
    /// For mutable states: returns a deep copy that is not frozen (can be modified).
    /// For immutable states: may return the same instance (since it can't change).
    /// </summary>
    IState Clone();

    /// <summary>
    /// Returns a 64-bit hash of the state's contents.
    /// Two states with the same logical content must return the same hash.
    /// Used for state deduplication during state space exploration.
    /// 
    /// Implementation notes:
    /// - Collections (lists, dictionaries) must use deterministic ordering
    /// - Dictionaries should sort by key before hashing
    /// - The hash is cached after the state is frozen
    /// </summary>
    ulong GetStateHash();

    /// <summary>
    /// Returns a deterministic string representation of the state.
    /// Two states with the same logical content must return the same string.
    /// Used for debugging, logging, and as an input for hash computation.
    /// </summary>
    string StringRepresentation();
}
