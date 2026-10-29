# Install-AccordantCli.ps1
# Installs (or reinstalls) the Microsoft.Accordant.Cli tool locally for development

param(
    [switch]$Global = $true
)

$ErrorActionPreference = "Stop"

$toolName = "Microsoft.Accordant.Cli"
$projectPath = (Resolve-Path "$PSScriptRoot\..\Microsoft.Accordant.Cli\Microsoft.Accordant.Cli.csproj").Path
$nupkgPath = "$PSScriptRoot\..\bin\packages"

$version = dotnet msbuild $projectPath -getProperty:Version -nologo

Write-Host "=== Accordant CLI Installer ===" -ForegroundColor Cyan
Write-Host ""

# Check if tool is already installed
Write-Host "Checking for existing installation..." -ForegroundColor Yellow
$installed = dotnet tool list --global | Select-String -Pattern "microsoft.accordant.cli"

if ($installed) {
    Write-Host "Found existing installation. Uninstalling..." -ForegroundColor Yellow
    dotnet tool uninstall --global $toolName
    Write-Host "Uninstalled." -ForegroundColor Green
}

# Build the tool (creates binaries for all target frameworks)
Write-Host ""
Write-Host "Building..." -ForegroundColor Yellow
dotnet build $projectPath -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Create packages output directory if it doesn't exist
if (-not (Test-Path $nupkgPath)) {
    New-Item -ItemType Directory -Path $nupkgPath -Force | Out-Null
}

# Convert to absolute path for dotnet tool install
$nupkgPath = (Resolve-Path $nupkgPath).Path

Write-Host ""
Write-Host "Packing..." -ForegroundColor Yellow

# Remove any existing nupkg to avoid file locking issues
Remove-Item "$nupkgPath\$toolName.*.nupkg" -Force -ErrorAction SilentlyContinue

dotnet pack $projectPath -c Release -o $nupkgPath --no-build

if ($LASTEXITCODE -ne 0) {
    Write-Host "Pack failed!" -ForegroundColor Red
    exit 1
}

# Install the tool globally
Write-Host ""
Write-Host "Installing Accordant CLI tool globally..." -ForegroundColor Yellow

# Use --version to explicitly specify version and avoid cache issues
dotnet tool install --global --add-source $nupkgPath $toolName --version $version --ignore-failed-sources

if ($LASTEXITCODE -ne 0) {
    Write-Host "Installation failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Installation Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "You can now use 'accordant' from anywhere:" -ForegroundColor Cyan
Write-Host "  accordant new <name>    - Create a new Accordant project"
Write-Host "  accordant --help        - Show all commands"
Write-Host ""
