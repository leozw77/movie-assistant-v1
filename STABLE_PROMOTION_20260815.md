# 稳定版提升记录：Explore 无限滚动

日期：2026-08-15

来源开发副本（本次提升后作为新稳定版配套源码）：

`D:\chatgpt\观影助手\开发\v1.0-douban-plus-personal-filter-explore-infinite-scroll-20260815-090638`

本稳定包：

`D:\chatgpt\观影助手\开发\v1.0-douban-plus-personal-filter-explore-infinite-scroll-20260815-090638\artifacts\观影助手-v1.0.1-unified-shell-stable-explore-horizontal-filter-infinite-scroll-20260815-091329-win-x64`

## 本版范围

- 在现有统一 DoubanShell、真实豆瓣 DOM Source、Explore 电影/电视剧切换和横版筛选基础上，加入 Explore 分页无限滚动；
- Explore 追加结果时按 SubjectId 去重，保留已有卡片；
- 分页请求绑定内容类型、视图、请求序号和分页代际，避免旧响应污染当前列表；
- 加载失败保留已有结果，并提供“重试加载更多”；
- 个人页继续复用原有分页逻辑，未改写个人页状态、筛选和排序行为；
- 未实现“我的待看”下一轮功能。

## 验证记录

- Release 构建：0 错误；
- NuGet：1 个 `NU1900` 源不可访问警告；
- 内置自检：73/73；
- 评价管线专项自检：18/18；
- Douban Plus-only：41 个源文件，0 failed；
- Shell、Explore、Source bridge、嵌入脚本：全部通过；
- Explore 无限滚动专项静态验证：0 failed；
- 本次提升按用户确认“测试完毕”登记；真实登录态 WebView2 仍按交接文档列出的下一轮功能边界现场验证。

## 稳定版文件

EXE SHA256：

`98B9B5656243098F8D98F7A5D483BA9963245F5B95BCEC4EBB6252D1DA028C2F`

稳定 ZIP SHA256：

`D5704041CE6ACBCEAA0A1A53CDA68FDE1FF8710A762A97906E5DFD511C7B91C2`

## 直接回滚边界

本次提升前的旧稳定版保持原目录和文件不变，作为直接只读回滚基线：

`D:\chatgpt\观影助手\开发\v1.0-douban-plus-personal-filter-20260815\artifacts\观影助手-v1.0.1-unified-shell-stable-explore-horizontal-filter-20260815-083732-win-x64\观影助手.exe`

旧稳定版 SHA256：

`24054F951BB6621BAC762B1C840150FB269BBD44583DFEBD0FD38C6C8055E59E`

更早的 `1F22A4...A414` 和 `CA26B6...E8AC78` 回滚基线继续保留。

## 下一版本：我的待看

下一轮从本稳定版配套源码另建时间戳开发副本，只实现“我的待看”，不重写已稳定的 Explore 筛选和无限滚动。

必须补齐并现场验证：

- Explore、个人页、搜索结果和详情页海报右键菜单中的“加入我的待看”；
- 海报右键菜单中的“刷新页面”；
- 统一 Shell 中“我的待看”入口、列表、删除和详情返回；
- 本地待看数据与豆瓣官方状态/历史数据隔离。

当前源码中即使存在未接通的待看相关片段，也不视为功能已完成；下一轮必须以真实登录态 WebView2 可操作结果为准。
