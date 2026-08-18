# Douban Plus 统一页面壳层与 DOM Source WebView 交接文档

日期：2026-08-14  
交接性质：下一轮新对话的架构起点与实施边界；本轮只写文档，未修改代码、未回滚、未生成 EXE/ZIP。

## 一、已经确认的方向

用户确认采用方案 1：

```text
可见统一 DoubanShell
        │
        │ JSON/消息桥
        ▼
隐藏 DOM Source WebView
        │
        ▼
豆瓣真实页面 DOM、真实登录态、真实筛选和加载
```

这不是自建数据库，也不是复制豆瓣数据。所有电影、电视剧、筛选项和加载结果仍然来自豆瓣真实 DOM；Provider 只是读取和转换适配层。

目标是把以下内容放到同一个可见应用壳层中：

```text
DoubanShell
├── 个人影片
├── 探索电影
├── 探索电视剧
└── 我的待看
```

个人、探索电影、探索电视剧和待看是同级内容模式，不再由各自页面生成完整标题区和导航，也不再让 Explore 作为“外置按钮 + 隐藏原生页面”的独立 UI 继续发展。

## 二、从哪个版本开始

### 2.1 新架构起点

新架构以以下稳定包作为功能和资源边界起点：

```text
D:\chatgpt\观影助手\开发\v1.0-douban-plus\artifacts\观影助手-v1.0.1-stable-douban-plus-20260813-091454-win-x64
```

版本信息：

- 版本：`v1.0.1 stable-v1.0.1`
- 生成时间：`2026-08-13 09:15:04 +08:00`
- EXE SHA256：`6FDFA6E4125F83B9E780D6A410F5D678636345D28432A332A55B3DD95226A702`
- 已确认能力：个人页、搜索页、待看、共享卡片、详情返回、双 WebView 既有基础
- 明确未纳入：Explore 页面适配

这个稳定包没有 `douban-explore-page.js/.css`，因此它是干净的 Explore 起点，不包含已经被现场证明不可靠的 Explore 代理筛选架构。

### 2.2 不得作为新架构起点的版本

以下版本只用于问题对照、差异分析和回滚保留，不得继续在其上修补统一页面：

- 当前开发实例：`观影助手-v1.0.1-dual-webview-explore-tv-ui-20260814-032545-win-x64`
- 必须保留的回滚基线：`观影助手-v1.0.1-dual-webview-explore-tv-ui-20260814-010555-win-x64`
- 更早的 Explore 开发包：`20260813-113118` 及之后所有 Explore 包

这些版本从一开始都采用了同一类不稳定方式：

- 自定义一套 Explore 按钮；
- 隐藏豆瓣 `#wrapper`；
- 通过脚本模拟点击豆瓣原生节点；
- 用定时器、MutationObserver 和状态签名猜测豆瓣完成时间。

`010555` 不是正确的架构基线，只是必须保留的回滚/问题对照包。

### 2.3 正式版本边界

以下内容不得修改或覆盖：

- 正式 `v0.9.0 BuildFix12 R11`；
- 稳定 `v1.0.0`；
- 稳定 `v1.0.1` 交付包及其哈希；
- `20260814-010555` 回滚基线。

新工作必须使用独立开发副本或新分支。当前 `v1.0-douban-plus` 工作副本含有失败 Explore 实验，不能直接视为干净源码起点。

注意：当前已保存的 `091454` 主要是完整交付包，包内不是完整 C# 源码仓库。新对话开始时应先恢复/确认“构建该稳定包的源码状态”，再创建统一 Shell 开发副本；不能因为当前工作目录存在就直接把 `032545` 源码当作稳定源码。

## 三、现有经验哪些可以复用

### 3.1 可以复用

以下是已经有价值、应保留的经验和组件：

