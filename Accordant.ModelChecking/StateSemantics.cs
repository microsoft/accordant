namespace Microsoft.Accordant.ModelChecking;

using System;

internal static class StateSemantics
{
    public static bool Equal(IState left, IState right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.GetStateHash() != right.GetStateHash())
            return false;

        return string.Equals(
            left.StringRepresentation(),
            right.StringRepresentation(),
            StringComparison.Ordinal);
    }
}
