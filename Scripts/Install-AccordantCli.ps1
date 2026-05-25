# Install-AccordantCli.ps1
# Installs (or reinstalls) the Microsoft.Accordant.Cli tool locally for development
# Uses the nuspec file from nuget/ folder

param(
    [switch]$Global = $true
)

$ErrorActionPreference = "Stop"

$toolName = "Microsoft.Accordant.Cli"
$projectPath = (Resolve-Path "$PSScriptRoot\..\Microsoft.Accordant.Cli\Microsoft.Accordant.Cli.csproj").Path
$nuspecPath = (Resolve-Path "$PSScriptRoot\..\nuget\Microsoft.Accordant.Cli.nuspec").Path
$nupkgPath = "$PSScriptRoot\..\bin\packages"

# Extract version from nuspec
[xml]$nuspec = Get-Content $nuspecPath
$version = $nuspec.package.metadata.version

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

# Pack using nuspec (catches target framework mismatches)
Write-Host ""
Write-Host "Packing with nuspec (validates target framework coverage)..." -ForegroundColor Yellow

# Remove any existing nupkg to avoid file locking issues
Remove-Item "$nupkgPath\$toolName.*.nupkg" -Force -ErrorAction SilentlyContinue

# Use nuget pack with the nuspec file
$nugetExe = Get-Command nuget -ErrorAction SilentlyContinue
if ($nugetExe) {
    nuget pack $nuspecPath -OutputDirectory $nupkgPath
} else {
    # Fallback: use dotnet pack with nuspec
    dotnet pack $projectPath -c Release -o $nupkgPath -p:NuspecFile=$nuspecPath /m:1
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "Pack failed! Check if nuspec matches csproj TargetFrameworks." -ForegroundColor Red
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
