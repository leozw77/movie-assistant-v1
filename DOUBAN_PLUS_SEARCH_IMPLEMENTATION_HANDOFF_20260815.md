# 豆瓣原生搜索实现交接

> 2026-08-16：本实现已按 `STABLE_PROMOTION_20260816.md` 写为 v1.0.1 发布版，并由用户确认正常可用。本文保留实现细节，稳定路径和哈希以 `DEVELOPMENT_BASELINE.json` 为准。

## 本轮目标

搜索框调用豆瓣影视原生搜索页，不走本地搜索数据源。搜索结果由隐藏 Source WebView2 读取真实的 `search.douban.com/movie/subject_search` DOM，再转发给统一 Shell 展示。

## 历史实现复用边界

本轮先检索了旧版 `v1.0-douban-plus-unified-shell-20260814` 的搜索实现，复用了其中已经验证过的真实 DOM 规则：

- 搜索结果节点、标题、评分、评价人数、年份、类型/地区/时长、演职员的 DOM 读取；
- 绝对地址、协议相对地址和相对 `/subject/{id}/` 地址归一化；
- 豆瓣原生分页链接读取；
- 搜索结果进入 `/subject/{id}/` 详情，以及返回原搜索页。

没有复用旧版 `SearchCard`、`atv-search-page-card` 或旧版搜索页面视觉样式。

## 当前视觉规则

搜索结果继续使用当前稳定版统一卡片：

- `WebAssets/DoubanPlus/douban-card.js`
- `WebAssets/DoubanPlus/douban-card.css`

因此海报、评分角标、标题、年份和底下文字沿用当前页面已有规则；搜索结果只补充真实搜索 DOM 提供的上下文数据。

搜索页原始文本按豆瓣结果顺序解析：片长之后的第一个人物作为导演，后续人物作为主演；当前卡片只显示年份/国家、类型、导演、主演四类结构化文字，别名和片长不塞入卡片底部上下文。

## 2026-08-15 搜索 DOM 读取修复

真实豆瓣搜索页使用 `.item-root`、`.meta.abstract` 和 `.meta.abstract_2` 结构。演职员文本必须从 `.abstract_2` 读取；若只读取旧适配器使用的 `.subject-cast/.cast`，卡片会只剩年份/国家和类型。首次运行时演员解析函数还误引用未定义的 `castIndex`，使 `readPage()` 抛出异常并让宿主收到空值，表现为“Source DOM 读取结果不是 JSON 对象”。现已按既定规则直接返回：人员列表第一个为导演，其余为主演；验证器同时锁定 `.abstract_2` 规则和 `castIndex` 不存在。

## 主要链路

1. Shell 搜索框提交 `doubanShellSearch`。
2. 宿主导航 Source WebView2 到豆瓣原生搜索 URL。
3. Source bridge 读取真实搜索结果 DOM，发送 `doubanSourceResult`。
4. 宿主转发 `doubanShellData`，包含结果、查询词和原生分页链接。
5. Shell 用当前卡片组件展示，点击分页仍导航豆瓣原生搜索页。
6. 点击卡片进入真实豆瓣详情，返回原搜索页。

搜索页只保留豆瓣原生分页按钮：手动点击分页后导航下一张原生搜索页；不再使用搜索页 IntersectionObserver，不再自动连续追加或自动导航。探索页和个人页的原有无限滚动阈值与行为不变。

## 本轮文件范围

- `WebAssets/DoubanPlus/douban-source-bridge.js`
- `WebAssets/DoubanPlus/douban-shell.js`
- `WebAssets/DoubanPlus/douban-shell.css`
- `DoubanPlusWebView2Script.cs`
- `HtmlMediaLibraryForm.cs`
- `SelfTest.cs`
- `tests/validate_douban_shell.py`

## 验证边界

- 已执行开发基线门禁；
- 已执行 Shell/Source bridge 静态校验；
- 已执行 Release 编译和内置自检；
- 用户已确认当前 v1.0.1 发布版正常可用；后续 AI 影评问题不在本轮搜索发布范围内。

## 现场验收顺序

从个人页、Explore 或待看页提交一个已知影视名称，确认原生搜索结果、无结果页、分页、详情进入/返回，以及当前待看功能回归；再验证连续快速搜索不会把旧结果覆盖新结果。
