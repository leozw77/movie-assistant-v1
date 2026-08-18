param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-FileHash([string]$Name, [string]$Path, [string]$Expected) {
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "$Name is missing: $Path"
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    Assert-True ($actual -eq $Expected.ToUpperInvariant()) "$Name SHA256 mismatch. Expected '$Expected', actual '$actual'."
}

$sourceFull = Resolve-FullPath $SourceRoot
$devRoot = Split-Path -Parent $sourceFull
$projectRoot = Split-Path -Parent $devRoot
$baselinePath = Join-Path $devRoot 'DEVELOPMENT_BASELINE.json'
Assert-True (Test-Path -LiteralPath $baselinePath -PathType Leaf) "Baseline manifest is missing: $baselinePath"
$baseline = Get-Content -LiteralPath $baselinePath -Raw -Encoding UTF8 | ConvertFrom-Json

$releaseName = [string]$baseline.release.releaseName
$packageRoot = Resolve-FullPath $baseline.canonicalStable.packageRoot
$packageName = Split-Path -Leaf $packageRoot
$zipPath = Resolve-FullPath $baseline.canonicalStable.zipPath
$zipName = Split-Path -Leaf $zipPath

Assert-True ($releaseName -eq $packageName) "Release manifest name and package directory name differ."
Assert-True ($zipName -eq "$releaseName.zip") "ZIP name does not match the formal release name."
Assert-True ($releaseName -match '^观影助手-v\d+\.\d+\.\d+-.+-[0-9a-f]{7,40}-net8轻量版$') "Release name does not follow the formal naming rule: $releaseName"
Assert-True ($packageRoot.StartsWith((Join-Path $projectRoot '发布版本'), [System.StringComparison]::OrdinalIgnoreCase)) "Stable package is not under 发布版本."
Assert-True ($zipPath.StartsWith((Join-Path $projectRoot '发布版本'), [System.StringComparison]::OrdinalIgnoreCase)) "Stable ZIP is not under 发布版本."

Assert-FileHash 'Stable EXE' (Resolve-FullPath $baseline.canonicalStable.exePath) $baseline.canonicalStable.exeSha256
Assert-FileHash 'Stable ZIP' $zipPath $baseline.canonicalStable.zipSha256
Assert-FileHash 'Rollback EXE' (Resolve-FullPath $baseline.rollback.exePath) $baseline.rollback.exeSha256

$packageVersionPath = Join-Path $packageRoot 'VERSION.json'
Assert-True (Test-Path -LiteralPath $packageVersionPath -PathType Leaf) "Package VERSION.json is missing."
$packageVersion = Get-Content -LiteralPath $packageVersionPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-True ([string]$packageVersion.version -eq [string]$baseline.release.version) "Package version does not match the release version."
Assert-True ([string]$packageVersion.status -eq 'stable') "Package status is not stable."
Assert-True ([string]$packageVersion.sha256.ToUpperInvariant() -eq [string]$baseline.canonicalStable.exeSha256.ToUpperInvariant()) "Package VERSION.json EXE hash does not match the baseline."

$releaseManifestPath = Resolve-FullPath $baseline.release.releaseManifest
Assert-True (Test-Path -LiteralPath $releaseManifestPath -PathType Leaf) "Release manifest is missing: $releaseManifestPath"
$releaseManifest = Get-Content -LiteralPath $releaseManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-True ([string]$releaseManifest.releaseName -eq $releaseName) "Release manifest releaseName mismatch."
Assert-True ([string]$releaseManifest.exeSha256.ToUpperInvariant() -eq [string]$baseline.canonicalStable.exeSha256.ToUpperInvariant()) "Release manifest EXE hash mismatch."
Assert-True ([string]$releaseManifest.zipSha256.ToUpperInvariant() -eq [string]$baseline.canonicalStable.zipSha256.ToUpperInvariant()) "Release manifest ZIP hash mismatch."
Assert-True ([string]$releaseManifest.rollbackExeSha256.ToUpperInvariant() -eq [string]$baseline.rollback.exeSha256.ToUpperInvariant()) "Release manifest rollback hash mismatch."

$artifactsRoot = Join-Path $sourceFull 'artifacts'
if (Test-Path -LiteralPath $artifactsRoot -PathType Container) {
    Assert-True (@(Get-ChildItem -Force -LiteralPath $artifactsRoot).Count -eq 0) "Development artifacts directory is not empty."
}

$governanceFiles = @(
    (Join-Path $devRoot 'DEVELOPMENT_BASELINE.json'),
    (Join-Path $devRoot 'DEVELOPMENT_DIRECTORY_INDEX.md'),
    (Join-Path $sourceFull 'STABLE_PROMOTION_20260816.md')
)
foreach ($file in $governanceFiles) {
    Assert-True (Test-Path -LiteralPath $file -PathType Leaf) "Governance file is missing: $file"
    $text = Get-Content -LiteralPath $file -Raw -Encoding UTF8
    Assert-True ($text -notmatch 'search-stable-manual-pagination-20260816') "Old temporary release name remains in $file"
}

Write-Output 'RELEASE LAYOUT PASS'
Write-Output "Release: $releaseName"
Write-Output "Stable EXE: $($baseline.canonicalStable.exePath)"
Write-Output "Stable ZIP: $($baseline.canonicalStable.zipPath)"
Write-Output "Release manifest: $($baseline.release.releaseManifest)"
