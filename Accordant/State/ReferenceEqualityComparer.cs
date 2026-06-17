// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// An equality comparer that uses reference equality (object identity)
/// instead of value equality. Required for netstandard2.0 where
/// System.Collections.Generic.ReferenceEqualityComparer is not available.
/// </summary>
internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

    private ReferenceEqualityComparer() { }

    public new bool Equals(object x, object y) => ReferenceEquals(x, y);

    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
