# Douban Plus：选电影页适配交接文档

更新时间：2026-08-13

工作副本：`D:\chatgpt\观影助手\开发\v1.0-douban-plus`

当前稳定版本基线：`v1.0.1`。本文件记录当前工作副本的 Explore 实现和验收边界；正式稳定产物的纳入范围仍以 `STABLE_VERSION_V1.0.1.json` 为准。详情返回的双 WebView 后续方案见 `DOUBAN_PLUS_DUAL_WEBVIEW_PLAN_20260813.md`。

> **2026-08-13 回归警告（以用户现场截图为准）**：此前记录的“电视剧点击全部后 5 组筛选已修正”不能视为完成结论。当前现场仍存在电视剧首页提前显示完整筛选按钮、筛选组标题出现“华语”错误，以及电影 Explore UI 被共享筛选改动连带破坏的问题。必须先初步筛选完成、原生 DOM 稳定后才显示完整筛选组；首页初始状态只显示电视剧三个一级入口。详细问题、证据和下一次验收序列见 `DOUBAN_PLUS_EXPLORE_FILTER_REGRESSION_HANDOFF_20260813.md`。本轮仅更新交接文档，未修复代码和未重建 EXE/ZIP。

## 一、当前状态

本次已完成以下处理：

1. 搜索页无限滚动实验已回滚。
2. 搜索页恢复为“当前页解析 + 豆瓣原生分页链接”。
3. 搜索页不再执行下一页 `fetch`、`DOMParser`、iframe 加载或 `IntersectionObserver` 自动追加。
4. 个人页无限滚动保留，不做回滚。
5. 正式 v0.9.0 发布目录未修改。

当前页面能力边界：

| 页面 | 当前状态 |
|---|---|
| 个人页 `/people/{id}/collect`、`wish`、`do` | 已实现并由用户确认无限滚动正常 |
| 搜索页 `search.douban.com/movie/subject_search` | 仅保留当前页展示和原生分页 |
| 选片页 `movie.douban.com/explore`、电视剧页 `movie.douban.com/tv/` | 开发版已接入同一 Explore 适配器，待宿主 WebView2 端到端验收 |

> 2026-08-13 实施更新：上表中 Explore 一行已由“尚未接入”进入“开发版已接入，待宿主 WebView2 端到端验收”。以下“下一步目标”和“推荐实施路线”保留为前期规划记录；真实探针结果与当前实现以本节为准。

## 1.1、真实探针与当前实现（2026-08-13）

真实页面确认：Explore 使用窗口滚动；筛选状态不写入 URL；原生“加载更多”会向 `m.douban.com/rexxar/api/v2/subject/recent_hot/movie` 发起带 `category`、`type`、`start`、`limit` 的请求。实现因此代理豆瓣原生筛选和分页控件，不在适配器中猜测或重造查询参数。

当前开发版新增：

- `WebAssets\DoubanPlus\douban-explore-page.js/.css`：独立根节点 `#qb-douban-explore-root`；复用 `.subject-list-list` 解析电影与电视剧卡片，排除片单节点；
- 个人页“探索”入口，以及 Explore 内电影 / 电视剧类型切换；
- 分类、地区、评分区间、未看过、可播放筛选代理；
- 原生“加载更多”代理升级为窗口滚动哨兵自动加载，新增请求锁、SubjectId 去重和追加渲染；
- 卡片进入独立详情 WebView，返回时只切换回列表 WebView，保留已加载卡片和滚动位置；
- 探索卡片沿用现有右键“加入本地待看”菜单，来源标记为 `explore`。

2026-08-13 电视剧适配补充：

- 电视剧页实际原生筛选只有 `可播放` 复选项，没有电影页使用的 `未看过` 复选项；适配器现在按原生 DOM 动态渲染复选按钮，不再显示无效的“未看过”。
- 电影仍按原生实际控件显示 `未看过` / `可播放`；评分区间、电视剧分类、卡片首播年份、加载更多、SubjectId 去重、详情来源和列表状态恢复继续共用 Explore 链路。
- 该差异来自电视剧页真实 DOM，不通过猜测补造豆瓣筛选参数。

2026-08-13 电视剧“全部”筛选修正：

