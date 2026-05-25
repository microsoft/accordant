# Publish-Local.ps1
# Builds and packs Microsoft.Accordant to the local NuGet feed for sample development
# Run this after making changes to Accordant libraries before testing samples

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$nuspecPath = "$repoRoot\nuget\Microsoft.Accordant.nuspec"
$nupkgPath = "$repoRoot\bin\packages"

# Extract version from nuspec
[xml]$nuspec = Get-Content $nuspecPath
$version = $nuspec.package.metadata.version

Write-Host "=== Accordant Local Publisher ===" -ForegroundColor Cyan
Write-Host "Version: $version"
Write-Host ""

# Build the solution (puts binaries in bin/)
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build "$repoRoot\Accordant.slnx" -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build complete." -ForegroundColor Green
Write-Host ""

# Create packages output directory if it doesn't exist
if (-not (Test-Path $nupkgPath)) {
    New-Item -ItemType Directory -Path $nupkgPath -Force | Out-Null
}

# Remove any existing package to avoid issues
Remove-Item "$nupkgPath\Microsoft.Accordant.*.nupkg" -Force -ErrorAction SilentlyContinue

# Pack using nuspec (matches official pipeline)
Write-Host "Packing Microsoft.Accordant..." -ForegroundColor Yellow
Push-Location $repoRoot
nuget pack nuget\Microsoft.Accordant.nuspec -OutputDirectory $nupkgPath -NoPackageAnalysis
Pop-Location

if ($LASTEXITCODE -ne 0) {
    Write-Host "Pack failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Publish Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Package published to: $nupkgPath" -ForegroundColor Cyan
Write-Host "Version: $version" -ForegroundColor Cyan
Write-Host ""
Write-Host "Samples can now build against the local package." -ForegroundColor Gray
Write-Host "To force NuGet to pick up the new package, you may need to:" -ForegroundColor Gray
Write-Host "  dotnet nuget locals all --clear" -ForegroundColor Gray
Write-Host ""
