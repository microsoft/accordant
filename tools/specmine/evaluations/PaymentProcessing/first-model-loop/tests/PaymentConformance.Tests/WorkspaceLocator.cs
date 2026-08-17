namespace PaymentConformance.Tests;

/// <summary>
/// Resolves the black-box investigation's workspace root (the directory containing
/// <c>workspace.json</c>) the same way <c>model\ReplayRunner.cs</c> does: by walking up from a
/// starting directory. No test source file hardcodes an absolute path to the workspace.
///
/// The search starts from the test assembly's own build output directory
/// (<c>AppContext.BaseDirectory</c>), which sits under
/// <c>tests\PaymentConformance.Tests\bin\...\</c>, so walking up reaches the workspace root
/// regardless of the configuration (Debug/Release) or target framework moniker in the output
/// path. An environment variable override is honored first, for CI layouts or a copy of this
/// project run outside this workspace entirely.
/// </summary>
internal static class WorkspaceLocator
{
    public const string WorkspaceRootEnvironmentVariable = "PAYMENT_CONFORMANCE_WORKSPACE_ROOT";

    public static string ResolveWorkspaceRoot()
    {
        var overridden = Environment.GetEnvironmentVariable(WorkspaceRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            if (!File.Exists(Path.Combine(overridden, "workspace.json")))
            {
                throw new InvalidOperationException(
                    $"{WorkspaceRootEnvironmentVariable} was set to '{overridden}', but that directory does not " +
                    "contain workspace.json.");
            }

            return Path.GetFullPath(overridden);
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "workspace.json")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate workspace.json by walking up from the test assembly's directory " +
            $"({AppContext.BaseDirectory}). Set {WorkspaceRootEnvironmentVariable} to the workspace root explicitly " +
            "if this test is being run from outside its normal location.");
    }
}
