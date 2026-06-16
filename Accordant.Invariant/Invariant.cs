// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

public static class Invariant
{
    public static void Assert(bool condition, string message = null)
    {
        if (!condition)
        {
            throw new UnexpectedInvariantException(message);
        }
    }

    public static UnexpectedInvariantException InaccessibleCode()
    {
        return new UnexpectedInvariantException("This code should never have been hit.");
    }
}
