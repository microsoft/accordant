// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking
{
    using System;

    /// <summary>
    /// This class is used to check safety and liveness properties of a given state space graph.
    /// </summary>
    public static class Property
    {
        /// <summary>
        /// Check a safety and/or liveness property. The user provided property lambda
        /// is given a <see cref="PropertyChecker">checker</see> object which can be used
        /// to check safety and/or liveness properties starting from a given node in the
        /// state graph.
        /// </summary>
        public static PropertyCheckingResult Check(Func<PropertyChecker, bool> property)
        {
            var checker = new PropertyChecker();

            var valid = property(checker);
            var trace = valid ? null : checker.NestedTraces.Peek();

            return new PropertyCheckingResult
            {
                Valid = valid,
                Trace = trace
            };
        }
    }
}