| 能力 | 复用方式 |
|---|---|
| 个人页 DOM 解析 | 改造成 `PersonalProvider` 的数据读取逻辑，不改变已确认的分页/恢复规则 |
| 个人页无限滚动 | 迁入 Provider 的 `loadMore`，保留请求锁、末页和恢复边界 |
| 统一卡片 | 继续使用 `douban-card.js/.css`，Provider 只提供统一字段 |
| 详情双 WebView | 保留 `_doubanSubjectView` 和返回/恢复协议，Shell 只发送统一详情消息 |
| 登录态 | Source WebView 与 Shell 使用同一 WebView2 environment/profile，继续使用现有豆瓣登录态 |
| WebView2 注入 | 复用 `DoubanPlusWebView2Script.cs` 的 bundle、Card、资源加载和文档创建时机经验 |
| 评价/状态/删除后端 | 保留 `DoubanWebView2Connector`、Detail/Worker 隔离和官方回读协议 |
| 本地待看 | 保留独立本地存储，不改成豆瓣官方 wish，不与 Source DOM 混为一谈 |
| 失败/恢复探针 | 复用真实内容探针、页面就绪判断和失败重试边界 |
| Explore DOM 事实 | 复用已确认的选择器、卡片字段、加载更多节点和电影/电视剧差异，但不复用旧 UI 代理代码 |

### 3.2 明确禁止复用

以下代码和思路不得作为新 Shell 的交互基础：

- `douban-explore-page.js` 当前自定义按钮和自定义弹层；
- `wrapper.style.display = "none"` 后在同一脚本内模拟原生点击；
- `clickNative()` 作为用户可见筛选按钮的主链路；
- 以旧 `li`、旧 `.base-selector` 或旧闭包节点驱动后续操作；
- 通过 `80ms/120ms/180ms/260ms/500ms` 等固定延时判断页面状态；
- 用 `filterOperationBusy` 禁用整组按钮作为页面同步机制；
- 在 Shell 内复制豆瓣筛选控件并等待隐藏原生页面结果；
- 继续向当前 Explore 脚本增加补丁式 MutationObserver、重绘或回滚逻辑。

## 四、目标架构

### 4.1 可见 Shell

可见 `_doubanPlusView` 不再承载某个具体豆瓣页面，而是承载统一 Shell 文档。Shell 负责：

- 标题区；
- 全局搜索入口；
- 一级主导航；
- 电影/电视剧切换；
- 当前模式和筛选状态显示；
- 统一卡片网格；
- 加载、空结果、错误和重试状态；
- 详情打开消息；
- 当前模式、筛选和滚动位置保存。

Shell 不负责：

- 解析豆瓣原始 DOM；
- 直接查询 `movie.douban.com` 的原生节点；
- 自己猜测豆瓣 API 参数；
- 保存影片数据库。

建议新文件：

```text
WebAssets\DoubanPlus\douban-shell.js
WebAssets\DoubanPlus\douban-shell.css
```

### 4.2 DOM Source WebView

新增隐藏 `_doubanSourceView`，只负责加载真实豆瓣页面并提供 DOM 数据源：

- 个人页：`/people/{id}/collect`、`wish`、`do`；
- 电影探索：`/explore`；
- 电视剧探索：`/tv/`；
- 必要时加载当前模式对应的真实豆瓣页面。

Source WebView 负责：

- 真实页面导航；
- 真实 DOM 就绪探针；
- 从 DOM 读取卡片、筛选组、选项和分页状态；
- 在 Source 文档内执行真实筛选和加载更多；
- 将结构化 JSON 结果发送给 C# 宿主。

Source WebView 不负责：

- 生成 Douban Plus 可见导航；
- 生成共享卡片 UI；
- 直接控制可见 Shell 的布局。

建议文件：

```text
WebAssets\DoubanPlus\douban-source-bridge.js
```

Source 文档可以继续显示豆瓣原生页面，但它不应再作为用户可见界面。隐藏 Source WebView 时必须先验证 WebView2 在非可见状态下仍能完成 DOM 加载、脚本执行和消息返回；如果豆瓣事件链确实要求可信输入，只在 Source WebView 内增加受控的 WebView2/CDP 输入桥，不回到 Computer Use，也不把外置按钮直接绑定隐藏 DOM。

### 4.3 C# 宿主消息桥

C# 宿主是 Shell 和 Source 的唯一中间层：

```text
Shell → postMessage(command) → C# → Source WebView
Source WebView → postMessage(result) → C# → Shell
```

命令必须带 `requestId`、`mode`、`generation`：

```json
{
  "type": "sourceCommand",
  "requestId": "explore-movie-001",
  "mode": "explore-movie",
  "generation": 4,
  "action": "readPage"
}
```

结果必须带同样的关联信息：

