// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

/// <summary>
/// Exception thrown when a JsonState contains a dictionary with an unsupported key type.
/// JsonState requires dictionary keys to be one of the supported types to ensure
/// deterministic serialization and hashing.
/// </summary>
public class UnsupportedDictionaryKeyTypeException : Exception
{
    /// <summary>
    /// The unsupported key type that was encountered.
    /// </summary>
    public Type KeyType { get; }

    public UnsupportedDictionaryKeyTypeException(Type keyType)
        : base($"Dictionary key type '{keyType.FullName}' is not supported in JsonState. " +
               $"Supported key types are: string, int, long, Guid. " +
               $"Consider using one of these types as your dictionary key.")
    {
        KeyType = keyType;
    }
}