- 真实 DOM 核验确认：电视剧非“全部”模式使用 `explore-menu-second-tag` 平级分类；点击“全部”后切换为 5 个 `.base-selector` 下拉组：类型、地区、年代、平台、排序。
- 电影与电视剧现在共用同一套“全部模式”筛选读取、下拉选项提取和原生点击链路；分组名称、数量和当前值从实时 DOM 动态读取，不再用电影 4 组配置排除电视剧。
- 修复了此前电视剧“全部”读取错误分支导致筛选栏为空的问题。验证重点为：电视剧点击“全部”后 5 个筛选按钮出现，逐个打开下拉并读取 `.expand-card .drc-label` 选项。

已在真实 Explore 页面验证：重绘根节点成功；“豆瓣高分”筛选成功；“未看过”状态成功；加载更多由 20 项增加到 40 项；URL 保持 `https://movie.douban.com/explore`。另已确认 `https://movie.douban.com/tv/` 使用同结构 `.subject-list-list` 和电视剧卡片字段，可复用同一解析链路。宿主详情导航、返回恢复和登录态 WebView2 端到端操作仍需在开发版 EXE 中验收，未宣称已完成正式 UI 验收。

## 1.2、返回恢复与全页面刷新补丁（2026-08-13）

已确认详情返回失败的根因：宿主在 Explore 原生 DOM 尚未完成异步挂载时，约 300ms 后就触发二次导航，第二次仍未看到 `#qb-douban-explore-root` 后提前判定恢复失败。当前修复对 Explore 使用最长 12 秒的条件等待，并在显示成功前再次确认导航 ID 仍有效。

同时，现有全页面右键菜单增加“刷新页面”：无卡片区域右键直接显示刷新；卡片区域保留“加入本地待看”并附加刷新项。刷新请求由宿主校验来源后执行 WebView2 Reload，并写入 `PageRefreshRequested` 日志。

## 1.3、选电影页无限滚动实现（2026-08-13）

本次实现范围只覆盖 Explore 列表 WebView；双 WebView 的详情返回和已加载列表保留不再重复改动。分页仍由豆瓣原生“加载更多”按钮驱动，适配器不猜测接口、不自行组装 `start/limit` 参数。

实现链路：

1. 在列表页底部放置 `IntersectionObserver` 哨兵，距底部约 720px 时触发一次原生“加载更多”；
2. 通过 `state.loading` 和 `loadGeneration` 防止并发加载，并等待原生卡片集合发生稳定变化；
3. 读取原生 `.subject-list-list`，以 `SubjectId` 去重，只把新卡片追加到 Douban Plus 网格，不全量重建已显示卡片；
4. 原生按钮不存在、被禁用或没有新增 SubjectId 时进入末页状态；超时保留“加载更多”重试入口并写入探针错误；
5. 切换模式、分类、地区、评分和复选筛选时重置本轮分页状态，避免跨筛选复用旧的结束标志。

探针新增 `autoInfinite`、`loadCount`、`endReached`、`endReason`、`lastLoadDurationMs`、`lastLoadTrigger` 字段，便于区分自动触发、手动重试、末页和超时。已完成静态脚本检查；真实登录态 WebView2 的连续滚动、末页和网络失败验收仍需在交付 EXE 中确认。

## 1.4、与个人影片页统一为同级页面（2026-08-13）

Explore 继续保留独立 `/explore` 路由，电视剧选片使用 `/tv/`；两者都保留原生 DOM 分页，不把原生内容强行嵌入个人页文档。页面层面统一为 Douban Plus 同级导航：个人页与 Explore 使用“看过 / 想看 / 在看 / 探索”四项主导航，Explore 另提供电影 / 电视剧类型切换。

个人页和 Explore 均接入统一搜索宿主；页面下滑超过约 120px 后显示固定搜索入口，搜索建议、键盘导航和搜索请求继续复用现有 `SubjectSwitcher`。列表页不显示宿主返回按钮，只有进入影片详情后显示统一文案“返回”。双 WebView 详情返回机制不变，只切换回原列表 WebView。

## 二、本次回滚范围

已修改：

- `vendor\douban-plus-1.8.1\src\modules\subject\runtime\search-page-mount.tsx`
- `vendor\douban-plus-1.8.1\src\modules\subject\styles\search-page.css`
- `WebAssets\DoubanPlus\douban-plus.user.js`
- `WebAssets\DoubanPlus\douban-plus.meta.js`
- `tests\validate_douban_personal_page.py`

搜索页现在只保留：

- 当前文档中的搜索结果提取；
- 当前页卡片渲染；
- 原生分页链接渲染；
- 页面 DOM 变化观察，用于等待当前页结果完成。

