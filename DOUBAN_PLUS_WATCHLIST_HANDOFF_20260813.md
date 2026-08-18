# Douban Plus 本地“待看”与个人页无限滚动交接文档

日期：2026-08-13  
当前开发目录：`D:\chatgpt\观影助手\开发\v1.0-douban-plus`

## 最新交接摘要（2026-08-13）

- 本地待看目前已经实现：加入、删除、SubjectId 去重、独立 `watchlist.json`、重启恢复、详情 URL 保存、详情页打开、本地海报缓存和海报失败回退。
- 当前新增需求是：主要针对豆瓣个人页“看过 / 想看 / 在看”实现无限滚动；本地“待看”不是本次重点，继续保持独立本地数据和现有分页/列表逻辑。
- 本次只完成了网上方案搜索和可行性分析，没有开始修改无限滚动代码。
- 后续任何代码交付都必须同时提供最新 Release EXE；不得只交源码或只交文档。
- 正式 v0.9.0 EXE、ZIP、哈希和发布目录继续保持隔离，不得被覆盖。

## 一、目标

继续在隔离的 v1.0 Douban Plus 副本中实现“应用独立待看”功能。

这里的“待看”是应用自己的本地片单，不是豆瓣官方“想看”。它需要保存到本地待看数据库，且不改变豆瓣账号的任何状态。

用户已经确认的入口：

1. 豆瓣个人页“看过”中的海报右键加入待看；
2. 豆瓣个人页“想看”中的海报右键加入待看；
3. 豆瓣个人页“在看”中的海报右键加入待看；
4. Douban Plus 搜索结果卡片中的海报右键加入待看；
5. Douban Plus 影片详情页主海报右键加入待看。

三个个人页状态仍然是豆瓣官方状态：

- `collect`：看过；
- `wish`：想看；
- `do`：在看。

本地待看只是另一份应用数据，不应被显示成豆瓣“想看”，也不应调用豆瓣官方状态写入接口。

## 二、当前已经完成的内容

### 2.1 Douban Plus 个人页重绘

主要文件：

- `WebAssets\DoubanPlus\douban-personal-page.js`
- `WebAssets\DoubanPlus\douban-personal-page.css`
- `HtmlMediaLibraryForm.cs`
- `tests\validate_douban_personal_page.py`

已经支持真实豆瓣个人页：

- `/people/{ProfileId}/collect`
- `/people/{ProfileId}/wish`
- `/people/{ProfileId}/do`

适配器根节点：

`#qb-douban-personal-root`

当前个人页从真实 DOM 读取：

- SubjectId；
- 豆瓣详情 URL；
- 标题；
- 海报 URL；
- 年份；
- 豆瓣评分和评价人数（若真实列表 DOM 提供）；
- 个人星级；
- 标记日期；
- 用户短评；
- 简介字段。

页面已实现：

- “看过 / 想看 / 在看”状态切换；
- “全部 / 电影 / 电视”类型切换；
- 豆瓣原生分页；
- 空列表、登录/验证码、网络结构异常提示；
- 点击卡片进入真实豆瓣详情页；
- sessionStorage 滚动位置保存和返回恢复。

个人页适配器是只读解析，不使用 `fetch` 或 `GM_xmlhttpRequest`。

### 2.2 统一搜索和搜索结果页

搜索入口复用 Douban Plus 原有 `SubjectSwitcher`，不能重新做第二套搜索框。

相关源码：

- `vendor\douban-plus-1.8.1\src\modules\subject\search\subject-switcher.tsx`
- `vendor\douban-plus-1.8.1\src\modules\subject\runtime\personal-search-mount.tsx`
- `vendor\douban-plus-1.8.1\src\modules\subject\runtime\search-page-mount.tsx`
- `vendor\douban-plus-1.8.1\src\modules\subject\styles\search-page.css`
- 生成文件：`WebAssets\DoubanPlus\douban-plus.user.js`

搜索结果卡片目前已有：

- SubjectId；
- 真实详情 URL；
- 海报；
- 标题；
- 年份/类型；
- 豆瓣评分；
- 影片事实和主创信息；
- 打开详情动作。

搜索卡片海报保持比例，使用 `object-fit: contain`，不应为了加入按钮而压缩或裁切海报。

### 2.3 详情页和 WebView2 宿主

详情页使用真实 URL：

`https://movie.douban.com/subject/{SubjectId}/`

