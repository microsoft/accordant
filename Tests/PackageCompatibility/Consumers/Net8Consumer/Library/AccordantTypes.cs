namespace PackageCompatibility.ConsumerLibrary;

using Microsoft.Accordant;

public static class AccordantTypes
{
    public static string Description => $"{typeof(StateAttribute).FullName};{typeof(Spec).FullName}";
}
