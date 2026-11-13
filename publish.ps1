# Ascolotl Publish Script
# Produces self-contained publish artifacts for Windows, macOS, and Linux.
#
# Usage:
#   .\publish.ps1                       # Build all platforms
#   .\publish.ps1 -Runtime win-x64      # Build for a specific runtime
#
# Prerequisites:
#   - .NET 10.0 SDK (build machine only -- not needed on target)
#
# Runtime dependencies (target machine):
#   - No .NET install or WebView: builds are self-contained; rendering and export are pure-managed via AdocNet.
#   - Linux: X11, fontconfig and ICU (libicu), present on standard desktop distros.
#
# Output: publish/<runtime>/ -- zip or tar to distribute.

param(
    [string]$Runtime = "all",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Project = "src/Ascolotl.App/Ascolotl.App.csproj"
$OutputBase = "publish"

$Runtimes = if ($Runtime -eq "all") {
    @("win-x64", "osx-x64", "osx-arm64", "linux-x64")
} else {
    @($Runtime)
}

# Run tests first
Write-Host "Running tests..." -ForegroundColor Cyan
dotnet test --nologo -v q -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests failed -- aborting publish." -ForegroundColor Red
    exit 1
}
Write-Host "Tests passed." -ForegroundColor Green

foreach ($rid in $Runtimes) {
    Write-Host "`nPublishing for $rid..." -ForegroundColor Cyan
    $outDir = "$OutputBase/$rid"

    dotnet publish $Project `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -o $outDir `
        -p:PublishReadyToRun=true `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed for $rid" -ForegroundColor Red
        exit 1
    }

    Write-Host "Published to $outDir" -ForegroundColor Green
}

Write-Host "`nPublish complete. Artifacts in ./$OutputBase/" -ForegroundColor Green
Write-Host "Zip each runtime folder to distribute." -ForegroundColor DarkGray