由 Douban Plus 进行页面重绘，仍在同一个 WebView2 中打开。

当前宿主已经有：

- `openDoubanPlusDetail` 详情打开桥接；
- 个人页详情返回状态；
- `_activeDoubanPersonalPageUrl`；
- `_returnToPersonalPageOnBack`；
- 导航遮罩；
- 稳定根节点检测；
- 黑屏/内容未挂载时的一次恢复尝试；
- 最大化窗口和 DPI 缩放设置。

近期真实压力测试结果：20 部不同影片中，17 次完成搜索→详情→返回，3 次出现导航完成但页面黑屏无卡片。后续实现待看时不能忽略这个问题，新增功能必须在黑屏恢复链路下验证。

### 2.4 识别、AI 和旧数据边界

当前阶段保留：

- 视频文件名识别；
- `RecognitionMatcher` 和识别缓存；
- PotPlayer/爱奇艺播放状态；
- 豆瓣详情页实时读取；
- 后续 AI 读取详情页剧情、短评和长评的方向。

AI 后续不应恢复读取 `douban-history.json` 或旧本地历史缓存作为主要输入。

## 三、当前明确没有完成的内容（原始阶段基线）

> 本节记录最初建立交接文档时的未完成状态。当前实际完成情况以“最新交接摘要”和第十节为准，不得再据此判断本地待看功能当前是否存在。

当前源码中没有完成以下本地待看实现：

- `LocalWatchlistItem` 模型；
- `watchlist.json` 读写；
- `watchlist\posters` 本地海报目录；
- 本地待看页面；
- 右键菜单；
- 个人页海报右键消息；
- 搜索结果海报右键消息；
- 详情页海报右键消息；
- SubjectId 去重；
- 本地待看删除；
- 海报下载失败回退；
- 重启后恢复；
- 本地待看与豆瓣“想看”的 UI 区分。

当前 `Models.cs` 和 `HtmlMediaLibraryForm.cs` 中仍能看到部分旧 `DoubanHistoryState`/历史协调代码。不能只根据旧交接文档中的“已删除”描述判断源码已经完全清理；新对话开始后应先重新核对依赖，再决定是否拆除。

## 四、待看功能的目标设计

### 4.1 用户交互

三个页面统一使用自定义右键菜单：

```text
右键海报
  ├─ 加入待看
  └─ 已在待看时显示：已在待看 / 移出待看
```

要求：

- 拦截页面海报的 `contextmenu` 事件；
- 阻止 WebView2 默认浏览器右键菜单；
- 菜单使用 Douban Plus 深色视觉；
- 菜单显示影片名，避免用户误操作；
- 菜单关闭方式包括点击页面其他位置、Esc、操作完成；
- 不能影响海报左键打开详情和详情页海报预览；
- 不能影响正常滚动、分页和搜索。

菜单操作通过 WebView2 `postMessage` 发送到 C# 宿主。C# 宿主负责验证 URL/SubjectId、去重和写入本地数据。

### 4.2 最小本地数据模型

建议新增独立文件，例如 `LocalWatchlistStore.cs`，不要把模型塞进 `DoubanHistoryRecord`：

```text
LocalWatchlistItem
  SubjectId       string，唯一键
  SubjectUrl      string
  Title           string
  OriginalTitle   string，可选
  Year            string，可选
  PosterPath      string，可选，本地海报路径
  PosterSourceUrl string，可选，原始海报 URL
  AddedAt         DateTime
  UpdatedAt       DateTime
  Note            string，可选
  Source          string，例如 personal/search/detail
```

建议数据位置：

```text
%LOCALAPPDATA%\DoubanBrowserReminder\watchlist\watchlist.json
%LOCALAPPDATA%\DoubanBrowserReminder\watchlist\posters\<SubjectId>.*
```

### 4.3 写入规则

- SubjectId 是唯一键；
- 重复加入不得生成第二条；
- 写入先写 `.tmp`，再原子替换正式 JSON；
- JSON 损坏时不得覆盖原文件，应保留备份并显示错误；
- 海报下载失败时仍保存 `PosterSourceUrl`，不能导致整条待看失败；
- 海报下载应使用明确允许的 Douban 图片域名，不允许任意 URL 下载；
- 只保存本地待看数据，不写豆瓣官方 `wish/do/collect`；
- 不修改 `douban-history.json`；
- 不复用 `DoubanHistoryRecord.Status`。