```json
{
  "type": "sourceResult",
  "requestId": "explore-movie-001",
  "mode": "explore-movie",
  "generation": 4,
  "ok": true,
  "items": [],
  "filters": [],
  "loading": false,
  "endReached": false
}
```

旧请求、旧页面、旧模式的结果不得更新当前 Shell。超时必须可取消，并明确显示重试，而不是继续锁住按钮。

建议调整文件：

```text
DoubanPlusWebView2Script.cs
HtmlMediaLibraryForm.cs
```

宿主新增职责：

- 创建并初始化 Source WebView；
- 确保 Shell 和 Source 使用同一登录态环境；
- 路由 Source 导航；
- 转发命令和结果；
- 保留详情 WebView 独立职责；
- 在关闭/崩溃/导航代次变化时取消旧请求。

## 五、Provider 设计

Provider 不是数据库，不持久化全量影片。它只代表“如何从 Source DOM 读取当前模式”。

```text
PersonalProvider
ExploreMovieProvider
ExploreTvProvider
WatchlistProvider
```

统一输出字段至少包括：

```text
SubjectId
SubjectUrl
Title
Subtitle
Score
Rating
Poster
Year
ContentType
SourceMode
```

Provider 接口建议包括：

```text
Open(mode)
ReadCurrentPage()
ReadFilters()
ApplyFilter(filterId, value)
LoadMore()
Cancel(generation)
```

Explore 电影和电视剧共享接口和卡片字段，但不共享硬编码筛选组：

- 电影筛选以电影 Source DOM 实际存在的组和选项为准；
- 电视剧筛选以电视剧 Source DOM 实际存在的组和选项为准；
- 不把电影的 `未看过`、类型、地区或排序配置硬编码复制到电视剧；
- Provider 读取结果必须带 `contentType`，防止两种模式互相污染。

## 六、实施阶段

### 阶段 0：冻结和建立新副本

1. 保留当前 `032545` 作为失败问题样本。
2. 保留 `20260814-010555`，禁止覆盖。
3. 以 `v1.0.1 stable 091454` 的源码/资源状态建立新隔离副本。
4. 确认新副本不包含 `douban-explore-page.js/.css` 的旧 Explore 交互层。
5. 先让稳定版个人页、详情返回、待看和后端自检保持原样。

阶段 0 不生成交付 EXE/ZIP，不修改正式版本。

### 阶段 1：只建立 Shell 和消息桥

目标：Shell 能启动、显示统一导航和空状态；Source WebView 能加载个人页或电影 Explore；Shell 与 Source 能完成一次 `readPage` 往返。

不得在这一阶段加入筛选、无限滚动或电视剧 UI。

验收：

- Shell 可见；
- Source WebView 不抢占用户可见区域；
- 登录态可用；
- `requestId/generation` 正确关联；
- Source 页面失败不会让 Shell 永久卡住。

### 阶段 2：接入个人页 Provider

先迁移已经由用户确认稳定的个人页：

- 看过/想看/在看；
- 当前页卡片读取；
- 无限滚动；
- 详情打开和返回；
- SubjectId/scrollY 恢复。

此阶段的意义是先验证统一 Shell 不破坏稳定能力。

### 阶段 3：只接入 ExploreMovieProvider

最小范围：

1. Source 加载 `https://movie.douban.com/explore`。
2. 从真实 `.subject-list-list` 读取电影卡片。
3. Shell 显示共享卡片。
4. Shell 点击卡片进入现有详情 WebView。
5. 返回后保留 Shell 模式、卡片和滚动位置。

这一阶段暂不实现复杂筛选，先证明“Source DOM → JSON → Shell 卡片”链路。

### 阶段 4：接入电影真实筛选和加载更多

筛选原则：

- Shell 只显示根据 Source DOM 读取出的筛选状态；
- 点击 Shell 筛选按钮后发送命令给 Source；
- Source 在自己的真实豆瓣文档内操作原生控件；
- Source 等待真实结果 DOM 就绪后一次性返回新卡片和状态；
- Shell 不查询隐藏 DOM，不复制旧 `.base-selector` 节点；
- 不用固定 6 秒，也不把固定延时当作成功条件。

先验证电影：一级模式、类型、地区、年代、排序、评分、复选项、加载更多。

### 阶段 5：接入 ExploreTvProvider

电影链路稳定后再接电视剧：

