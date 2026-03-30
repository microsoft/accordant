// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    /// <summary>
    /// The unit type - a type with exactly one value.
    /// Use this when specifying operations that either take no request 
    /// (no parameters) or return no response (void).
    /// This is the standard name in functional programming (F#, Scala, Rust, Haskell).
    /// </summary>
    public class Unit
    {
        /// <summary>
        /// The single value of the Unit type.
        /// </summary>
        public static readonly Unit Value = new Unit();

        public override string ToString()
        {
            return "()";
        }
    }
}