已移除的搜索页实验能力包括：

- 自动下一页页码推导；
- 搜索页分页 HTML 请求；
- iframe fallback；
- 自动追加结果；
- 搜索页无限滚动 sentinel、加载状态和重试按钮。

个人页实现位于：

- `WebAssets\DoubanPlus\douban-personal-page.js`
- `WebAssets\DoubanPlus\douban-personal-page.css`

个人页不得因为选电影页适配而直接改写。它应继续作为已经由用户确认的稳定样板。

## 三、构建与校验结果

已执行：

- `vendor\douban-plus-1.8.1\node_modules\.bin\vite.cmd build`：通过；
- `python tests\validate_douban_personal_page.py`：通过；
- 搜索页无限滚动关键标记检查：通过移除；
- 正式 v0.9.0 文件哈希检查：通过，未变化。

当前构建产物：

- `vendor\douban-plus-1.8.1\dist\douban-plus.user.js`
- `WebAssets\DoubanPlus\douban-plus.user.js`
- `WebAssets\DoubanPlus\douban-plus.meta.js`

类型检查仍存在一个与本次页面回滚无关的既有错误：

- `vendor\douban-plus-1.8.1\src\shared\utils\request.ts:24`
- 原因是 TypeScript `exactOptionalPropertyTypes` 下，`body: string | undefined` 不符合 `RequestInit` 类型。

本次未修改该文件，也未借此扩大修复范围。

真实 WebView2 UI 验收尚未由本次工作代替；最终需要用户在开发版 EXE 中自行确认。

## 四、历史规划记录：选电影页适配

目标页面：

`https://movie.douban.com/explore?support_type=movie&is_all=false&category=%E7%83%AD%E9%97%A8&type=%E5%85%A8%E9%83%A8`

目标行为：

1. 进入选电影页后保留当前筛选条件。
2. 保留第一页电影卡片。
3. 用户下拉接近底部时加载下一页。
4. 下一页电影卡片追加到当前列表末尾。
5. 不跳转页面，不覆盖已经加载的卡片。
6. 电影详情链接、海报和必要信息保持可点击、可渲染。
7. 到达末页后停止加载。
8. 请求失败时显示明确状态，并保留原生分页或重试入口。

## 五、推荐实施路线

### 阶段 1：只做页面探针

先不要写无限滚动。确认真实页面结构：

- 选电影页的主列表容器；
- 单个电影卡片节点；
- 电影详情链接和 subject id；
- 下一页链接或分页状态；
- 筛选条件是否包含在下一页 URL；
- 页面是否由脚本异步生成卡片；
- 当前滚动容器是 `window` 还是内部 `overflow: auto` 容器。

探针必须只读，不替换页面，不发起额外分页请求。

### 阶段 2：新增独立 `ExplorePageAdapter`

在现有插件内新增选电影页适配器，建议目录：

```text
vendor\douban-plus-1.8.1\src\modules\explore\
  index.ts
  runtime\mount.tsx
  runtime\extract.ts
  styles\page.css
```

适配器职责只包括：

- 判断 `/explore` 页面；
- 读取当前筛选条件；
- 识别电影卡片；
- 识别下一页；
- 将下一页卡片追加到选电影页列表。

`main.ts` 只注册 `explorePage`，不改变个人页和搜索页的挂载逻辑。

### 阶段 3：再抽取公共分页机制

选电影页通过真实 UI 验证后，再考虑抽取公共加载器：

- 请求锁；
- `IntersectionObserver` sentinel；
- 下一页 URL 去重；
- subject id 去重；
- 结束判断；
- 加载中、失败、重试状态；
- 页面卸载时断开 observer 和取消请求。

公共加载器只能抽取机制，不能把个人页、选电影页和搜索页的 DOM 选择器混在一起。

## 六、隔离规则

必须保持以下关系：

```text
URL 路由
  ├─ /people/{id}/collect|wish|do  → PersonalPageAdapter
  ├─ /explore、/tv                 → ExplorePageAdapter（电影 / 电视剧）
  └─ search.douban.com/...        → SearchPageAdapter（仅原生分页）
```

规则：

- 一个页面只能挂载一个页面适配器；
- 个人页继续使用 `douban-personal-page.js`；
- 选电影页不得调用个人页的 DOM 选择器；
- 搜索页不得重新启用本次回滚的自动加载实验；
- 每个适配器拥有独立的 observer、请求锁、去重集合和生命周期；
- 适配器根节点必须有唯一 owner 标记，重复注入直接退出；
- 不使用 iframe 作为分页解析主路径；
- 不直接猜测或硬编码未经运行时确认的豆瓣内部接口；
- 不修改正式 v0.9.0 EXE、ZIP 或其哈希文件。

