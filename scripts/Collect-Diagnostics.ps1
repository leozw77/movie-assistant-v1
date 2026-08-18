$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$work = Join-Path $env:TEMP ("DoubanReview-Diagnostics-" + $stamp)
$zip = Join-Path $root ("DoubanReview-Diagnostics-" + $stamp + ".zip")

function Copy-OptionalFile {
    param([string]$Source, [string]$DestinationName)
    if (Test-Path -LiteralPath $Source -PathType Leaf) {
        Copy-Item -LiteralPath $Source -Destination (Join-Path $work $DestinationName) -Force
    }
}

try {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    New-Item -ItemType Directory -Path $work -Force | Out-Null

    Copy-OptionalFile (Join-Path $root "build-preview.log") "build-preview.log"
    Copy-OptionalFile (Join-Path $root "performance-hotfix.log") "performance-hotfix.log"

    $artifactRoots = @()
    $artifacts = Join-Path $root "artifacts"
    if (Test-Path -LiteralPath $artifacts -PathType Container) {
        $artifactRoots = @(Get-ChildItem -LiteralPath $artifacts -Directory | Sort-Object LastWriteTime -Descending)
    }
    if ($artifactRoots.Count -gt 0) {
        $latest = $artifactRoots[0].FullName
        Copy-OptionalFile (Join-Path $latest "review-self-test-result.txt") "review-self-test-result.txt"
        Copy-OptionalFile (Join-Path $latest "self-test-result.txt") "self-test-result.txt"
        Copy-OptionalFile (Join-Path $latest "BUILD_INFO.txt") "BUILD_INFO.txt"
        Copy-OptionalFile (Join-Path $latest "PACKAGE_SHA256SUMS.txt") "published-PACKAGE_SHA256SUMS.txt"
    }

    $logRoot = Join-Path $env:LOCALAPPDATA "DoubanBrowserReminder\logs"
    $diagnosticPath = Join-Path $logRoot "diagnostic.log"
    Copy-OptionalFile $diagnosticPath "diagnostic.log"
    Copy-OptionalFile (Join-Path $logRoot "review-transactions.jsonl") "review-transactions.jsonl"
    if (Test-Path -LiteralPath $logRoot -PathType Container) {
        $crashDir = Join-Path $work "crash-logs"
        $crashes = @(Get-ChildItem -LiteralPath $logRoot -Filter "crash-*.log" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 10)
        if ($crashes.Count -gt 0) {
            New-Item -ItemType Directory -Path $crashDir -Force | Out-Null
            foreach ($crash in $crashes) { Copy-Item -LiteralPath $crash.FullName -Destination $crashDir -Force }
        }
    }

    $logFirstEntryAt = ""
    $logLastEntryAt = ""
    if (Test-Path -LiteralPath $diagnosticPath -PathType Leaf) {
        $timestampLines = @(Get-Content -LiteralPath $diagnosticPath | Where-Object { $_ -match '^\[(?<stamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\]' })
        if ($timestampLines.Count -gt 0) {
            if ($timestampLines[0] -match '^\[(?<stamp>[^\]]+)\]') { $logFirstEntryAt = $Matches.stamp }
            if ($timestampLines[-1] -match '^\[(?<stamp>[^\]]+)\]') { $logLastEntryAt = $Matches.stamp }
        }
    }

    $currentProcessId = ""
    $appProcess = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'DoubanReview|QbPotDoubanAi|DoubanBrowserReminder' } | Sort-Object StartTime -Descending | Select-Object -First 1)
    if ($appProcess.Count -gt 0) { $currentProcessId = $appProcess[0].Id }

    $currentSessionVerifiedAt = ""
    $sessionPath = Join-Path $env:LOCALAPPDATA "DoubanBrowserReminder\douban-session.json"
    if (Test-Path -LiteralPath $sessionPath -PathType Leaf) {
        try {
            $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
            $currentSessionVerifiedAt = [string]$session.LastVerifiedAt
        } catch { }
    }

    $collectorGeneratedAt = Get-Date -Format o
    $summary = @(
        "CollectorGeneratedAt: $collectorGeneratedAt",
        "LogFirstEntryAt: $logFirstEntryAt",
        "LogLastEntryAt: $logLastEntryAt",
        "CurrentProcessId: $currentProcessId",
        "CurrentSessionVerifiedAt: $currentSessionVerifiedAt",
        "Generated: $collectorGeneratedAt",
        "Project root: $root",
        "Computer: $env:COMPUTERNAME",
        "OS: $([System.Environment]::OSVersion.VersionString)",
        "64-bit OS: $([System.Environment]::Is64BitOperatingSystem)",
        "64-bit process: $([System.Environment]::Is64BitProcess)",
        "PowerShell: $($PSVersionTable.PSVersion)",
        "",
        "Privacy: this collector copies only named build/test/runtime logs.",
        "It does not copy WebView2 profiles, cookies, passwords, or browser storage."
    )
    $summary | Set-Content -LiteralPath (Join-Path $work "COLLECTOR_INFO.txt") -Encoding UTF8

    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $work "*") -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Diagnostics package created:" -ForegroundColor Green
    Write-Host $zip -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ("Diagnostic collection failed: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
finally {
    if (Test-Path -LiteralPath $work) {
        try { Remove-Item -LiteralPath $work -Recurse -Force } catch { }
    }
}
