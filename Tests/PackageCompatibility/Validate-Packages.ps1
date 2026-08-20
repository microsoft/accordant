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
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & dotnet @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed:`n$output"
    }

    return $output
}

function Assert-PackageDependencies {
    param(
        [Parameter(Mandatory)]
        [string] $PackagePath,

        [Parameter(Mandatory)]
        [string] $TargetFramework,

        [Parameter(Mandatory)]
        [hashtable] $ExpectedPackages
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecEntry = $archive.Entries |
            Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try {
            [xml] $packageNuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $group = $packageNuspec.SelectNodes("//*[local-name()='group']") |
        Where-Object { $_.targetFramework -eq $TargetFramework } |
        Select-Object -First 1

    if ($null -eq $group) {
        throw "Package dependency group $TargetFramework was not found in $PackagePath."
    }

    foreach ($package in $ExpectedPackages.GetEnumerator()) {
        $dependency = $group.SelectNodes("*[local-name()='dependency']") |
            Where-Object { $_.id -eq $package.Key } |
            Select-Object -First 1

        if ($null -eq $dependency -or $dependency.version -ne $package.Value) {
            throw "Expected $($package.Key) $($package.Value) in the $TargetFramework dependency group."
        }
    }
}

function Assert-AccordantAsset {
    param(
        [Parameter(Mandatory)]
        [string] $AssetsPath,

        [Parameter(Mandatory)]
        [string] $PackageVersion,

        [Parameter(Mandatory)]
        [string] $TargetFramework
    )

    $assets = Get-Content $AssetsPath -Raw | ConvertFrom-Json
    $target = $assets.targets.PSObject.Properties | Select-Object -First 1 -ExpandProperty Value
    $accordant = $target.PSObject.Properties |
        Where-Object { $_.Name -eq "Microsoft.Accordant/$PackageVersion" } |
        Select-Object -First 1 -ExpandProperty Value
    $compileAssets = @($accordant.compile.PSObject.Properties.Name)

    if ($compileAssets.Count -eq 0 -or
        $compileAssets.Where({ -not $_.StartsWith("lib/$TargetFramework/", [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw "Expected Microsoft.Accordant $PackageVersion to select only $TargetFramework compile assets."
    }
}

try {
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
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

    [xml] $nuspec = Get-Content $nuspecPath
    $packageVersion = [string] $nuspec.package.metadata.version

    Invoke-DotNet @(
        "pack",
        $packageProject,
        "--configuration", "Release",
        "--output", $packageDirectory,
        "--nologo",
        "--verbosity", "minimal"
    ) | Out-Host
    $packagePath = Join-Path $packageDirectory "Microsoft.Accordant.$packageVersion.nupkg"

    $consumerCases = @(
        @{
            Name = "Net8Consumer"
            TargetFramework = "net8.0"
            Packages = @{
                "System.Collections.Immutable" = "8.0.0"
                "System.IO.Hashing" = "8.0.0"
                "System.Text.Json" = "8.0.5"
            }
        },
        @{
            Name = "Net10Consumer"
            TargetFramework = "net10.0"
            Packages = @{
                "System.Collections.Immutable" = "10.0.0"
                "System.IO.Hashing" = "10.0.3"
                "System.Text.Json" = "10.0.3"
            }
        }
    )

    foreach ($case in $consumerCases) {
        Assert-PackageDependencies `
            -PackagePath $packagePath `
            -TargetFramework $case.TargetFramework `
            -ExpectedPackages $case.Packages

        $sourceDirectory = Join-Path $PSScriptRoot "Consumers\$($case.Name)"
        $consumerDirectory = Join-Path $tempRoot $case.Name
        New-Item -ItemType Directory -Path $consumerDirectory -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceDirectory "*") -Destination $consumerDirectory -Recurse

        $consumerProject = Get-ChildItem $consumerDirectory -Filter "*.csproj" | Select-Object -First 1 -ExpandProperty FullName
        Invoke-DotNet @(
            "restore",
            $consumerProject,
            "--packages", $globalPackagesDirectory,
            "--configfile", $nugetConfigPath,
            "--force",
            "--no-cache",
            "--ignore-failed-sources",
            "--nologo",
            "--verbosity", "minimal",
            "-p:AccordantPackageVersion=$packageVersion"
        ) | Out-Host

        $buildOutput = Invoke-DotNet @(
            "build",
            $consumerProject,
            "--configuration", "Release",
            "--no-restore",
            "--nologo",
            "--verbosity", "minimal",
            "-warnaserror:MSB3277",
            "-p:AccordantPackageVersion=$packageVersion"
        )

        if ($buildOutput -match "\bMSB3277\b") {
            throw "$($case.Name) emitted an MSB3277 assembly conflict:`n$buildOutput"
        }

        $assetsPath = Join-Path $consumerDirectory "obj\project.assets.json"
        Assert-AccordantAsset `
            -AssetsPath $assetsPath `
            -PackageVersion $packageVersion `
            -TargetFramework $case.TargetFramework

        Write-Host "$($case.Name) resolved the expected package train without MSB3277."
    }
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force
    }
}
