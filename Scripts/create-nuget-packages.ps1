# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

Import-Module $PSScriptRoot/common.psm1 -Force

$dotnet = "dotnet"

Write-Comment -prefix "." -text "Creating the Accordant NuGet packages" -color "yellow"

Write-Comment -prefix "..." -text "Creating the 'Microsoft.Accordant' package"

$command = "pack $PSScriptRoot/../nuget/Microsoft.Accordant.Package.csproj --configuration Release --output $PSScriptRoot/../bin/packages --no-restore"
$error_msg = "Failed to create the Microsoft.Accordant NuGet package"
Invoke-ToolCommand -tool $dotnet -cmd $command -error_msg $error_msg

Write-Comment -prefix "..." -text "Creating the 'Microsoft.Accordant.Cli' package (dotnet tool)"

$command = "pack $PSScriptRoot/../Microsoft.Accordant.Cli/Microsoft.Accordant.Cli.csproj --configuration Release --output $PSScriptRoot/../bin/packages --no-restore"
$error_msg = "Failed to create the Microsoft.Accordant.Cli NuGet package"
Invoke-ToolCommand -tool $dotnet -cmd $command -error_msg $error_msg

Write-Comment -prefix "." -text "Successfully created the Accordant NuGet packages" -color "green"