### 4.4 删除规则

本地“移出待看”只做本地操作：

1. 按 SubjectId 删除 JSON 条目；
2. 删除关联本地海报；
3. 保留豆瓣官方状态不变；
4. 删除失败时显示明确错误，不假报成功；
5. 定期或启动时清理孤儿海报和 `.tmp` 文件。

## 五、建议实现顺序

### 阶段 1：只读核对和基础存储

1. 检查 `Models.cs`、`Store`、`HtmlMediaLibraryForm.cs` 的历史依赖；
2. 新增独立 `LocalWatchlistItem`；
3. 新增独立 `LocalWatchlistStore`；
4. 只做 JSON 原子写入、读取、去重、删除和海报文件管理；
5. 添加离线单元/自检，不接 UI。

### 阶段 2：统一右键菜单桥接

1. 设计统一消息格式，例如 `doubanWatchlistAdd`；
2. 校验消息来源必须是允许的 Douban 页面；
3. 个人页适配器给 `.qb-personal-poster` 增加右键入口；
4. 搜索结果适配器给 `.atv-search-page-card-poster` 增加右键入口；
5. 详情页给主海报增加右键入口；
6. 不直接修改生成后的 `douban-plus.user.js`，应修改 vendor 源码后重新构建；
7. 若详情页不适合修改 vendor 组件，应新增独立 host 注入适配器，不复制整套详情页。

### 阶段 3：本地待看页面

1. 新增 Douban Plus 风格的本地待看页面；
2. 支持卡片、海报、标题、年份、加入时间；
3. 支持点击进入真实豆瓣详情；
4. 支持详情返回本地待看页面并恢复滚动位置；
5. 明确标注“本地待看”，避免和豆瓣“想看”混淆。

### 阶段 4：真实 WebView2 验收

依次验证：个人页三种状态、搜索结果、详情页、重复加入、重启恢复、删除、网络失败、黑屏恢复。

## 六、开发规范和边界

### 6.1 版本隔离

- 只能修改：`D:\chatgpt\观影助手\开发\v1.0-douban-plus`；
- 正式 v0.9.0 EXE、ZIP、哈希和发布目录不得修改；
- 默认不修改 `vendor\douban-plus-1.8.1` 的行为边界；若必须修改，改源码后重新构建生成 bundle；
- 不直接手改生成后的 `WebAssets\DoubanPlus\douban-plus.user.js`。

### 6.2 数据保护

- 本地待看必须是独立数据源；
- 不删除或覆盖历史数据，除非有单独明确授权；
- 不把豆瓣账号状态当成本地待看状态；
- 不把本地待看写回豆瓣；
- 不让 AI 依赖本地旧历史缓存。

### 6.3 修改流程

- 行为/架构修改前先说明方案、风险、准确文件范围和验证计划；
- 用户已经确认本交接文档中的右键加入待看范围，可以按该范围实施；
- 修改使用 `apply_patch`；
- 不使用宽范围删除，不删除整个缓存目录；
- 关闭进程时只关闭已经确认属于 v1.0 开发副本的进程；
- 不关闭正式版本或无法确认路径的进程。

### 6.4 验收标准

静态验证不能代替真实 UI 验收。至少要有：

- `node --check`；
- 个人页静态验证；
- 前端源码构建；
- .NET Release 构建；
- 本地待看存储自检；
- 真实 WebView2 右键操作；
- 真实不同影片测试，避免缓存重复造成假数据；
- 记录实际 SubjectId、文件写入、重复加入、删除和返回结果。

## 七、交接后的第一件事（原始阶段基线）

新对话开始后，先执行以下检查，不要直接写 UI：

1. 确认当前源码是否没有 `LocalWatchlistItem`/`watchlist.json` 实现；
2. 确认个人页、搜索页、详情页实际海报 DOM 结构；
3. 确认 WebView2 当前消息桥接和允许页面校验；
4. 确认 `HtmlMediaLibraryForm.cs` 仍有哪些旧历史依赖；
5. 提交阶段 1 的文件范围和测试计划；
6. 再实现独立存储和右键菜单。

新对话可直接使用以下指令：

> 继续执行 `DOUBAN_PLUS_WATCHLIST_HANDOFF_20260813.md`。用户已确认：个人页看过/想看/在看、搜索结果和详情页的海报右键都可以加入独立本地待看数据库。先按文档完成阶段1只读核对和独立存储设计，不要修改正式v0.9.0，不要把本地待看写入豆瓣wish/do/collect，也不要直接编辑生成后的douban-plus.user.js。