- `/tv/` 作为 Source URL；
- 电视剧真实一级入口；
- 电视剧实际存在的筛选组；
- 电视剧卡片字段、类型和首播年份；
- 电视剧无限滚动和详情返回。

不能从电影 Provider 复制五组筛选配置。

### 阶段 6：接入待看和完整导航

最后接入：

- 我的待看；
- 个人 → 探索电影 → 探索电视剧 → 待看 → 个人；
- 浏览器后退/前进；
- 刷新和登录态恢复；
- 详情双 WebView 返回；
- 错误恢复和 Source 崩溃重建。

## 七、建议文件范围

### 新增

```text
WebAssets\DoubanPlus\douban-shell.js
WebAssets\DoubanPlus\douban-shell.css
WebAssets\DoubanPlus\douban-source-bridge.js
tests\validate_douban_shell.py
tests\validate_douban_source_bridge.py
```

### 逐步调整

```text
DoubanPlusWebView2Script.cs
HtmlMediaLibraryForm.cs
WebAssets\DoubanPlus\douban-personal-page.js
WebAssets\DoubanPlus\douban-personal-page.css
WebAssets\DoubanPlus\douban-watchlist.js
WebAssets\DoubanPlus\douban-watchlist.css
tests\validate_douban_plus_only.py
tests\validate_embedded_scripts.py
```

### 暂不作为新实现基础

```text
WebAssets\DoubanPlus\douban-explore-page.js
WebAssets\DoubanPlus\douban-explore-page.css
```

它们可作为 DOM 选择器、字段解析和历史问题证据参考，但不应直接迁移其中的自定义筛选 UI、隐藏 `#wrapper`、`clickNative`、旧状态机和定时器重绘链路。

## 八、第一轮必须验证的最小闭环

新对话开始后，不要直接做全部功能。第一轮只完成并验证：

```text
启动 Shell
→ Source WebView 加载电影 Explore
→ Source 读取真实电影卡片
→ C# 转发 JSON
→ Shell 显示共享卡片
→ 点击卡片进入现有详情 WebView
→ 返回 Shell 后卡片仍在
```

最小闭环通过后，才允许加入筛选。

## 九、最终真实验收顺序

静态测试和 Release 编译不能代替真实登录态 WebView2 验收。最终必须按以下顺序：

1. 个人页进入、切换状态、无限滚动。
2. 个人页卡片进入详情、返回并恢复位置。
3. 进入探索电影，确认无豆瓣原始页面闪现。
4. 探索电影卡片读取、加载更多、详情返回。
5. 探索电影一级模式和筛选二次筛选。
6. 进入探索电视剧，确认筛选项来自电视剧真实 DOM。
7. 电视剧加载更多、详情返回、二次筛选。
8. 电影/电视剧/个人/待看来回切换。
9. 刷新、后退、前进、登录失效和网络失败恢复。
10. Source WebView 取消、超时、崩溃后 Shell 解锁并可重试。

完成判定：

- 不依赖自建影片数据库；
- 所有内容来自豆瓣真实 DOM；
- Shell 是唯一可见页面壳层；
- Source WebView 不抢占可见 UI；
- 个人、电影探索、电视剧探索、待看共享导航和卡片；
- 筛选支持连续二次操作，不因旧 DOM、旧请求或超时永久锁死；
- 详情双 WebView 返回不丢模式、筛选、卡片和滚动位置；
- 正式 v0.9.0、稳定 v1.0.0、稳定 v1.0.1 和 `20260814-010555` 均未被覆盖；
- 真实登录态 WebView2 序列重复通过后，才生成新的 EXE/ZIP。

## 十、给下一轮对话的启动语句

```text
阅读 DOUBAN_PLUS_UNIFIED_SHELL_SOURCE_WEBVIEW_HANDOFF_20260814.md。
不要继续修当前 douban-explore-page.js，也不要回滚覆盖 20260814-010555。
从 v1.0.1 stable 091454 的稳定能力建立独立副本，先实现阶段 0/1：统一可见 DoubanShell + 隐藏 DOM Source WebView + C# JSON 消息桥。
第一轮只完成电影 Explore 的 readPage 最小闭环：真实 DOM 读取 → JSON → Shell 卡片；暂不加入筛选、无限滚动和电视剧。
```
