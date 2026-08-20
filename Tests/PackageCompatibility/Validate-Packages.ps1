# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [string] $NuGetSource = "https://api.nuget.org/v3/index.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$nuspecPath = Join-Path $repositoryRoot "nuget\Microsoft.Accordant.nuspec"
$packageProject = Join-Path $repositoryRoot "nuget\Microsoft.Accordant.Package.csproj"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "accordant-package-compatibility-$([Guid]::NewGuid().ToString('N'))"
$packageDirectory = Join-Path $tempRoot "packages"
$globalPackagesDirectory = Join-Path $tempRoot "global-packages"
$nugetConfigPath = Join-Path $tempRoot "NuGet.config"

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = & dotnet @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed:`n$output"
    }

    return $output
}

try {
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

    [xml] $nuspec = Get-Content $nuspecPath
    $packageVersion = [string] $nuspec.package.metadata.version

    Invoke-DotNet @(
        "pack", $packageProject,
        "--configuration", "Release",
        "--output", $packageDirectory,
        "--nologo",
        "--verbosity", "minimal"
    ) | Out-Host

    $escapedPackageDirectory = [System.Security.SecurityElement]::Escape($packageDirectory)
    $escapedNuGetSource = [System.Security.SecurityElement]::Escape($NuGetSource)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="package-under-test" value="$escapedPackageDirectory" />
    <add key="nuget.org" value="$escapedNuGetSource" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="package-under-test">
      <package pattern="Microsoft.Accordant" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="System.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content $nugetConfigPath -Encoding utf8

    foreach ($targetFramework in "net8.0", "net10.0") {
        $consumerDirectory = Join-Path $tempRoot $targetFramework
        $consumerProject = Join-Path $consumerDirectory "Consumer.csproj"
        New-Item -ItemType Directory -Path $consumerDirectory -Force | Out-Null

        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$targetFramework</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Accordant" Version="$packageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content $consumerProject -Encoding utf8

        'public static class Consumer { public static System.Type AccordantType => typeof(Microsoft.Accordant.StateAttribute); }' |
            Set-Content (Join-Path $consumerDirectory "Consumer.cs") -Encoding utf8

        Invoke-DotNet @(
            "restore", $consumerProject,
            "--packages", $globalPackagesDirectory,
            "--configfile", $nugetConfigPath,
            "--force",
            "--no-cache",
            "--ignore-failed-sources",
            "--nologo",
            "--verbosity", "minimal"
        ) | Out-Host

        $buildOutput = Invoke-DotNet @(
            "build", $consumerProject,
            "--configuration", "Release",
            "--no-restore",
            "--nologo",
            "--verbosity", "minimal",
            "-warnaserror:MSB3277"
        )

        if ($buildOutput -match "\bMSB3277\b") {
            throw "$targetFramework emitted an MSB3277 assembly conflict:`n$buildOutput"
        }

        $assets = Get-Content (Join-Path $consumerDirectory "obj\project.assets.json") -Raw | ConvertFrom-Json
        $target = $assets.targets.PSObject.Properties | Select-Object -First 1 -ExpandProperty Value
        $accordant = $target.PSObject.Properties |
            Where-Object Name -eq "Microsoft.Accordant/$packageVersion" |
            Select-Object -First 1 -ExpandProperty Value
        $compileAssets = @($accordant.compile.PSObject.Properties.Name)

        if ($compileAssets.Count -eq 0 -or
            $compileAssets.Where({ -not $_.StartsWith("lib/$targetFramework/", [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            throw "Microsoft.Accordant $packageVersion did not select its $targetFramework assets."
        }

        Write-Host "$targetFramework selected its package assets without MSB3277."
    }
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force
    }
}
