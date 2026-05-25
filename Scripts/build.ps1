# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

param(
    [ValidateSet("Debug", "Release")]
    [string]$configuration = "Release",
    [switch]$ci
)

$ScriptDir = $PSScriptRoot

Import-Module $ScriptDir/common.psm1 -Force

Write-Comment -prefix "." -text "Building Accordant" -color "yellow"

if ($host.Version.Major -lt 7) {
    Write-Error "Please use PowerShell v7.x or later (see https://docs.microsoft.com/en-us/powershell/scripting/install/installing-powershell-core-on-windows?view=powershell-7)."
    exit 1
}

# Check that the expected .NET SDK is installed.
$dotnet = "dotnet"
$dotnet_sdk_path = FindDotNetSdkPath -dotnet $dotnet
$sdk_version = FindDotNetSdkVersion -dotnet_sdk_path $dotnet_sdk_path

if ($null -eq $sdk_version) {
    Write-Comment -prefix "..." -text "Warning: Could not verify .NET SDK version from global.json" -color "yellow"
}

Write-Comment -prefix "..." -text "Using configuration '$configuration'"
$solution = Join-Path -Path $ScriptDir -ChildPath ".." -AdditionalChildPath "Accordant.slnx"
$command = "build -c $configuration $solution"

$error_msg = "Failed to build Accordant"
Invoke-ToolCommand -tool $dotnet -cmd $command -error_msg $error_msg

Write-Comment -prefix "." -text "Successfully built Accordant" -color "green"