## 八、2026-08-13 实机验收补充

- 本次实机观察时未看到“待看”按钮；当时启动的是旧开发产物/被单实例占用的窗口，不能据此判定当前源码功能缺失，后续必须使用当前 v1.0 开发构建重新验收顶部导航。
- 后续如果启动或验收被占用，且能够明确确认占用进程属于 `D:\chatgpt\观影助手\开发\v1.0-douban-plus`，直接关闭该开发进程后再启动当前构建，不再停留在“程序已在后台运行”提示上。
- 仍不得关闭正式 v0.9.0、来源不明或无法确认路径的进程；关闭前必须记录实际进程路径。
- 待看按钮缺失的根因已确认：待看脚本在 WebView2 `DocumentCreated` 阶段直接对尚未创建的 `head/html` 调用 `append`，脚本提前终止；已改为等待可用 DOM 节点，并让 MutationObserver 观察 `document`，避免同类时序错误。

## 九、海报缓存和卡片显示补充

- 点击加入待看时先保存条目与 `PosterSourceUrl`，随后下载海报到本地 `watchlist\posters` 目录。
- JSON 保留原始海报 URL；列表显示优先使用本地海报，本地文件缺失或下载失败时回退到 URL。
- 海报缓存通过 WebView2 本地虚拟主机读取，不把本地绝对路径暴露给页面。
- 删除待看时同步删除对应的本地海报缓存。
- 待看卡片沿用个人页卡片比例和视觉结构；标题下依次显示“上映年份”和“加入时间”。
- 海报下载失败不影响条目保存和详情页跳转，响应会返回失败状态供界面提示。

## 十、个人页无限滚动方案分析

### 10.1 网上方案结论

已分析 Greasy Fork 的“自动无缝翻页 / AutoPager”脚本：

`https://greasyfork.org/zh-CN/scripts/419215-autopager`

该脚本的核心不是跳转下一页，而是：

1. 监听滚动接近页面底部；
2. 从分页器取得下一页 URL；
3. 后台请求下一页 HTML；
4. 在临时 DOM 中解析下一页内容；
5. 取出指定内容节点并追加到当前页面；
6. 替换分页器，使下一次继续加载后续页面。

它使用 `nextL` 指定下一页链接、`pageE` 指定需要追加的内容、`replaceE` 指定需要更新的分页器、`scrollD` 指定触发距离。该模型适合“服务端分页、页面中存在明确下一页链接”的网站。

AutoPager 源码还针对登录 Cookie、跨域、请求超时和请求失败做了额外处理。它的作者明确提示：没有明确分页、完全动态加载或需要特殊登录状态的页面，不能直接套用普通翻页规则。

### 10.2 与当前个人页的匹配情况

当前个人页已经具备适配条件：

- 原始电影节点：`.grid-view .item`；
- 原生分页节点：`.paginator`；
- 分页 URL 带有 `start` 参数；
- `douban-personal-page.js` 已有 `readItem()`，可以提取 SubjectId、详情 URL、标题、海报、年份等信息；
- 当前页面已经使用独立的 `qb-personal-card` 自定义卡片重绘。

因此，技术上可以借鉴 AutoPager 的“后台加载下一页”机制，但不能直接把下一页原始 HTML 节点插入当前页面。

下一页内容必须重新经过现有的 `readItem()` 和自定义卡片渲染逻辑，否则可能出现：

- 豆瓣原始样式和 Douban Plus 样式混杂；
- 新卡片没有详情页点击恢复逻辑；
- 新卡片缺少右键加入待看功能；
- 海报、筛选和状态标签显示不一致。

### 10.3 推荐实现方案

第一版仅针对豆瓣官方个人页：

- `collect`：看过；
- `wish`：想看；
- `do`：在看。

本地 `watchlist` 页面不纳入本次无限滚动改造。

推荐在个人页 WebView2 的同源页面上下文中请求下一页：

```text
触底哨兵进入可视区域
    ↓
锁定请求状态，防止重复请求
    ↓
读取下一页 URL
    ↓
请求并解析下一页 HTML
    ↓
提取 .grid-view .item
    ↓
通过 readItem() 转为统一条目
    ↓
按 SubjectId 去重
    ↓
使用现有 qb-personal-card 追加渲染
    ↓
更新下一页 URL；没有下一页时显示完成状态
```

