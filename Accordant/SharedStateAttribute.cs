// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

/// <summary>
/// Marks a property in a [State] class as shared state.
/// 
/// Shared state properties are:
/// - Cloned by reference (not deep cloned)
/// - Not locked when the containing state is locked
/// - Still contribute to fingerprinting for state equality
/// 
/// Use this for large immutable data (like image content) that should be
/// shared across clones for performance.
/// 
/// For [State] class properties, the default fingerprinting uses StringRepresentation().
/// For other supported types (primitives, collections), a custom fingerprint method is required.
/// 
/// Example:
/// <code>
/// [State]
/// public partial class ImageState
/// {
///     public string Name { get; set; }
///     
///     [SharedState(Fingerprint = nameof(ContentFingerprint))]
///     public List&lt;byte&gt; Content { get; set; }
///     
///     private string ContentFingerprint(List&lt;byte&gt; content) => 
///         content == null ? null : Convert.ToBase64String(content.ToArray());
/// }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SharedStateAttribute : Attribute
{
    /// <summary>
    /// Name of a method on the containing class that computes the fingerprint for this property.
    /// The method should have signature: string MethodName(PropertyType value)
    /// 
    /// Required for non-[State] types (primitives, collections, etc.).
    /// Optional for [State] types (defaults to StringRepresentation()).
    /// </summary>
    public string Fingerprint { get; set; }

    public SharedStateAttribute()
    {
    }
}
