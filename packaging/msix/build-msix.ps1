# Builds the Microsoft Store package: one MSIX per architecture (x64, arm64),
# bundled into publish/msix/Ascolotl_<version>.msixbundle for upload to Partner Center.
#
# Usage (identity values from Partner Center > your app > Product identity):
#   .\packaging\msix\build-msix.ps1 -PackageVersion 1.0.0.0 `
#       -IdentityName "12345Publisher.Ascolotl" -Publisher "CN=XXXXXXXX-XXXX-..." `
#       -PublisherDisplayName "Publisher name"
# Without the identity parameters it builds a local test package (Developer Mode
# installs the unpacked layout with: Add-AppxPackage -Register <layout>\AppxManifest.xml).
#
# The Store signs the package itself; this script does not sign.
# Prerequisites: .NET 10 SDK and the Windows 10/11 SDK (makeappx.exe).

param(
    # MSIX version: four parts, the first not 0, the last 0 (Store rules).
    [Parameter(Mandatory)] [string]$PackageVersion,
    [string]$IdentityName = "Ascolotl.LocalTest",
    [string]$Publisher = "CN=AscolotlLocalTest",
    [string]$PublisherDisplayName = "Ascolotl",
    [string[]]$Architectures = @("x64", "arm64")
)

$ErrorActionPreference = "Stop"
if ($PackageVersion -notmatch '^[1-9]\d{0,4}\.\d{1,5}\.\d{1,5}\.0$') {
    throw "PackageVersion must look like 1.2.3.0: four numbers, the first not 0 and the last 0 (Store rule)."
}

$root = Resolve-Path "$PSScriptRoot/../.."
$project = "$root/src/Ascolotl.App/Ascolotl.App.csproj"
$out = "$root/publish/msix"
$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
    Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
if (-not $makeappx) { throw "makeappx.exe not found: install the Windows SDK." }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force "$out/bundle" | Out-Null

foreach ($arch in $Architectures) {
    Write-Host "`nBuilding $arch..." -ForegroundColor Cyan
    $layout = "$out/$arch/layout"

    # publish.ps1's single-file build shares this intermediate folder; reusing its
    # trimmed/ReadyToRun output produced an exe that crashed at startup.
    Remove-Item "$root/src/Ascolotl.App/obj/Release/net10.0/win-$arch" -Recurse -Force -ErrorAction SilentlyContinue

    # Multi-file publish: MSIX installs the files, so no single-file self-extraction.
    dotnet publish $project -c Release -r "win-$arch" --self-contained true -o $layout `
        -p:PublishReadyToRun=true -p:PublishTrimmed=true -p:TrimMode=partial -p:DebugType=none
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $arch" }

    Copy-Item "$PSScriptRoot/Assets" "$layout/Assets" -Recurse
    (Get-Content "$PSScriptRoot/AppxManifest.xml" -Raw) `
        -replace '\$IdentityName\$', $IdentityName `
        -replace '\$Publisher\$', $Publisher `
        -replace '\$PublisherDisplayName\$', $PublisherDisplayName `
        -replace '\$Version\$', $PackageVersion `
        -replace '\$Architecture\$', $arch |
        Set-Content "$layout/AppxManifest.xml" -Encoding utf8

    # Index the icons' 200% and exact-size variants, so Start and the taskbar pick sharp ones.
    $makepri = Join-Path $makeappx.DirectoryName "makepri.exe"
    & $makepri createconfig /cf "$out/$arch/priconfig.xml" /dq en-US /o | Out-Null
    # One index: the default splits scales and languages into resource packs this bundle doesn't build.
    $config = [xml](Get-Content "$out/$arch/priconfig.xml")
    $config.resources.RemoveChild($config.resources.packaging) | Out-Null
    $config.Save("$out/$arch/priconfig.xml")
    & $makepri new /pr $layout /cf "$out/$arch/priconfig.xml" /of "$layout/resources.pri" /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makepri failed for $arch" }

    & $makeappx.FullName pack /d $layout /p "$out/bundle/Ascolotl_${PackageVersion}_$arch.msix" /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed for $arch" }
}

& $makeappx.FullName bundle /d "$out/bundle" /p "$out/Ascolotl_$PackageVersion.msixbundle" /bv $PackageVersion /o
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed" }

Write-Host "`nStore upload: $out/Ascolotl_$PackageVersion.msixbundle" -ForegroundColor Green
