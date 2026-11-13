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
# Output: publish/<runtime>/Ascolotl[.exe] -- one self-contained, trimmed, ReadyToRun
# executable per platform (native libraries included; no .pdb) -- and release archives
# in publish/dist/: Ascolotl-<version>-<runtime>.zip (Windows) or .tar.gz (macOS, Linux;
# the executable bit is kept).

param(
    [string]$Runtime = "all",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Project = "src/Ascolotl.App/Ascolotl.App.csproj"
$OutputBase = "publish"
$DistDir = "$OutputBase/dist"
$Version = ([xml](Get-Content $Project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

$Runtimes = if ($Runtime -eq "all") {
    @("win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64")
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
    # Start clean: a previous multi-file publish would otherwise linger beside the single executable.
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    dotnet publish $Project `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -o $outDir `
        -p:PublishReadyToRun=true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=true `
        -p:TrimMode=partial `
        -p:DebugType=none

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed for $rid" -ForegroundColor Red
        exit 1
    }

    Write-Host "Published to $outDir" -ForegroundColor Green

    New-Item -ItemType Directory -Force $DistDir | Out-Null
    $archive = "$DistDir/Ascolotl-$Version-$rid" + $(if ($rid -like "win-*") { ".zip" } else { ".tar.gz" })
    dotnet run tools/package/pack.cs -- $outDir $archive
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Packaging failed for $rid" -ForegroundColor Red
        exit 1
    }
    Write-Host "Packaged $archive" -ForegroundColor Green
}

Write-Host "`nPublish complete. Artifacts in ./$OutputBase/" -ForegroundColor Green
Write-Host "Release archives in ./$DistDir/" -ForegroundColor DarkGray
