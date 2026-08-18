#!/usr/bin/env python3
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FAILURES = []


def check(name: str, condition: bool, detail: str = "") -> None:
    if condition:
        print(f"PASS: {name}")
    else:
        FAILURES.append(name)
        print(f"FAIL: {name}{' | ' + detail if detail else ''}")


source_js = (ROOT / "WebAssets/DoubanPlus/douban-source-bridge.js").read_text(encoding="utf-8-sig")
check("Source bridge 存在", bool(source_js.strip()))
check("Source bridge 读取真实电影与电视剧 Explore DOM", all(token in source_js for token in (".subject-list-list", "a[href]", "movie.douban.com", "readPage", "isTvExplore", "contentTypeLabel")))
check("Source bridge 输出统一 JSON", all(token in source_js for token in ("doubanSourceResult", "requestId", "generation", "contentType", "items", "candidateAnchorCount")))
check("Source bridge 输出异步 DOM 诊断", all(token in source_js for token in ("htmlLength", "exploreMenuCount", "listMainCount", "readyState")))
check("Source bridge 输出原生筛选和分页状态", all(token in source_js for token in ("readFilterSnapshot", "filters", "paging", "loadMore")))
check("Source bridge 只驱动豆瓣原生筛选节点", all(token in source_js for token in (".explore-menu", ".base-selector", ".drc-label", "clickNative")))
check("Source bridge 支持筛选组和选项", all(token in source_js for token in ("openFilterGroup", "selectFilter", "visibleNativeOptions")))
check("Source bridge 对重复选择当前项提供 no-op 短路", all(token in source_js for token in ("nativeOptionSelected", "noOp: true", "currentValue === label")))
check("Source bridge 不生成可见 Shell UI", all(token not in source_js for token in ("qb-douban-shell-root", "innerHTML", "QbDoubanCard")))
check("Source bridge 电视剧卡片复用 subject URL 与解析链路", all(token in source_js for token in ("(?:movie|tv)", "contentType()", "exploreMode")))

node = shutil.which("node")
if node:
    with tempfile.NamedTemporaryFile("w", suffix=".js", encoding="utf-8", delete=False) as handle:
        handle.write(source_js)
        path = Path(handle.name)
    try:
        result = subprocess.run([node, "--check", str(path)], capture_output=True, text=True)
        check("Source bridge Node 语法检查", result.returncode == 0, (result.stderr or result.stdout).strip())
    finally:
        path.unlink(missing_ok=True)
else:
    check("Source bridge Node 语法检查", False, "node 未安装")

print(f"SUMMARY: {len(FAILURES)} failures")
sys.exit(1 if FAILURES else 0)