## 七、数据来源优先级

按以下顺序选择：

1. 选电影页当前 DOM 中已经存在的原生下一页链接 + HTML 解析；
2. 页面自身已经发出的、可确认的同源结构化请求；
3. WebView2 页面级请求/响应观察；
4. 最后才考虑严格 allowlist 的宿主 HTTP 桥接。

不要把旧的第三方豆瓣接口示例直接当作当前协议。先在真实登录 WebView2 中确认当前 URL、响应类型和字段。

## 八、风险与处理

### 1. 页面是动态渲染的

风险：请求下一页 HTML 可能只有空壳，解析不到电影卡片。

处理：优先捕获真实页面请求；如果只能导航加载，则应使用页面级请求/响应观察，不用隐藏 iframe 作为主方案。

### 2. 下一页链接不是普通分页

风险：筛选条件可能由 query、hash 或脚本状态保存，简单拼接 `page=2` 会丢筛选条件。

处理：从当前页面实际分页控件或实际网络请求读取下一页，按完整 URL 传递筛选状态。

### 3. 卡片选择器变化

风险：豆瓣页面结构变化会导致“没有可解析电影结果”。

处理：选择器集中在 `ExplorePageAdapter`，增加 subject id、详情链接和卡片数量诊断；解析失败时保留原生页面。

### 4. 重复加载或重复挂载

风险：MutationObserver、IntersectionObserver 和页面重新渲染互相触发，造成重复请求或卡片重复。

处理：每页只有一个 adapter；请求锁、URL 去重、subject id 去重三层保护；页面离开时清理 observer。

### 5. 无限列表过长造成性能下降

风险：长期追加所有卡片会增加 DOM 和图片内存。

处理：第一版先不引入虚拟列表；设定最大连续加载页数和明确停止状态，验证后再评估虚拟化。

## 九、验收标准

静态/构建验收：

- 选片页适配器只匹配 `/explore` 与 `/tv`；
- 搜索页源码和 bundle 中不存在搜索无限滚动实验标记；
- 个人页静态校验继续通过；
- `vite build` 通过；
- 正式 v0.9.0 文件哈希不变。

真实 UI 验收：

- 第一页能正常显示；
- 不点击分页，直接向下滚动可以加载第二页；
- 连续加载至少三页时没有重复电影卡片；
- 改变筛选条件后从新结果集重新开始，不混入旧条件结果；
- 详情链接仍能进入对应电影页；
- 加载失败时不白屏、不崩溃，原生分页仍可用；
- 到末页后不出现无限重复请求；
- 个人页 collect/wish/do 仍按原已验证方式工作；
- 搜索页只进行原生分页，不自动追加。

## 十、可直接交给下一次开发的指令

> 阅读 `DOUBAN_PLUS_EXPLORE_ADAPTER_HANDOFF_20260813.md`。
>
> 在 `D:\chatgpt\观影助手\开发\v1.0-douban-plus` 开发副本中，先对当前豆瓣 `/explore` 页面做只读 DOM/网络探针，确认主列表、电影卡片、subject id、下一页来源和滚动容器。不要修改个人页，不要恢复搜索页无限滚动，不要使用 iframe 或未经确认的豆瓣接口。探针结果确认后，再新增独立 `ExplorePageAdapter`，先实现选电影页适配，再考虑抽取公共分页机制。每次代码交付必须同步提供最新 Release EXE、构建结果、静态自检结果和真实 UI 验收边界；正式 v0.9.0 发布物保持不变。

## 十一、参考实现

通用无限滚动方案可参考：

- [XIU2/UserScript Autopage](https://github.com/XIU2/UserScript/tree/master/other/Autopage)：规则驱动的下一页识别与页面追加；
- [researchgate/react-intersection-list](https://github.com/researchgate/react-intersection-list)：sentinel + IntersectionObserver；
- [Microsoft WebView2 WinForms 示例](https://github.com/MicrosoftDocs/edge-developer/blob/main/microsoft-edge/webview2/get-started/winforms.md)：页面脚本注入和宿主通信。

这些项目只作为通用机制参考，不代表豆瓣当前页面的选择器、接口或响应协议。