第一版建议保留原生分页作为备用入口，并暂不完全照搬 AutoPager 的历史记录修改逻辑。原因是当前项目已经有详情页返回和 `sessionStorage` 滚动恢复机制，直接修改浏览器历史 URL 可能导致返回详情后重新落到错误分页。

### 10.4 预计文件范围

优先只修改：

- `WebAssets\DoubanPlus\douban-personal-page.js`：加载状态、下一页请求、DOM 解析、去重、追加渲染；
- `WebAssets\DoubanPlus\douban-personal-page.css`：加载中、加载失败、重试、已加载全部内容状态。

第一版原则上不修改：

- `LocalWatchlistStore.cs`；
- `watchlist.json` 数据结构；
- 豆瓣官方状态写入逻辑；
- 正式 v0.9.0；
- 生成后的 `douban-plus.user.js`。

如果 WebView2 内同源请求不能稳定复用登录状态，才评估增加 C# 桥接或隐藏 iframe；这属于备用方案，不作为第一版默认设计。

### 10.5 主要风险

- 豆瓣分页 HTML 或 CSS 选择器变化；
- 登录失效、验证码或网络失败；
- 滚动触底造成并发请求和重复追加；
- 不同类型筛选混入错误页面的数据；
- 不去重导致同一 SubjectId 重复出现；
- 加载下一页后点击详情，返回时滚动位置和已加载内容不一致；
- 一次性追加过多卡片导致 WebView2 页面变慢。

### 10.6 无限滚动验收标准

真实登录 WebView2 中必须至少验证：

1. “看过 / 想看 / 在看”分别可以连续加载两页以上；
2. “全部 / 电影 / 电视”筛选不会混入其他筛选的数据；
3. 不出现重复 SubjectId；
4. 加载过程中不会重复发送并发请求；
5. 网络失败时显示错误和重试入口；
6. 没有下一页时显示“已加载全部内容”；
7. 第一页和后续页的卡片视觉、海报、详情点击、右键加入待看行为一致；
8. 从后续页进入详情再返回，个人页状态和滚动位置可恢复；
9. 不修改豆瓣 `wish / do / collect`；
10. 不影响本地待看数据和海报缓存。

## 十一、每次交付的硬性底线：必须提供 EXE

这是后续开发和交付的强制要求：

- 每次代码或行为变更交付，必须先从隔离开发目录生成最新 Release EXE，并在交付消息中给出可点击的 EXE 文件路径；
- 不允许只提交源码、只提交文档或只报告“已完成”而不提供 EXE；
- EXE 必须来自：

`D:\chatgpt\观影助手\开发\v1.0-douban-plus`

- 交付前至少执行：

```text
dotnet build 观影助手.csproj -c Release --no-restore
观影助手.exe --self-test
```

- 交付消息必须说明：EXE 路径、是否为本次新构建、构建结果、自检结果和真实 UI 验收边界；
- 若构建失败、EXE 不是最新代码生成、或真实 UI 验收尚未完成，不得宣称功能已经完整交付；
- 文档-only 交付也必须给出当前可用 EXE；如果没有代码变化，可以复用最近一次通过验证的 EXE，但必须明确说明“本次未重新构建”；
- 正式 v0.9.0 EXE、ZIP、哈希和发布目录不得被替换；
- 当前已知的开发 EXE：

[观影助手.exe](D:/chatgpt/观影助手/开发/v1.0-douban-plus/bin/Release/net8.0-windows/win-x64/观影助手.exe)

## 十二、下一次开发对话的第一条指令

下一次开始实现时，先按本节给出方案、风险、准确文件范围、验证计划和 EXE 交付计划，等待确认后再修改代码：

> 继续执行 `DOUBAN_PLUS_WATCHLIST_HANDOFF_20260813.md` 的“个人页无限滚动”部分。目标是只对豆瓣个人页看过/想看/在看实现基于现有分页的后台加载和连续追加；待看页面保持现状。不得修改正式 v0.9.0，不得写入豆瓣 wish/do/collect，不得直接编辑生成后的 douban-plus.user.js。每次代码交付必须生成并提供隔离 v1.0 开发目录下的最新 Release EXE，同时报告构建、自检和真实 WebView2 验收结果。
