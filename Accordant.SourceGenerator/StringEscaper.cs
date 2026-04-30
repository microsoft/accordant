// Copyright (c) Accordant. All rights reserved.
// Licensed under the MIT license.

namespace Microsoft.Accordant.SourceGenerator
{
    /// <summary>
    /// Generates C# code expressions for string escaping in fingerprint output.
    /// 
    /// The generated code at runtime will:
    /// - For null values: produce the literal text "null" (4 characters, no quotes)
    /// - For non-null values: produce a quoted string with internal escaping
    ///   - Backslashes are doubled: \ → \\
    ///   - Quotes are escaped: " → \"
    ///   - Result is wrapped in quotes: hello → "hello"
    /// 
    /// Example runtime transformations:
    ///   null       → null
    ///   ""         → ""
    ///   hello      → "hello"
    ///   say "hi"   → "say \"hi\""
    ///   a\b        → "a\\b"
    /// </summary>
    internal static class StringEscaper
    {
        /// <summary>
        /// Generates a C# expression that produces an escaped string representation.
        /// 
        /// The generated expression evaluates at runtime to:
        ///   value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
        /// </summary>
        /// <param name="valueExpr">The C# expression that produces the string to escape.</param>
        /// <returns>A C# expression string to embed in generated code.</returns>
        public static string GenerateEscapedStringExpression(string valueExpr)
        {
            // Break down the escaping layers:
            //
            // We need to generate C# source code that, when compiled and executed, will:
            //   1. Check if value is null → output literal "null"
            //   2. Otherwise: escape backslashes, escape quotes, wrap in quotes
            //
            // Layer 1 (runtime C#):     value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            // Layer 2 (C# string literal for layer 1 string constants):
            //   "\\" in source → \ at runtime
            //   "\\\\" in source → \\ at runtime
            //   "\\\"" in source → \" at runtime

            const string nullCheck = "\"null\"";
            const string openQuote = "\"\\\"\"";   // produces literal " at runtime
            const string closeQuote = "\"\\\"\"";   // produces literal " at runtime

            // Replace("\\", "\\\\") — escape backslashes
            const string replaceBackslash = ".Replace(\"\\\\\", \"\\\\\\\\\")";
            // Replace("\"", "\\\"") — escape quotes
            const string replaceQuote = ".Replace(\"\\\"\", \"\\\\\\\"\")";

            return $"({valueExpr} == null ? {nullCheck} : {openQuote} + {valueExpr}{replaceBackslash}{replaceQuote} + {closeQuote})";
        }
        /// <summary>
        /// Generates a C# expression that produces an escaped string for use as a dictionary key
        /// in fingerprint path expressions. Wraps the result in brackets: [escapedKey]
        /// 
        /// For null keys: produces "null" (no brackets)
        /// For non-null keys: produces "\"key\"" with internal escaping, wrapped in brackets
        /// </summary>
        /// <param name="keyExpr">The C# expression that produces the dictionary key string.</param>
        /// <param name="pathExpr">The C# expression for the current path.</param>
        /// <returns>A C# expression string for the path with the escaped key appended.</returns>
        public static string GeneratePathKeyExpression(string keyExpr, string pathExpr)
        {
            var escapedKey = GenerateEscapedStringExpression(keyExpr);
            return $"{pathExpr} + \"[\" + {escapedKey} + \"]\"";
        }
    }
}
