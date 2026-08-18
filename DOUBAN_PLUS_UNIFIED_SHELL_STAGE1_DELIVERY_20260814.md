# Douban Plus 统一 Shell 第一阶段交付说明

日期：2026-08-14  
开发副本：`v1.0-douban-plus-unified-shell-20260814`  
基线范围：以现有 v1.0.1 stable 091454 交付内容和当前可用源码为参考建立的独立副本；091454 交付包本身不含 C# 源码，因此源码逐文件同源性未宣称。

## 本轮已实现

1. 可见 `_doubanPlusView` 改为本地统一 `DoubanShell`。
2. 新增隐藏 `_doubanSourceView`，使用同一 WebView2 环境和登录 Profile 加载电影 Explore。
3. 新增 C# JSON 消息桥：Source DOM → C# → Shell 卡片。
4. 新增电影卡片读取：从真实 `.subject-list-list li > a[href]` 解析 SubjectId、标题、年份、评分和海报。
5. Shell 使用共享 `douban-card.js/.css` 渲染卡片。
6. Shell 卡片点击复用现有双 WebView 详情打开和返回机制。
7. 增加 `--unified-shell-preview` 独立 mutex/管道，避免开发 EXE 与正式稳定版互相抢占单实例。

## 本轮明确未实现

- Explore 筛选；
- 加载更多和无限滚动；
- 电视剧 Provider；
- 个人页、待看页面迁移到新 Shell；
- 新的详情页面 UI；详情仍使用既有双 WebView；
- 真实登录态 WebView2 端到端验收。

## 已执行测试

| 检查 | 结果 | 证据 |
|---|---:|---|
| Shell 静态检查 | 通过 | `tests/validate_douban_shell.py`，0 failures |
| Source bridge 静态检查 | 通过 | `tests/validate_douban_source_bridge.py`，0 failures |
| Shell/Source JavaScript 语法 | 通过 | Node `--check` |
| Release 编译 | 通过 | .NET 8，0 warnings / 0 errors |
| 评价管线专项自检 | 通过 | 18/18 |
| 内置综合自检 | 通过 | 71/71 |

## 交付前必须由用户测试

启动 `RUN_UNIFIED_SHELL_PREVIEW.cmd`，或直接运行：

```text
观影助手.exe --unified-shell-preview
```

请重点确认：

1. 可见区域显示统一 Shell，而不是豆瓣原始 Explore 页面；
2. Shell 显示真实电影卡片；
3. Source WebView 不抢占可见区域；
4. 点击卡片可以进入既有详情页；
5. 返回后回到 Shell，卡片仍在；
6. 正式稳定版 `091454` 和回滚基线 `010555` 未被覆盖。

本说明中的静态、编译和自检结果不能替代真实登录态 WebView2 验收。
