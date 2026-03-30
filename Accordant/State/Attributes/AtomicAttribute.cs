// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    /// <summary>
    /// This attribute marks a property inside a class marked with the <see cref="StateDefinitionAttribute"/>
    /// attribute as being an atomic property. Properties of types like strings, integers, bools etc are already
    /// treated as atomic properties but you might want to treat a complex property (say, List&lt;byte&gt;) as an
    /// atomic property. This attribute can be used to mark such complex properties as atomic. You must provide
    /// the fully qualified name of a static method that can be used to return a string representation of the
    /// property, to be used when generating the string representation of the state.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class AtomicAttribute : Attribute
    {
        /// <summary>
        /// The fully qualified name of a static method that takes the value of the property and
        /// returns its string representation. This string representation is used while generating
        /// the string representation of the state object this property is a part of. The string representation
        /// for different values of the property must be unique.
        /// </summary>
        public string ToStringMethod { get; private set; }

        /// <summary>
        /// Constructs an instance of this attribute.
        /// </summary>
        public AtomicAttribute(string toStringMethod)
        {
            ToStringMethod = toStringMethod;
        }
    }
}
