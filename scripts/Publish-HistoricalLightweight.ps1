param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OnlyVersion = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$versions = @(
    [pscustomobject]@{ Version = '0.5.0'; Commit = '628f827'; Change = '稳定基线' },
    [pscustomobject]@{ Version = '0.6.0'; Commit = '8c1e9a5'; Change = '豆瓣演员原图直链' },
    [pscustomobject]@{ Version = '0.6.1'; Commit = 'b8f33fc'; Change = '演员头像诊断' },
    [pscustomobject]@{ Version = '0.7.0'; Commit = '4e3b6ec'; Change = '演员简介读取' },
    [pscustomobject]@{ Version = '0.7.1'; Commit = '3b195a1'; Change = '简介执行诊断' },
    [pscustomobject]@{ Version = '0.7.2'; Commit = 'd9960c8'; Change = '保存演员数据接入简介流程' },
    [pscustomobject]@{ Version = '0.8.0'; Commit = '7851485'; Change = '后台豆瓣演员读取' },
    [pscustomobject]@{ Version = '0.8.1'; Commit = '4422136'; Change = '豆瓣DOM诊断' },
    [pscustomobject]@{ Version = '0.8.2'; Commit = '87d1fdb'; Change = '独立轻量发布体系' },
    [pscustomobject]@{ Version = '0.8.3'; Commit = '2efd596'; Change = '浏览器恢复与豆瓣并发修复' },
    [pscustomobject]@{ Version = '0.8.4'; Commit = '8405fa4'; Change = '豆瓣简介与演职员头像修复' },
    [pscustomobject]@{ Version = '0.8.5'; Commit = '0bc2b42'; Change = '完整演职员与WebP头像修复' },
    [pscustomobject]@{ Version = '0.8.6'; Commit = 'a8f5877'; Change = '静默清理与头像缓存加速' },
    [pscustomobject]@{ Version = '0.8.7'; Commit = '7d268c5'; Change = '豆瓣在线搜索与PT-Depiler跳转' },
    [pscustomobject]@{ Version = '0.8.8'; Commit = 'c02b5f5'; Change = '影视库整合与搜索跳转修复' },
    [pscustomobject]@{ Version = '0.8.9'; Commit = 'v0.8.9'; Change = 'HTML影视库与内置豆瓣登录' }
)

if (-not [string]::IsNullOrWhiteSpace($OnlyVersion)) {
    $versions = @($versions | Where-Object Version -eq $OnlyVersion)
    if ($versions.Count -ne 1) { throw "未知版本：$OnlyVersion" }
}

$repository = [System.IO.Path]::GetFullPath($RepositoryRoot)
$releaseRoot = Join-Path $repository '发布版本'
$workRoot = Join-Path $repository ('.release-work-' + [Guid]::NewGuid().ToString('N'))
$appDataRoot = Join-Path $workRoot 'appdata'

if (-not (Test-Path -LiteralPath (Join-Path $repository '.git'))) {
    throw "不是 Git 仓库：$repository"
}

