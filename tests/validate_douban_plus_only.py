#!/usr/bin/env python3
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FAILURES = []


def check(name: str, condition: bool, detail: str = "") -> None:
    if condition:
        print(f"PASS: {name}")
    else:
        FAILURES.append(name)
        print(f"FAIL: {name}{' | ' + detail if detail else ''}")


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


try:
    ET.parse(ROOT / "观影助手.csproj")
    check("csproj XML 可解析", True)
except Exception as exc:
    check("csproj XML 可解析", False, str(exc))

source_files = {path.name for path in ROOT.glob("*.cs")}
legacy_files = {"MyWatchHistoryForm.cs", "MovieDetailForm.cs", "DoubanOnlineSearchForm.cs", "DoubanHistoryForm.cs"}
check("旧原生影视库窗体已删除", not source_files.intersection(legacy_files))
check("原 HTML 影视库目录已删除", not (ROOT / "WebAssets" / "MediaLibrary").exists())

tray = read("TrayContext.cs")
program = read("Program.cs")
host = read("HtmlMediaLibraryForm.cs")
single_instance = read("SingleInstanceControl.cs")
check("托盘只保留 Douban Plus 入口", '"Douban Plus…"' in tray and "旧版影视库" not in tray and "ShowLegacyMediaLibrary" not in tray)
check("启动参数不再提供 HTML 影视库预览", "--html-library-preview" not in program)
check("后台实例支持唤醒与退出控制", "--exit" in program and "TrySendCommand" in program and "ListenControlCommandsAsync" in tray and "QbPotDoubanAi.Control.v1" in single_instance)
check("宿主启动后直达 Douban Plus", "NavigateInitialDoubanPageAsync" in host and 'core.Navigate(LocalOrigin + "/index.html")' not in host)
check("宿主不再引用 MediaLibrary 资源目录", "WebAssets\\MediaLibrary" not in host and "WebAssets/MediaLibrary" not in host)
check("旧版回退协议已删除", all(token not in host for token in ("openLegacy", "ShowLegacyMediaLibrary", "legacyButton")))

douban_assets = ROOT / "WebAssets" / "DoubanPlus"
check("Douban Plus 资源完整", all((douban_assets / name).is_file() for name in ("system.min.js", "named-register.min.js", "douban-plus.user.js")))
build_script = read("scripts/Build-Preview.ps1")
check("构建脚本只校验 Douban Plus 脚本", "WebAssets\\DoubanPlus\\douban-plus.user.js" in build_script and "WebAssets\\MediaLibrary\\app.js" not in build_script)

plus_script = read("WebAssets/DoubanPlus/douban-plus.user.js")
check("搜索初始空结果仍会渲染", "lastSignature" in plus_script and "atv-search-page-empty" in plus_script)
check("评论头像不再抓取人物页面", "fetchAvatarUrls" not in plus_script and "extractProfileAvatar" not in plus_script)
check("外部评分延后加载", "setTimeout(loadRatings" in plus_script and "1200" in plus_script)
check(
    "WebView 完成判断包含实际内容探针",
    all(token in host for token in ("ContentProbe=", ".atv-search-page-card", ".atv-hero-title", "nativeSubjectLinkCount")),
)
check("空内容不会直接显示为完成", "CurrentDocumentShown=False" in host and "ContentRecoveryScheduled=True" in host)

print(f"SUMMARY: {len(source_files)} source files checked, {len(FAILURES)} failed")
sys.exit(1 if FAILURES else 0)
