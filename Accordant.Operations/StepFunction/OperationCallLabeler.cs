// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;

/// <summary>
/// Assigns the short label (e.g. <c>[u]</c>, <c>[v]</c>) that distinguishes one
/// invocation of an operation from another within a single test-generation run.
///
/// <para>The counters are deliberately scoped to one exploration: the labeler is
/// created per <c>ConstructStateSpaceGraph</c> call and threaded through every
/// <see cref="InputStepFunction"/>, including the ones spawned as derivations.
/// Keeping this state in one named object makes the only mutable generation state
/// explicit and unit-testable, instead of a loose dictionary shared alongside
/// unrelated request/response dictionaries.</para>
/// </summary>
internal sealed class OperationCallLabeler
{
    private static readonly char[] LabelChars =
    {
        's', 'u', 'p', 'e', 'r', 'g', 'y', 'a', 'q', 'z', 'c', 'o',
        'i', 'b', 't', 'd', 'l', 'm', 'v', 'f', 'w', 'j', 'n',
        'x', 'k', 'h'
    };

    private readonly Dictionary<string, int> counts = new Dictionary<string, int>();

    /// <summary>
    /// Returns the next label for the given operation name and advances that
    /// operation's counter, so successive calls yield distinct labels.
    /// </summary>
    public string NextLabel(string operationName)
    {
        counts.TryGetValue(operationName, out var count);
        counts[operationName] = count + 1;
        return GetArbitraryLabel(count);
    }

    /// <summary>
    /// Converts a zero-based call index into its label (0 = "s", 1 = "u", ...).
    /// </summary>
    internal static string GetArbitraryLabel(int num)
    {
        var randomStringsLength = LabelChars.Length;

        num = num + 1;

        string result = string.Empty;
        while (num > 0)
        {
            num--;
            var part = LabelChars[num % randomStringsLength];
            result = part + result;
            num /= randomStringsLength;
        }

        return result;
    }
}
