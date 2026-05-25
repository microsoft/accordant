# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

function Write-Comment([string]$prefix = "", [string]$text = "", [string]$color = "white") {
    Write-Host "$prefix " -ForegroundColor $color -NoNewline
    Write-Host $text -ForegroundColor $color
}

# Runs the specified tool command.
function Invoke-ToolCommand([String]$tool, [String]$cmd, [String]$error_msg) {
    Write-Host "Invoking $tool $cmd"
    Invoke-Expression "$tool $cmd"
    if (-not ($LASTEXITCODE -eq 0)) {
        Write-Error $error_msg
        exit $LASTEXITCODE
    }
}

# Finds the path of the .NET SDK.
function FindDotNetSdkPath([String]$dotnet) {
    $dotnet_sdks = Invoke-Expression "$dotnet --list-sdks"
    $dotnet_sdk_path = $dotnet_sdks | ForEach-Object {
        $sdk_path = ($_ -split {$_ -eq '[' -or $_ -eq ']'})[1]
        return $sdk_path
    }

    if ($dotnet_sdk_path -is [array]) {
        $dotnet_sdk_path = $dotnet_sdk_path[0]
    }

    return $dotnet_sdk_path
}

# Finds the .NET SDK version from global.json
function FindDotNetSdkVersion([String]$dotnet_sdk_path) {
    $global_json = Join-Path -Path $PSScriptRoot -ChildPath ".." -AdditionalChildPath "global.json"
    if (Test-Path $global_json) {
        $json = Get-Content $global_json | ConvertFrom-Json
        $target_version = $json.sdk.version
        
        $sdks = Get-ChildItem -Path $dotnet_sdk_path -Directory | Where-Object { $_.Name -like "$($target_version.Split('.')[0]).*" }
        if ($sdks.Count -gt 0) {
            return $target_version
        }
    }
    return $null
}

Export-ModuleMember -Function Write-Comment, Invoke-ToolCommand, FindDotNetSdkPath, FindDotNetSdkVersion
