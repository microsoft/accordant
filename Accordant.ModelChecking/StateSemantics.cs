namespace Microsoft.Accordant.ModelChecking;

internal static class StateSemantics
{
    public static bool Equal(IState left, IState right)
    {
        if (ReferenceEquals(left, right))
            return true;
        return left != null
            && right != null
            && left.GetStateHash() == right.GetStateHash();
    }
}
