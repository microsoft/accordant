// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    /// <summary>
    /// Marks a property as atomic in a <see cref="JsonState"/> class.
    /// 
    /// Atomic properties are:
    /// - Excluded from JSON serialization (not cloned via JSON)
    /// - Copied by reference during clone (shallow copy)
    /// - Expected to be immutable (user promises not to mutate after setting)
    /// 
    /// The user must provide a separate property (getter-only) that computes
    /// a string representation for fingerprinting purposes.
    /// 
    /// Example usage:
    /// <code>
    /// public class ImageJsonState : JsonState
    /// {
    ///     [JsonAtomic(nameof(ContentFingerprint))]
    ///     public byte[] Content { get; set; }
    ///     
    ///     // Computed fingerprint - included in JSON for hashing
    ///     public string ContentFingerprint => Content == null 
    ///         ? null 
    ///         : Convert.ToHexString(SHA256.HashData(Content));
    /// }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class JsonAtomicAttribute : Attribute
    {
        /// <summary>
        /// Name of the property that provides the string representation 
        /// for fingerprinting. That property should be a getter-only property.
        /// </summary>
        public string FingerprintProperty { get; }

        /// <summary>
        /// Creates a new JsonAtomicAttribute.
        /// </summary>
        /// <param name="fingerprintProperty">
        /// Name of the getter-only property that provides a string representation
        /// of this atomic value for fingerprinting purposes.
        /// </param>
        public JsonAtomicAttribute(string fingerprintProperty)
        {
            FingerprintProperty = fingerprintProperty;
        }
    }
}
