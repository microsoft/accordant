namespace PackageCompatibility.SharedFrameworkLibrary;

using System.Text.Json;

public static class JsonTypes
{
    public static string Description => typeof(JsonSerializer).Assembly.FullName!;
}
