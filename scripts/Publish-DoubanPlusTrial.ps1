param(
    [string]$DeliveryName = "search-page-adapt-20260812"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$projectFile = Get-ChildItem -LiteralPath $root -Filter "*.csproj" -File | Select-Object -First 1
if (-not $projectFile) { throw "No project file found in $root." }
$project = $projectFile.FullName
$output = Join-Path $root ("artifacts\{0}-v1.0-{1}-win-x64" -f $projectFile.BaseName, $DeliveryName)
$archive = "$output.zip"

if ($DeliveryName -notmatch '^[A-Za-z0-9._-]+$') {
    throw "DeliveryName may contain only letters, numbers, dot, underscore and hyphen."
}
if (Test-Path -LiteralPath $output) {
    throw "Delivery directory already exists and must not be overwritten: $output"
}
if (Test-Path -LiteralPath $archive) {
    throw "Delivery archive already exists and must not be overwritten: $archive"
}

dotnet publish $project -c Release -r win-x64 --self-contained false --no-restore -p:ContinuousIntegrationBuild=true -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$copyFiles = @(
    "VERSION",
    "PACKAGE_VERSION.txt",
    "STABLE_VERSION_V0.9.0.json",
    "README.md",
    "CHANGELOG.md",
    "BUILD_STATUS.txt",
    "BUILD_REQUIRED.txt",
    "AI_HANDOFF.md",
    "docs\STATUS.md",
    "docs\CURRENT_ARCHITECTURE.md",
    "review\SEARCH_PAGE_ADAPTATION_20260812.md"
)
foreach ($relative in $copyFiles) {
    $source = Join-Path $root $relative
    if (Test-Path -LiteralPath $source) {
        $destination = Join-Path $output (Split-Path -Leaf $relative)
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}

$exe = Get-ChildItem -LiteralPath $output -Filter "*.exe" -File | Sort-Object Length -Descending | Select-Object -First 1
if (-not $exe) { throw "No EXE was generated in $output." }

$reviewResult = Join-Path $output "review-self-test-result.txt"
$reviewProcess = Start-Process -FilePath $exe.FullName -ArgumentList "--review-self-test" -WorkingDirectory $output -Wait -PassThru
if ($reviewProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reviewResult)) {
    throw "Published review self-test failed or produced no result file. ExitCode=$($reviewProcess.ExitCode)"
}

$selfResult = Join-Path $output "self-test-result.txt"
$selfProcess = Start-Process -FilePath $exe.FullName -ArgumentList "--self-test" -WorkingDirectory $output -Wait -PassThru
if ($selfProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $selfResult)) {
    throw "Published self-test failed or produced no result file. ExitCode=$($selfProcess.ExitCode)"
}

$exeHash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
@{
    product = "Movie Assistant"
    status = "independent-trial"
    version = "1.0"
    deliveryName = $DeliveryName
    generated = (Get-Date -Format o)
    executable = $exe.Name
    sha256 = $exeHash
    sourceBaseline = "v0.9.0 BuildFix12 R11"
    scope = "Douban Plus search-page adaptation; local library view hidden during Douban navigation; legacy return fallback disabled"
    formalReleaseImpact = "formal v0.9.0 unchanged"
    policy = "immutable-delivery-directory"
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output "VERSION.json") -Encoding UTF8

@(
    "Movie Assistant v1.0 independent trial - Douban Plus search page adaptation",
    "",
    "Scope: hide the local library view during Douban navigation; adapt the dark search page; render result cards, posters, metadata, pagination and subject-detail links; keep list-page and detail-page return flows inside the new Douban Plus surface.",
    "Not in scope: watchlist button, context menu and search-result status writes.",
    "",
    "Formal v0.9.0 release directory, EXE, ZIP and SHA-256 remain unchanged.",
    "Runtime: Windows x64, .NET 8 Desktop Runtime x64 and Microsoft Edge WebView2 Evergreen Runtime."
) | Set-Content -LiteralPath (Join-Path $output ("{0}{1}{2}{3}.txt" -f ([char]0x7248), ([char]0x672C), ([char]0x8BF4), ([char]0x660E))) -Encoding UTF8

$hashLines = Get-ChildItem -LiteralPath $output -File -Recurse |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($output.Length).TrimStart('\')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
$hashLines | Set-Content -LiteralPath (Join-Path $output "SHA256SUMS.txt") -Encoding UTF8

Compress-Archive -LiteralPath $output -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Output: $output"
Write-Host "Executable: $($exe.FullName)"
Write-Host "Archive: $archive"
