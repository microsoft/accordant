// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;

    /// <summary>
    /// This attribute marks a class as defining a state object. The state generator
    /// tool can be used to process classes marked with this attribute to generate
    /// state classes to be used in the specs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class StateDefinitionAttribute : Attribute
    {
    }
}
