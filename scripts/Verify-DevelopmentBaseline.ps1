param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-FileHash([string]$Name, [string]$Path, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Name is missing: $Path"
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $Expected.ToUpperInvariant()) {
        throw "$Name SHA256 mismatch. Expected '$Expected', actual '$actual'."
    }
}

$sourceFull = Resolve-FullPath $SourceRoot
$devRoot = Split-Path -Parent $sourceFull
$manifestPath = Join-Path $devRoot 'DEVELOPMENT_BASELINE.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Development baseline manifest is missing: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

$canonicalSource = Resolve-FullPath $manifest.pairedSource.root
$activeSource = Resolve-FullPath $manifest.activeDevelopment.root
$allowedSources = @($canonicalSource, $activeSource) | Select-Object -Unique
if (-not ($allowedSources | Where-Object { [string]::Equals($sourceFull, $_, [System.StringComparison]::OrdinalIgnoreCase) })) {
    throw "Source root mismatch. Expected canonical or registered active source, actual '$sourceFull'."
}

Assert-FileHash 'Canonical stable EXE' (Resolve-FullPath $manifest.canonicalStable.exePath) $manifest.canonicalStable.exeSha256
if ($manifest.canonicalStable.zipPath -and $manifest.canonicalStable.zipSha256) {
    Assert-FileHash 'Canonical stable ZIP' (Resolve-FullPath $manifest.canonicalStable.zipPath) $manifest.canonicalStable.zipSha256
}
if ($manifest.rollback -and $manifest.rollback.exePath -and $manifest.rollback.exeSha256) {
    Assert-FileHash 'Rollback EXE' (Resolve-FullPath $manifest.rollback.exePath) $manifest.rollback.exeSha256
}

$promotionRecord = Resolve-FullPath $manifest.canonicalStable.promotionRecord
if (-not (Test-Path -LiteralPath $promotionRecord -PathType Leaf)) {
    throw "Stable promotion record is missing: $promotionRecord"
}
$promotionText = Get-Content -LiteralPath $promotionRecord -Raw -Encoding UTF8
if ($promotionText -notmatch [regex]::Escape($canonicalSource)) {
    throw "Stable promotion record does not bind the canonical paired source: $canonicalSource"
}
if ($promotionText -notmatch [regex]::Escape($manifest.canonicalStable.exeSha256)) {
    throw 'Stable promotion record does not contain the canonical stable EXE SHA256.'
}

$stablePackage = Resolve-FullPath $manifest.canonicalStable.packageRoot
if (-not (Test-Path -LiteralPath $stablePackage -PathType Container)) {
    throw "Canonical stable package directory is missing: $stablePackage"
}

foreach ($property in $manifest.pairedSource.stableAssetHashes.PSObject.Properties) {
    Assert-FileHash "Stable source asset $($property.Name)" (Join-Path $canonicalSource $property.Name) $property.Value
}

Write-Output 'BASELINE PASS'
Write-Output "Stable EXE: $($manifest.canonicalStable.exePath)"
Write-Output "Paired source: $canonicalSource"
if ($manifest.rollback -and $manifest.rollback.exePath) {
    Write-Output "Rollback EXE: $($manifest.rollback.exePath)"
}
else {
    Write-Output 'Rollback EXE: none; clean v1.0.0 baseline'
}
