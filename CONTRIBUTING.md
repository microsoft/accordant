# Contributing to Accordant

This project welcomes contributions and suggestions. Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Create a branch for your changes

## Building

```bash
dotnet build Accordant.slnx
```

## Running Tests

```bash
dotnet test Accordant.slnx
```

## Working with Samples

Samples use the `Microsoft.Accordant` NuGet package (not project references). For local development:

1. After making changes to Accordant libraries, publish to the local feed:
   ```powershell
   .\Scripts\Publish-Local.ps1
   ```

2. Then build/test samples normally:
   ```bash
   dotnet test Samples/TodoList/TodoList.Tests
   ```

The local NuGet feed (`bin/packages`) takes priority over nuget.org, so samples will use your locally-built package.

## Submitting Changes

1. Ensure all tests pass
2. Update documentation if needed
3. Submit a pull request with a clear description of changes

## Reporting Issues

Please use GitHub Issues to report bugs or request features. Include:

- Clear description of the issue
- Steps to reproduce (for bugs)
- Expected vs actual behavior
- .NET version and OS
