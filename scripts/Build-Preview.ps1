param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = Split-Path -Parent $PSScriptRoot
$baselineVerifier = Join-Path $PSScriptRoot "Verify-DevelopmentBaseline.ps1"
if (-not (Test-Path -LiteralPath $baselineVerifier -PathType Leaf)) { throw "Development baseline verifier was not found: $baselineVerifier" }
& $baselineVerifier -SourceRoot $root
$logPath = Join-Path $root "build-preview.log"
$transcriptStarted = $false
    $deliveryName = "unified-shell-stage1-$(Get-Date -Format yyyyMMdd-HHmmss)"

function Invoke-Checked {
    param(
        [string]$Label,
        [scriptblock]$Action
    )
    Write-Host $Label
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

try {
    try {
        Start-Transcript -Path $logPath -Force | Out-Null
        $transcriptStarted = $true
    }
    catch {
        # Building must continue even when transcript is unavailable.
    }

    Write-Host "Movie Assistant v1.0.0 Douban Plus clean baseline build"
    Write-Host "Formal v0.9.0 BuildFix12 R11 baseline remains unchanged"
    Write-Host "Legacy validation contract: full-source BuildFix12 build; BuildFix12 delete-v2"
    Write-Host "Project root: $root"

    $projects = @(Get-ChildItem -LiteralPath $root -Filter "*.csproj" -File)
    if ($projects.Count -ne 1) {
        throw "Expected exactly one .csproj file in the project root; found $($projects.Count)."
    }
    $project = $projects[0].FullName
    $nugetConfig = Join-Path $root "NuGet.Config"
    if (-not (Test-Path -LiteralPath $nugetConfig)) {
        throw "NuGet.Config was not found: $nugetConfig"
    }

    $versionFile = Join-Path $root "VERSION"
    if (-not (Test-Path -LiteralPath $versionFile)) {
        throw "VERSION file was not found."
    }
    $version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "VERSION file is empty."
    }

    $projectText = Get-Content -LiteralPath $project -Raw
    if ($projectText -notmatch [regex]::Escape("<Version>$version</Version>")) {
        throw "VERSION and csproj Version do not match."
    }
    $crashLogger = Get-Content -LiteralPath (Join-Path $root "CrashLogger.cs") -Raw
    if ($crashLogger -notmatch [regex]::Escape("public const string Version = `"$version`";")) {
        throw "VERSION and AppInfo.Version do not match."
    }

    $output = Join-Path $root ("artifacts\{0}-v{1}-{2}-win-x64" -f $projects[0].BaseName, $version, $deliveryName)
    $archive = Join-Path $root ("artifacts\{0}-v{1}-{2}-win-x64.zip" -f $projects[0].BaseName, $version, $deliveryName)
    if (Test-Path -LiteralPath $output) {
        throw "Delivery directory already exists and must not be overwritten: $output"
    }
    if (Test-Path -LiteralPath $archive) {
        throw "Delivery archive already exists and must not be overwritten: $archive"
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw ".NET SDK was not found. Install the .NET 8 SDK, then run BUILD_PREVIEW.cmd again."
    }

    $sdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw "Unable to read the installed .NET SDK version."
    }
    $majorText = ($sdkVersion -split '\.')[0]
    $major = 0
    if (-not [int]::TryParse($majorText, [ref]$major) -or $major -lt 8) {
        throw "Installed .NET SDK is $sdkVersion. .NET 8 SDK or newer is required."
    }

    Write-Host "[1/8] Running source/protocol validation..."

    # Python validation is optional. Windows may expose a Microsoft Store
    # app-execution alias named python.exe even when Python is not installed.
    # Get-Command sees that alias, but launching it returns exit code 9009.
    # Probe each candidate before using it and skip the optional check when
    # none of them can actually run.
    $pythonRunner = $null
    foreach ($candidate in @(
        [pscustomobject]@{ Command = "py"; Prefix = @("-3") },
        [pscustomobject]@{ Command = "python"; Prefix = @() },
        [pscustomobject]@{ Command = "python3"; Prefix = @() }
    )) {
        $resolved = Get-Command $candidate.Command -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if (-not $resolved) {
            continue
        }

        # Ignore the Windows Store placeholder aliases. They are visible to
        # Get-Command but are not usable Python runtimes and commonly return
        # process exit code 9009.
        if ($resolved.Path -match '\\Microsoft\\WindowsApps\\python(?:3)?\.exe$') {
            continue
        }

        try {
            $probeArgs = @($candidate.Prefix) + @("--version")
            & $resolved.Path @probeArgs *> $null
            if ($LASTEXITCODE -eq 0) {
                if ($candidate.Prefix.Count -gt 0) {
                    $displayName = "$($candidate.Command) $($candidate.Prefix -join ' ')"
                }
                else {
                    $displayName = $candidate.Command
                }
                $pythonRunner = [pscustomobject]@{
                    Path = $resolved.Path
                    Prefix = @($candidate.Prefix)
                    Display = $displayName
                }
                break
            }
        }
        catch {
            # Try the next candidate. Python is not required for compilation.
        }
    }

    if ($pythonRunner) {
        $validationScript = Join-Path $root "tests\validate_douban_plus_only.py"
        $validationArgs = @($pythonRunner.Prefix) + @($validationScript)
        Write-Host "Using Python runner: $($pythonRunner.Display)"
        Invoke-Checked "Python Douban Plus-only validation" { & $pythonRunner.Path @validationArgs }
        $exploreValidationScript = Join-Path $root "tests\validate_douban_explore.py"
        $exploreValidationArgs = @($pythonRunner.Prefix) + @($exploreValidationScript)
        Invoke-Checked "Python Douban Explore validation" { & $pythonRunner.Path @exploreValidationArgs }
        $shellValidationScript = Join-Path $root "tests\validate_douban_shell.py"
        $shellValidationArgs = @($pythonRunner.Prefix) + @($shellValidationScript)
        Invoke-Checked "Python unified Shell validation" { & $pythonRunner.Path @shellValidationArgs }
        $sourceValidationScript = Join-Path $root "tests\validate_douban_source_bridge.py"
        $sourceValidationArgs = @($pythonRunner.Prefix) + @($sourceValidationScript)
        Invoke-Checked "Python DOM Source bridge validation" { & $pythonRunner.Path @sourceValidationArgs }
    }
    else {
        Write-Warning "A usable Python runtime was not found; optional Python validation was skipped. The .NET build will continue."
    }
    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($node) {
        Invoke-Checked "Douban shared card JavaScript syntax validation" { & node --check (Join-Path $root "WebAssets\DoubanPlus\douban-card.js") }
        Invoke-Checked "Douban country dictionary JavaScript syntax validation" { & node --check (Join-Path $root "WebAssets\DoubanPlus\douban-country-labels.js") }
        Invoke-Checked "Douban Plus JavaScript syntax validation" { & node --check (Join-Path $root "WebAssets\DoubanPlus\douban-plus.user.js") }
        Invoke-Checked "Douban Explore JavaScript syntax validation" { & node --check (Join-Path $root "WebAssets\DoubanPlus\douban-explore-page.js") }
        Invoke-Checked "Unified Shell JavaScript syntax validation" { & node --check (Join-Path $root "WebAssets\DoubanPlus\douban-shell.js") }
        Invoke-Checked "DOM Source bridge JavaScript syntax validation" { & node --check (Join-Path $root "WebAssets\DoubanPlus\douban-source-bridge.js") }
        Invoke-Checked "Personal Source bridge JavaScript syntax validation" { & node --check (Join-Path $root "WebAssets\DoubanPlus\douban-personal-source-bridge.js") }
        Invoke-Checked "Personal page JavaScript syntax validation" { & node --check (Join-Path $root "WebAssets\DoubanPlus\douban-personal-page.js") }
        if ($pythonRunner) {
            $embeddedScriptValidation = Join-Path $root "tests\validate_embedded_scripts.py"
            $embeddedArgs = @($pythonRunner.Prefix) + @($embeddedScriptValidation)
            Invoke-Checked "Embedded Douban JavaScript validation" { & $pythonRunner.Path @embeddedArgs }
        }
    }
    else {
        Write-Warning "Node.js was not found; JavaScript checks were skipped."
    }

    # This machine's user-level NuGet.Config is not readable. Keep the
    # preview build self-contained by redirecting NuGet's user config lookup
    # to a writable directory under this isolated development copy.
    $buildAppData = Join-Path $root ".build-appdata"
    $buildNuGetDirectory = Join-Path $buildAppData "NuGet"
    New-Item -ItemType Directory -Force -Path $buildNuGetDirectory | Out-Null
    $previousAppData = $env:APPDATA
    $env:APPDATA = $buildAppData
    try {
        Invoke-Checked "[2/8] Restoring NuGet packages..." { & dotnet restore $project -r win-x64 --configfile $nugetConfig }
    }
    finally {
        $env:APPDATA = $previousAppData
    }
    Invoke-Checked "[3/8] Building $Configuration..." { & dotnet build $project -c $Configuration -r win-x64 --no-restore -p:ContinuousIntegrationBuild=true }

    Write-Host "[4/8] Preparing publish directory..."
    Invoke-Checked "[5/8] Publishing win-x64 single-file output..." {
        & dotnet publish $project -c $Configuration -r win-x64 --self-contained false --no-restore -p:ContinuousIntegrationBuild=true -o $output
    }

    foreach ($relative in @(
        "VERSION",
        "PACKAGE_VERSION.txt",
        "STABLE_VERSION_V0.9.0.json",
        "STABLE_VERSION_V1.0.0.json",
        "STABLE_VERSION_V1.0.0.json",
        "README.md",
        "CHANGELOG.md",
        "BUILD_STATUS.txt",
        "AI_HANDOFF.md",
        "DEVELOPMENT_20260812.md",
        "DOUBAN_PLUS_CLEANUP_HANDOFF_20260812.md",
        "DOUBAN_PLUS_V1.0.0_STABLE_HANDOFF_20260813.md",
        "DOUBAN_PLUS_V1.0.0_STABLE_HANDOFF_20260813.md",
        "DOUBAN_PLUS_EXPLORE_ADAPTER_HANDOFF_20260813.md",
        "DOUBAN_PLUS_DUAL_WEBVIEW_PLAN_20260813.md",
        "DOUBAN_PLUS_DUAL_WEBVIEW_DELIVERY_20260813.md",
        "DOUBAN_PLUS_CARD_CONTENT_UNIFICATION_20260813.md",
        "DOUBAN_PLUS_UNIFIED_SHELL_STAGE1_DELIVERY_20260814.md",
        "DOUBAN_PLUS_UNIFIED_SHELL_STAGE1_FIX_20260814.md",
        "DOUBAN_PLUS_UNIFIED_SHELL_STAGE1_FIX2_20260814.md",
        "DOUBAN_PLUS_UNIFIED_SHELL_STAGE1_FIX3_20260814.md",
        "DOUBAN_PLUS_UNIFIED_SHELL_STAGE1_FIX4_20260814.md",
        "DOUBAN_PLUS_UNIFIED_SHELL_STAGE1_FIX5_20260814.md",
        "RUN_UNIFIED_SHELL_PREVIEW.cmd",
        "BUILD_FIX12_R11_NOTES.md",
        "docs\STATUS.md",
        "docs\CURRENT_ARCHITECTURE.md",
        "review\SEARCH_PAGE_ADAPTATION_20260812.md",
        "review\VALIDATION_RESULTS_BUILDFIX12_R11.md"
    )) {
        $source = Join-Path $root $relative
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination $output -Force
        }
    }

    $exe = Get-ChildItem -LiteralPath $output -Filter "*.exe" -File |
        Sort-Object Length -Descending |
        Select-Object -First 1
    if (-not $exe) {
        throw "No EXE was generated in the publish folder."
    }

    Write-Host "[6/8] Running v1.0 review-pipeline self-test..."
    $reviewSelfTestResult = Join-Path $output "review-self-test-result.txt"
    if (Test-Path -LiteralPath $reviewSelfTestResult) {
        Remove-Item -LiteralPath $reviewSelfTestResult -Force
    }

    $reviewProcess = Start-Process -FilePath $exe.FullName -ArgumentList "--review-self-test" -WorkingDirectory $output -Wait -PassThru
    if ($reviewProcess.ExitCode -ne 0) {
        throw "Review-pipeline self-test process exited with code $($reviewProcess.ExitCode)."
    }
    if (-not (Test-Path -LiteralPath $reviewSelfTestResult)) {
        throw "Review-pipeline self-test did not create review-self-test-result.txt."
    }

    # File.WriteAllText uses UTF-8. Read explicitly so Windows PowerShell 5.1
    # does not decode Chinese pass/fail prefixes with the system ANSI code page.
    $utf8 = [System.Text.Encoding]::UTF8
    $reviewSelfTestText = [System.IO.File]::ReadAllText($reviewSelfTestResult, $utf8)
    $reviewFirstLine = ($reviewSelfTestText -split "`r?`n")[0].Trim([char]0xFEFF)
    $reviewSummaryMatch = [regex]::Match($reviewFirstLine, '(?<passed>\d+)\s*/\s*(?<total>\d+)')
    if (-not $reviewSummaryMatch.Success) {
        throw "Could not parse the review-pipeline self-test summary. First line: $reviewFirstLine"
    }
    $reviewPassed = [int]$reviewSummaryMatch.Groups['passed'].Value
    $reviewTotal = [int]$reviewSummaryMatch.Groups['total'].Value
    if ($reviewPassed -ne $reviewTotal) {
        $reviewFailures = @($reviewSelfTestText -split "`r?`n" | Where-Object { $_ -match '^失败：' } | Select-Object -First 12)
        $reviewFailureText = if ($reviewFailures.Count -gt 0) { " First failures: " + ($reviewFailures -join ' | ') } else { "" }
        throw "Review-pipeline self-test reported $reviewPassed/$reviewTotal passed.$reviewFailureText Review $reviewSelfTestResult."
    }
    Write-Host "Review-pipeline self-test passed: $reviewPassed/$reviewTotal." -ForegroundColor Green

    Write-Host "[7/8] Running legacy comprehensive diagnostics..."
    $selfTestResult = Join-Path $output "self-test-result.txt"
    if (Test-Path -LiteralPath $selfTestResult) {
        Remove-Item -LiteralPath $selfTestResult -Force
    }

    $legacyPassed = 0
    $legacyTotal = 0
    $legacyProcess = Start-Process -FilePath $exe.FullName -ArgumentList "--self-test" -WorkingDirectory $output -Wait -PassThru
    if ($legacyProcess.ExitCode -ne 0) {
        Write-Warning "Legacy comprehensive self-test exited with code $($legacyProcess.ExitCode). This diagnostic does not block the stable package because the focused review-pipeline gate already passed."
    }
    elseif (-not (Test-Path -LiteralPath $selfTestResult)) {
        Write-Warning "Legacy comprehensive self-test did not create self-test-result.txt."
    }
    else {
        $selfTestText = [System.IO.File]::ReadAllText($selfTestResult, $utf8)
        $firstLine = ($selfTestText -split "`r?`n")[0].Trim([char]0xFEFF)
        $summaryMatch = [regex]::Match($firstLine, '(?<passed>\d+)\s*/\s*(?<total>\d+)')
        if ($summaryMatch.Success) {
            $legacyPassed = [int]$summaryMatch.Groups['passed'].Value
            $legacyTotal = [int]$summaryMatch.Groups['total'].Value
            if ($legacyPassed -eq $legacyTotal) {
                Write-Host "Legacy comprehensive self-test passed: $legacyPassed/$legacyTotal." -ForegroundColor Green
            }
            else {
                $failedLines = @($selfTestText -split "`r?`n" | Where-Object { $_ -match '^失败：' } | Select-Object -First 12)
                Write-Warning "Legacy comprehensive self-test reported $legacyPassed/$legacyTotal. It contains machine-dependent browser/WebView2 and historical diagnostics, so it is retained in the package but does not block this preview build."
                foreach ($failedLine in $failedLines) {
                    Write-Warning $failedLine
                }
            }
        }
        else {
            Write-Warning "Could not parse the legacy comprehensive self-test summary. First line: $firstLine"
        }
    }

    Write-Host "[8/8] Writing hashes and release metadata..."
    $hashLines = Get-ChildItem -LiteralPath $output -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($output.Length).TrimStart('\')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    $hashLines | Set-Content -LiteralPath (Join-Path $output "SHA256SUMS.txt") -Encoding UTF8

    $exeHash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
    @(
        "Version: $version",
        "SDK: $sdkVersion",
        "Generated: $(Get-Date -Format o)",
        "EXE: $($exe.Name)",
        "SHA256: $exeHash",
        "Source model: v1.0.0 clean rebaseline from the user-confirmed stable Douban native search source",
        "Review pipeline: v2 Keep/Set/Clear + official settlement/readback + BuildFix11 Detail/Worker isolation; BuildFix12 delete-v2",
        "Review pipeline self-test: $reviewPassed/$reviewTotal",
        "Legacy comprehensive diagnostics: $legacyPassed/$legacyTotal (non-blocking)",
        "Delete: v2 enabled; do routes to exact personal /do page with Chromium trusted mouse input, fresh-list settlement and lightweight subject-detail double sampling; wish/collect retain subject-detail route; local tombstone only after official confirmation; BrowserProcessExited triggers full Detail/Worker controller recreation",
        "Channel: clean-v1.0.0; real signed-in WebView2 acceptance remains user-side; this package is the new personal management baseline."
    ) | Set-Content -LiteralPath (Join-Path $output "BUILD_INFO.txt") -Encoding UTF8

    @{
        product = "观影助手"
        status = "independent-trial"
        version = $version
        deliveryName = $deliveryName
        generated = (Get-Date -Format o)
        executable = $exe.Name
        sha256 = $exeHash
        sourceBaseline = "v0.9.0 BuildFix12 R11"
        scope = "Unified visible DoubanShell + horizontal Explore filters + hidden movie Explore DOM Source WebView + C# JSON bridge + shared cards + existing dual-WebView detail entry/return"
        formalReleaseImpact = "formal v0.9.0 unchanged"
        policy = "immutable-delivery-directory"
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output "VERSION.json") -Encoding UTF8

    @(
        "观影助手 v1.0.0 clean baseline delivery",
        "",
        "本轮包含：统一 DoubanShell、Explore 横向筛选布局、隐藏电影 Explore DOM Source WebView、C# JSON 消息桥、共享电影卡片和既有双 WebView 详情入口。",
        "本轮不包含：真实登录态 WebView2 端到端验收；电影/电视剧筛选状态链路沿用现有 Source DOM 适配。",
        "",
        "本次发布物从用户确认可用的稳定源码重建，核心 C#/JS 业务代码不变。",
        "运行要求：Windows x64、.NET 8 Desktop Runtime x64、Microsoft Edge WebView2 Evergreen Runtime。"
    ) | Set-Content -LiteralPath (Join-Path $output "版本说明.txt") -Encoding UTF8

    $completeHashLines = Get-ChildItem -LiteralPath $output -File -Recurse |
        Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($output.Length).TrimStart('\')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    $completeHashLines | Set-Content -LiteralPath (Join-Path $output "SHA256SUMS.txt") -Encoding UTF8

    Compress-Archive -LiteralPath $output -DestinationPath $archive -CompressionLevel Optimal

    Write-Host ""
    Write-Host "Build completed successfully." -ForegroundColor Green
    Write-Host "Output: $output" -ForegroundColor Green
    Write-Host "Executable: $($exe.FullName)" -ForegroundColor Green
    Write-Host "Archive: $archive" -ForegroundColor Green
    exit 0
}
catch {
    $message = $_.Exception.Message
    Write-Host ""
    Write-Host ("BUILD FAILED: " + $message) -ForegroundColor Red
    try {
        Add-Content -LiteralPath $logPath -Value ("BUILD FAILED: " + $message) -Encoding UTF8
    }
    catch { }
    exit 1
}
finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch { }
    }
}
