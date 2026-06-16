// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

/// <summary>
/// Marks a POCO class for state source generation.
/// 
/// The source generator will automatically generate:
/// - Clone() implementation for deep copying
/// - StringRepresentationInternal() for fingerprinting
/// - LockComponents() for immutability
/// 
/// Requirements:
/// - Must be a class (not struct or record)
/// - Must be declared as partial
/// - Must be at namespace level (not nested inside another type)
/// - Must have an accessible parameterless constructor
/// 
/// Supported property types:
/// - Primitives (int, bool, string, etc.)
/// - Enums
/// - DateTime, TimeSpan, Guid, DateOnly, TimeOnly
/// - Other [State] classes
/// - List&lt;T&gt;, Dictionary&lt;K,V&gt; (where K is primitive/string/enum), arrays
/// - Tuples
/// - Nullable versions of the above
/// 
/// Not supported:
/// - HashSet (use List instead)
/// - Interface types (IList, IDictionary, etc.)
/// - Records (cannot inherit from State base class)
/// - Nested classes
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StateAttribute : Attribute
{
}