$desktopRuntime = & dotnet --list-runtimes | Select-String '^Microsoft\.WindowsDesktop\.App 8\.'
if (-not $desktopRuntime) {
    throw '未安装 .NET 8 Desktop Runtime，无法验证 framework-dependent 发布。'
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $workRoot | Out-Null
New-Item -ItemType Directory -Path $appDataRoot | Out-Null

try {
    foreach ($item in $versions) {
        $fullCommit = (& git -c "safe.directory=$repository" -C $repository rev-parse "$($item.Commit)^{commit}").Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($fullCommit)) {
            throw "无法解析提交：$($item.Commit)"
        }
        $shortCommit = (& git -c "safe.directory=$repository" -C $repository rev-parse --short=7 $fullCommit).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($shortCommit)) { throw "无法生成短提交号：$fullCommit" }

        $folderName = "观影助手-v$($item.Version)-$($item.Change)-$shortCommit-net8轻量版"
        $targetDirectory = Join-Path $releaseRoot $folderName
        if (Test-Path -LiteralPath $targetDirectory) {
            throw "拒绝覆盖已有发布目录：$targetDirectory"
        }

        $sourceZip = Join-Path $workRoot "$shortCommit.zip"
        $sourceDirectory = Join-Path $workRoot $shortCommit
        $publishDirectory = Join-Path $workRoot ("publish-" + $shortCommit)

        & git -c "safe.directory=$repository" -C $repository archive --format=zip --output=$sourceZip $fullCommit
        if ($LASTEXITCODE -ne 0) { throw "导出提交失败：$fullCommit" }
        Expand-Archive -LiteralPath $sourceZip -DestinationPath $sourceDirectory

        $project = Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.csproj' -File | Select-Object -First 1
        if ($null -eq $project) { throw "提交中没有找到项目文件：$fullCommit" }

        $env:APPDATA = $appDataRoot
        & dotnet restore $project.FullName --configfile (Join-Path $sourceDirectory 'NuGet.Config') -r win-x64 -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw "还原失败：v$($item.Version)" }

        & dotnet publish $project.FullName -c Release -r win-x64 --self-contained false --no-restore `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:DebugType=None `
            -p:DebugSymbols=false `
            -o $publishDirectory
        if ($LASTEXITCODE -ne 0) { throw "发布失败：v$($item.Version)" }

        $builtExecutable = Get-ChildItem -LiteralPath $publishDirectory -Filter '*.exe' -File |
            Where-Object Name -NotIn @('createdump.exe') |
            Select-Object -First 1
        if ($null -eq $builtExecutable) { throw "未找到发布程序：v$($item.Version)" }

        New-Item -ItemType Directory -Path $targetDirectory | Out-Null
        $publishedName = "观影助手-v$($item.Version)-$shortCommit.exe"
        $publishedExecutable = Join-Path $targetDirectory $publishedName
        Copy-Item -LiteralPath $builtExecutable.FullName -Destination $publishedExecutable
        Get-ChildItem -LiteralPath $publishDirectory -Force |
            Where-Object { $_.FullName -ne $builtExecutable.FullName -and $_.Extension -ne '.xml' } |
            Copy-Item -Destination $targetDirectory -Recurse

        $selfTestProcess = Start-Process -FilePath $publishedExecutable -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
        if ($selfTestProcess.ExitCode -ne 0) { throw "内置自检进程失败：v$($item.Version)" }
        $selfTestPath = Join-Path $targetDirectory 'self-test-result.txt'
        if (-not (Test-Path -LiteralPath $selfTestPath)) { throw "未生成内置自检结果：v$($item.Version)" }
        $selfTestText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($selfTestPath))
        $selfTestSummary = ($selfTestText -split "`r?`n")[0]

        $hash = (Get-FileHash -LiteralPath $publishedExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
        $buildTime = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')
        $manifest = [ordered]@{
            product = '观影助手'
            version = $item.Version
            change = $item.Change
            gitCommit = $fullCommit
            gitShortCommit = $shortCommit
            buildTime = $buildTime
            targetFramework = 'net8.0-windows'
            runtimeIdentifier = 'win-x64'
            deploymentMode = if ($item.Version -eq '0.8.9') { 'framework-dependent-single-file-with-local-html-assets' } else { 'framework-dependent-single-file' }
            requiredRuntime = '.NET 8 Desktop Runtime x64'
            requiredWebView2Runtime = if ($item.Version -eq '0.8.9') { 'Microsoft Edge WebView2 Evergreen Runtime' } else { $null }
            webView2Profile = if ($item.Version -eq '0.8.9') { '%LOCALAPPDATA%\DoubanBrowserReminder\WebView2\DoubanProfile' } else { $null }
            firstDoubanUse = if ($item.Version -eq '0.8.9') { '需要在软件内使用豆瓣官方页面扫码登录' } else { $null }
            executable = $publishedName
            executableSizeBytes = $builtExecutable.Length
            sha256 = $hash
            selfTest = $selfTestSummary
            overwritePolicy = 'immutable-refuse-overwrite'
        }
        $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $targetDirectory 'VERSION.json') -Encoding UTF8

        @(
            "观影助手 v$($item.Version)",
            "核心改动：$($item.Change)",
            "Git 提交：$fullCommit",
            '发布方式：依赖本机 .NET 8 Desktop Runtime x64 的轻量单文件版',
            $(if ($item.Version -eq '0.8.9') { 'WebView2：依赖 Microsoft Edge WebView2 Evergreen Runtime；不携带浏览器运行时。' }),
            $(if ($item.Version -eq '0.8.9') { '豆瓣 Profile：%LOCALAPPDATA%\DoubanBrowserReminder\WebView2\DoubanProfile；首次使用需要在软件内扫码登录。' }),
            "程序文件：$publishedName",
            "内置自检：$selfTestSummary",
            '覆盖策略：此目录不可覆盖；重新构建必须使用新的目标目录。',
            '提示：程序版本独立，但旧代码仍可能读取共享的应用数据和 ChromeProfile。'
        ) | Set-Content -LiteralPath (Join-Path $targetDirectory '版本说明.txt') -Encoding UTF8

        $checksumLines = Get-ChildItem -LiteralPath $targetDirectory -File -Recurse |
            Where-Object Name -ne 'SHA256SUMS.txt' |
            Sort-Object FullName |
            ForEach-Object {
                $relativePath = $_.FullName.Substring($targetDirectory.Length).TrimStart('\', '/').Replace('\', '/')
                "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relativePath"
            }
        $checksumLines | Set-Content -LiteralPath (Join-Path $targetDirectory 'SHA256SUMS.txt') -Encoding UTF8
    }
}
finally {
    $resolvedWork = [System.IO.Path]::GetFullPath($workRoot)
    if ($resolvedWork.StartsWith($repository + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedWork).StartsWith('.release-work-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force -ErrorAction SilentlyContinue
    }
}
