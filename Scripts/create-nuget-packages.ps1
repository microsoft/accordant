# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

Import-Module $PSScriptRoot/common.psm1 -Force

$nuget = "nuget"

# Check that NuGet.exe is installed.
if (-not (Get-Command $nuget -errorAction SilentlyContinue)) {
    Write-Comment -text "Please install the latest NuGet.exe from https://www.nuget.org/downloads and add it to the PATH environment variable." -color "yellow"
    exit 1
}

Write-Comment -prefix "." -text "Creating the Accordant NuGet packages" -color "yellow"

$version = "0.1.5"

# Setup the command line options for nuget pack.
$cmd_options = "-OutputDirectory $PSScriptRoot/../bin/packages -Version $version"
$cmd_options = "$cmd_options -Symbols -SymbolPackageFormat snupkg"

Write-Comment -prefix "..." -text "Creating the 'Microsoft.Accordant' package"

$command = "pack $PSScriptRoot/../nuget/Microsoft.Accordant.nuspec $cmd_options"
$error_msg = "Failed to create the Microsoft.Accordant NuGet package"
Invoke-ToolCommand -tool $nuget -cmd $command -error_msg $error_msg

# Tool packages don't need symbols
$tool_cmd_options = "-OutputDirectory $PSScriptRoot/../bin/packages -Version $version"

Write-Comment -prefix "..." -text "Creating the 'Microsoft.Accordant.Cli' package (dotnet tool)"

$command = "pack $PSScriptRoot/../nuget/Microsoft.Accordant.Cli.nuspec $tool_cmd_options"
$error_msg = "Failed to create the Microsoft.Accordant.Cli NuGet package"
Invoke-ToolCommand -tool $nuget -cmd $command -error_msg $error_msg

Write-Comment -prefix "." -text "Successfully created the Accordant NuGet packages" -color "green"
