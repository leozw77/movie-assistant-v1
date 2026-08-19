# 2026-08-19：Personal Store Refactor v2（状态秒切 + BoundedSync + Store 不变量）

本轮完成 Personal Store Refactor 的 entry/sync 闭环：

- `看过 / 想看 / 在看` 状态切换优先直接 `Store.Query(status)`，已有完整 Store 时不再经过 Provider 全量建立路径，页面先本地秒开。
- 状态切换后仅后台请求 Frodo 首屏做 BoundedSync；`CloudTotal +N` 最多向后扫描 5 页，找到 N 个陌生 SubjectId 即停止。
- `CloudTotal < LocalTotal` 不再 invalidate/remove 完整 Store；旧 Store 继续服务 UI，并在后台运行 DeletionReconcile，完整扫描稳定 cloud ids 后只删除确认不存在的条目。
- 同 total 出现陌生 SubjectId、delta 不可解释、扫描上限未找齐等情况只记录 `NeedsDeepReconcile`，不清空 Store，也不会从普通状态切换隐式触发全量 Build。
- 完整扫描入口收敛为 `BootstrapStatusAsync`（真正无缓存时）与 `ForceFullReconcileAsync`（显式恢复入口）；普通刷新只做 BoundedSync。
- Full snapshot 的不变量：已有可用 Store 在任何同步路径中都不能先清空；新快照完整成功后才一次替换当前 snapshot。
- Frodo 远端字段整体覆盖本地镜像字段；仅当新响应 `InterestId` 为空时保留已有非空远端 InterestId。`InterestId` 仍来自 Frodo `interest.id`，不是本地生成 ID。
- 保留固定 source-slot 游标 `start=0,20,40...`，不修改评价写入/删除/官方回读核心。

# 2026-08-19：Personal Store Refactor v1（单一权威库 + total 最小增量）

开发分支仍为 `chatgpt/frodo-personal-20260819`，main 不修改。本轮不再给 Provider/Index 分叉继续加同步补丁，而是把现有 `FrodoPersonalIndexService` 提升为唯一持久化 Personal Store；Provider 只保留 UI 当前页的瞬时分页缓冲。

- Provider 初次加载会先恢复 Store cache；每个 Frodo interests 页面映射后立即交给 `ReconcileRemotePageAsync` UPSERT 云端整条记录。
- `CloudTotal == LocalTotal`：只更新当前已观察到的同 SubjectId 云端字段，不扫描全库。
- `CloudTotal > LocalTotal`：`RequiredAdds = CloudTotal - PreviousTotal`，从当前 source-slot 开始继续读取，找到 RequiredAdds 个本地目标状态不存在的 SubjectId 就停止；仍严格使用 `page.Count`/PageSize 固定 source-slot 游标。
- 状态迁移：目标状态出现的新 SubjectId 会同时从其它已完成状态 Store 快照移除；完整状态重建也做同样去重，避免同一 Subject 同时存在 wish/collect/do。
- `CloudTotal < LocalTotal` 或 total 无法解释观察到的结构变化时，不猜测删除项，而是移除该状态 complete 标记，让已有 `EnsureFrodoPersonalIndexAsync` 后台只重建该状态。
- `interest.id` 进入模型为 `InterestId`，cache schema v4，因此从 Step 5 升级后首次会重建旧 v3 cache。
- fresh Frodo whole-row 优先：传入 authoritative item 时不再用旧本地 `Rating/Comment/MarkedDate` 覆盖，日志记录 Authority / InterestId / LocalRating / AppliedRating。
- 自检新增：InterestId、total+1 增量、wish->collect 状态迁移、authoritative precedence。
- RSS 已经实测无法发现“很老影片只改评分”，本轮不接 RSS。手机静默修改老评分且 total 不变仍是独立边界，必要时走完整 reconcile。
- `ReviewWriteCoordinator.cs` / `ReviewWriteVerifier.cs` / `ReviewTargetResolver.cs` / `ReviewWriteModels.cs` 不修改；详情、Explore、搜索、Douban Plus、官方写入/删除/回读不迁移。

# 2026-08-19：个人库筛选 Step 5 已修复新增 UPSERT / 可播放 / 评分滑块

开发分支继续为 `chatgpt/frodo-personal-20260819`。本轮基于已实机运行的 Step 4，不修改 main。

- 新增评价不能再只修改已有 Index：`ApplyConfirmedReviewAsync` 支持 authoritative item INSERT；写入官方确认后若完整索引找不到 SubjectId，会短重试 Frodo 最新个人页，找到后同时 UPSERT Provider 与完整 Index。
- 这直接修复“新片刷新后默认页可见，但一应用完整库筛选就消失”的 Provider/Index 分叉。
- Playable 的真实个人 interests 字段为 `subject.has_linewatch`；不再使用 `is_playable` 猜测。兼容 `actions=可播放` / 非空 `linewatches`，缓存 schema 为 v3。
- 豆瓣评分本地 Query 已证实正常，本轮只替换前端交互：自定义双滑块、连续拖动、整数 1 分吸附。
- 不修改 `ReviewWriteCoordinator.cs` / `ReviewWriteVerifier.cs` / `ReviewTargetResolver.cs` / `ReviewWriteModels.cs`；官方回读仍是写入成功唯一确认依据。

# 2026-08-19：个人库筛选 Step 4 已统一

开发分支继续为 `chatgpt/frodo-personal-20260819`。本轮基于 Step 3 实机版本收敛个人页筛选架构：所有常用个人筛选由一个 Frodo 完整索引 criteria 执行，不再把“在线观看”切回 DOM。

最终 UI：

- 第一层固定一行：状态 / 影片类型 / 排序 / 可播放 / 豆瓣评分 / 筛选。
- 豆瓣评分点击后弹 0-10 双滑块；范围直接映射 `subject.rating.value`。
- 高级筛选第二层只显示：我的评分 / 年代 / 地区 / 题材；点击其中一个后，下方仅展示该分类选项。
- 年代直接混排近 5 个具体年份 + 更早年代，不再维护独立“年份”入口。
- `subject.is_playable` 映射入模型，index schema v2 强制旧缓存重建。
- 写入/删除官方确认后的即时索引同步与双评分卡片继续保留。

边界：`ReviewWriteCoordinator.cs` / `ReviewWriteVerifier.cs` / `ReviewTargetResolver.cs` / `ReviewWriteModels.cs` 仍不修改；Provider 固定 source-slot cursor 逻辑仍不修改。

# 2026-08-19：个人库筛选 Step 3 已接入

开发分支继续为 `chatgpt/frodo-personal-20260819`，基于 Step 2 实机 UI 版本继续。

本轮核心：

- 官方保存/删除确认后，不再等待下一次 Frodo 全库重建；完整索引和 Provider 已加载项同步修正。
- 活跃本地筛选会立即重新 Query；普通列表通过 `doubanShellPersonalItemMutation` 原地更新星级或移除条目。
- 第一层 UI 保持原个人页结构，只额外增加 `筛选`；高级面板只包含评分、年代、年份、地区、题材。
- 第一层 `正在热映/在线观看` 仍是 DOM 网页条件；高级面板不再重复这些入口。
- 卡片现在区分 `score`（豆瓣评分，右下）和 `myRating`（个人星级，左下）。
- 演员筛选继续暂缓，直到确认 Frodo 返回的人物字段足够完整。

# 2026-08-19：个人库筛选 Step 2 UI 已接入

开发分支继续为 `chatgpt/frodo-personal-20260819`。Step 1 的完整索引基础已经实机编译并推送；本轮把它接到统一 Shell。

行为：

- 打开看过/想看/在看时，现有 Provider 先给首屏；完整索引异步建立，不阻塞首屏。
- 索引未完成时只显示“正在建立完整个人库筛选索引”，不会对局部数据给出伪完整筛选结果。
- 索引就绪后出现：影片类型、我的评分/未评分、年份、地区、题材、排序。
- 本地筛选使用 `FrodoPersonalQuerySession` 按 20 部分页；Shell 无限滚动继续复用 `doubanShellLoadMore`。
- `可播放` / `有视频` 继续作为 DOM 网页条件保留。
- 状态切换会退出当前本地 query session，但已完成的状态索引继续保存在 `frodo-personal-index-v1.json` 中。
- 评价写入、删除、官方回读文件仍未修改。

# 2026-08-19：个人库筛选 Step 1 后端基础

当前开发分支继续为 `chatgpt/frodo-personal-20260819`。本轮从 v13.1（`983f9e408d43b89bcb169a3cff92a821f5adfedb`）继续：只加入日志瘦身、Frodo response 字段名探针和独立 `FrodoPersonalIndexService`。

关键边界：

- `FrodoPersonalProvider` 已实机通过的固定 `0/20/40...` 可见分页算法不改。
- 新 IndexService 自己完整扫描状态库，不调用 Provider 的 `LoadMoreAsync()` 来滚完整库。
- 完整扫描成功后才切换/写入缓存；失败不把部分索引冒充完整结果。
- 第一版可查询字段：contentType、myRating/unrated、year、genre、country；排序：marked/myRating/Douban score/year/title。
- tag/tags 仍未宣称支持；首次真实 non-empty response 会记录 `Frodo schema` 的 InterestKeys/SubjectKeys/HasTagLikeFields，之后根据实机日志决定。
- Shell 本地筛选 UI 尚未接入，这是下一步；评价写入/删除/官方回读文件仍禁止修改。

# 2026-08-19：v13.1 修正——Shell 转发保留 Frodo dom.source

v13 的方向正确，但实机日志证明 append 条件没有真正命中：加载 40/60/80 时第一页旧 SubjectId 又重复触发 poster fallback。检查消息链路后确认，`FrodoPersonalProvider.BuildPayload()` 已包含 `dom.source = "frodo-api"`，但 `HtmlMediaLibraryForm.ForwardDoubanSourceResultToShellAsync()` 重新组装 `doubanShellData` 时没有转发 `dom`，因此 `douban-shell.js` 的 `message.dom.source === "frodo-api"` 永远取不到值。

v13.1 只做协议补全：

- C# Shell forwarding 增加 `dom = root.dom.Clone()`，缺失时为 `{}`。
- 保留 v13 的前端条件判断与 SubjectId append 去重，不把所有 personal/DOM fallback 都强制改成 append。
- 保留 personal 1200px IntersectionObserver 提前触发。
- 不改 FrodoClient、Mapper、Provider 分页、个人筛选 fallback、详情和评价链路。

实机验收重点：加载 20 -> 40 -> 60 后，旧第一页 SubjectId 不应再次出现 poster fallback；只有新追加的 20 张可能触发新海报 fallback。若仍有轻微的新卡海报显现，再单独评估“海报预取”，不要重新修改分页规则。

---# 2026-08-19：Phase 1 个人页无限滚动（Frodo append + 1200px 提前加载）

分页数据本身已经通过真实账号验证：Frodo 使用固定 `0/20/40/60...` 槽位游标，Provider 把 underfilled 窗口补成 20 卡可见批次。剩余的“到底部闪一下”来自 Shell 渲染方式，而不是 API 分页。

本轮只修显示链路：

- `douban-shell.js` 原本只有 Explore/Search 的 paging response 使用 `append=true`，个人页每次都会 `replaceChildren()` 后重画累计 40/60/80… 卡片。
- Frodo personal paging 现在只追加尚未存在的 SubjectId；后端仍可继续返回累计 items，不改变 C# / Shell payload 契约。
- 为避免改变非默认个人筛选的 DOM fallback，append 条件额外要求 `message.dom.source === "frodo-api"`。
- personal 的 IntersectionObserver 提前加载距离从 720px 改为 1200px；Explore 仍为 720px。这样下一批通常会在用户真正触底前开始读取。
- Provider、FrodoClient、Mapper、评价写入和官方回读均不修改。

实机验收：看过/想看连续滚动 3～5 批，确认旧卡片不闪烁、不重新加载海报，卡片数量连续增加；日志仍应保持 `RequestedStart=0/20/40...`、`CursorAdvance=20`、正常 `Duplicates=0`。

---# 2026-08-19：Phase 1 Frodo 分页最终结论（固定 20 槽位游标）

真实账号二次实测已经推翻 v11 的 `start += RawCount`：

- `collect`：首段 `start=0,count=20 -> Raw=16`；若错误地从 `start=16` 继续，下一段立即出现 `Duplicates=3`。
- `wish`：首段 `start=0,count=20 -> Raw=19`；若错误地从 `start=19` 继续，下一段立即出现 `Duplicates=1`。

因此 `start` 是 Frodo 原始列表的固定槽位偏移。某个 20 槽位窗口可能因下架/不可见条目只返回 16/19 个 interests，但下一窗口仍应从 20/40/60… 开始。最终实现：

- 请求保持 `count=20`。
- 游标按 `ApiCount`/请求窗口推进，不按 `RawCount` 推进。
- 保留 v11 的 pending 缓冲，只负责把不足 20 个可见条目的 API 窗口与后续窗口拼成 20 卡 Shell 批次。
- 已显示 + pending 继续统一 SubjectId 去重；正常固定窗口分页不应再因重叠产生重复。
- 非默认筛选、Frodo 失败 DOM fallback、评价写入与官方回读边界保持不变。

下一轮实机验收只需确认：`RequestedStart` 为 `0 -> 20 -> 40 -> 60...`，同时最终返回给 Shell 的 `ShellItems` 为 `20 -> 40 -> 60...`（真实尾页除外），且正常分页 `Duplicates=0`。

---# 2026-08-19：Phase 1 Frodo underfilled-page 分页修正（v11 试验，已被后续实机纠正）

真实账号已经证实：`collect` 首次请求 `count=20` 时 API 响应为 `ApiCount=20 / Raw=16 / Mapped=16 / Skipped=0 / Duplicates=0`。公开 Frodo 实机导出项目记录了同类行为：已下架/删除的条目仍会影响 collection 总量，但 API 返回的 `interests` 会少于请求 `count`；正确分页方式是按本次实际返回的 `len(interests)` 推进 `start`。

本轮因此修正 Provider，而不是修改 Mapper 或把 page size 改成 16：

- Frodo 请求仍为 `count=20`。
- `_nextStart` 只按 `RawCount` 前进；不再使用 `Math.Max(page.Count, page.RawCount)`。
- 增加 `_pendingItems` 和 `_seenSubjectIds`，内部可连续请求多个 Frodo 页，Shell 每次尽量新增 20 个唯一影片。
- 首屏若先拿到 16 个，会继续从 `start=16` 读取，补足到 20 后再发给 Shell；多余结果留在 pending，下一次“加载更多”优先消费。
- 只有 API 真正耗尽时，最终批次才允许不足 20。
- 非默认筛选、API 失败 fallback、评价写入与官方回读边界保持不变。

实机验收重点：看过/想看连续加载 2～3 次，确认 ShellItems 以 20 为步长增长（最后一页除外），并确认日志中的 `RequestedStart` 在 underfilled 页后按 `Raw` 推进。

---# 2026-08-19：Phase 1 实机修正（Frodo 行诊断 + 日志轮转）

Phase 1 已在真实账号 `collect` 列表确认 `Source=Frodo`，总数 1062 且连续分页到 `nextStart=360` 正常。实测同时发现 Shell 累计数并不总等于 API 游标（首屏 `nextStart=20` 时 Items=16，`nextStart=300` 时 Items=295），因此本轮只增加可观测性，不先猜测为数据丢失。

- `FrodoPersonalPage` 记录 `RawCount` 和逐条 `Skipped` 原因。
- Provider 每页记录 `Raw / Mapped / Skipped / Duplicates / Added / ShellItems / Total / NextStart`。
- 只有实际无法映射的行才记录 `Frodo personal row skipped`；跨页或页内重复 ID 记录 `Frodo personal duplicate skipped`。
- 下一轮实机判断：若 `Raw < ApiCount`，属于 Frodo 本页实际少返回；若 `Skipped > 0`，按 reason 修 Mapper；若 `Duplicates > 0`，核查 Frodo 是否重复同一 SubjectId。
- `diagnostic.log` 改为 10 MiB × 当前文件 + 3 个归档，并限制单条 16384 字符，避免再次出现近 1 GB 单日志。
- 不修改 `ReviewWriteCoordinator.cs`、`ReviewWriteVerifier.cs`、`ReviewTargetResolver.cs`、`ReviewWriteModels.cs`。

---# 2026-08-19：API 迁移 Phase 1 交接（个人页 Frodo）

当前 API 迁移基线固定为 GitHub `main` commit `25e199bd1cc5f70e44f1caf6a3c5b294849d5a90`。本阶段只替换个人页默认列表读取：Frodo 为首选，旧 DOM 为 fallback；Explore、详情和写入不在本阶段范围。

关键边界：

- `done -> collect`、`mark -> wish`、`doing -> do` 只在 `DoubanStatusMapper` 内转换。
- Shell/前端结构不改；Frodo provider 输出与 `douban-personal-source-bridge.js` 同层级 payload。
- 非默认个人筛选明确退出 Frodo 并回到隐藏 Source WebView DOM，不删除现有筛选能力。
- Frodo 首屏、刷新或分页失败不得返回伪成功；自动进入 DOM fallback。
- `ReviewWriteCoordinator.cs`、`ReviewWriteVerifier.cs`、`ReviewTargetResolver.cs`、`ReviewWriteModels.cs` 未修改。
- 真实登录态必须验收：三状态首屏、至少三页连续分页、非默认筛选 fallback、Frodo 故障 fallback、评价修改/删除官方回读回归。
- 详细字段矩阵和验收清单见 `docs/FRODO_PERSONAL_PHASE1.md`。

---
# 观影助手 v1.0.0 基线：AI 影评问题交接

日期：2026-08-16

## 当前发布基线

当前版本已重新登记为 `v1.0.0` 个人开发基线，核心功能继承用户确认可用的稳定源码。当前唯一活动开发目录：

`D:\chatgpt\观影助手\开发\v1.0.0-clean-rebaseline-20260818-205734`

稳定 EXE、ZIP、哈希、回滚路径以父目录 `DEVELOPMENT_BASELINE.json` 为唯一准绳。开始下一阶段工作前，不修改稳定 EXE/ZIP；应从当前源码目录复制新的隔离开发目录。

## 下一阶段唯一主题

未来处理 AI 影评问题。当前只登记目标和边界，本轮没有修改 AI 影评实现。

目标是让 AI 影评基于当前真实豆瓣详情页内容工作：

- 读取当前影片详情页的剧情/简介；
- 读取当前豆瓣页面可见的短评和长评内容；
- 提取关键词、主题、优缺点和情绪倾向；
- 生成可预览的 AI 影评草稿；
- 用户明确确认后，沿用正式豆瓣写入、提交和官方回读确认链路。

## 必须保持的边界

- 不恢复 `douban-history.json`、`douban-search-cache.json` 或旧本地历史缓存作为 AI 输入；
- 不把搜索页卡片摘要当成影评输入；影评输入必须来自当前真实详情页 DOM；
- 不绕过豆瓣官方页面控件，不直接拼接或调用私有写入接口；
- “预览生成”不能产生真实豆瓣写入；真实提交必须有单独确认；
- 提交后必须回到官方页面做状态、评分和短评回读；
- AI 影评问题与搜索页分页、个人页无限滚动、Explore 页面状态隔离；不得顺手改动本次 v1.0.0 基线能力。

## 建议处理顺序

1. 读取本目录 `AGENTS.md`、父目录 `DEVELOPMENT_BASELINE.json` 和 `DEVELOPMENT_DIRECTORY_INDEX.md`。
2. 从当前稳定源码复制新的时间戳隔离目录，并先运行基线门禁。
3. 在真实登录态 WebView2 中确认当前详情页的剧情、短评、长评实际 DOM 结构和分页/展开行为。
4. 先实现只读采集与结构化结果，不接真实写入；记录空内容、未登录、验证码、风控和页面变化状态。
5. 实现 AI 草稿预览，保留原文来源和生成失败原因，不覆盖用户已有短评。
6. 接入现有官方写入协调器，增加显式确认、写入后回读和失败恢复证据。
7. 完成真实登录态验收后，再单独建立 AI 影评版本记录；没有真实回读证据不得写成完成。

## 当前不要做的事

- 不修改当前稳定 EXE/ZIP；
- 不重新启用搜索页自动无限滚动；
- 不把旧 `DoubanWriteProbe` 或已删除测试目录恢复到开发根目录；
- 不把 AI 影评草稿直接写入豆瓣；
- 不用静态测试、HTTP 成功或脚本加载代替真实页面验收。

## 交接结论

当前 v1.0.0 搜索基线作为正常可用起点结束；下一次工作的入口是 AI 影评问题，不是搜索功能回归。完成 AI 影评前，先补充真实详情页内容读取证据和可回读的写入闭环。

# 2026-08-19：路线 B / Frodo Filter-Only Phase 1

最终权威关系：

- 普通个人页 `collect / wish / do`：豆瓣官方网页 DOM 是 Personal State 唯一权威。
- Frodo 不再决定普通列表成员、排序、状态、我的评分、短评或标记日期。
- 只有启用完整个人库高级筛选时才进入 Frodo Filter Mode。
- 清空全部高级筛选条件后立即退出 Frodo Filter Mode，回到当前 status 的 DOM 页面。
- `FrodoPersonalProvider` 暂不删除，但退出普通个人页运行链。
- `FrodoPersonalIndexService`、QuerySession、完整 Store、facets、本地筛选分页继续保留。
- 下一层公共字段采用 Metadata Overlay：`SubjectId -> SubjectMetadataCache -> Frodo metadata source`，只允许补豆瓣评分、评分人数等公共字段，禁止修改 Personal State。
- Metadata Overlay 必须异步、批量、可失败降级，任何 miss/超时不得阻塞 DOM 普通页。

# 2026-08-19：Filter-Only Hotfix v3

Phase 1 回 DOM 后发现两个回归并已修复：

1. 完整个人库高级筛选 UI 不属于“普通列表数据源”，因此不能跟 `_frodoPersonalActive` 一起关闭。DOM 普通页仍持续接收本地完整索引 capability state，并显示完整筛选入口。
2. 豆瓣个人网页 DOM 不提供公共豆瓣评分。普通 DOM 卡片通过 `SubjectId` 从现有 Frodo Personal Index 只读补充 `score / ratingCount`。

硬边界：

- DOM：membership / status / myRating / comment / markedDate / order / paging 的普通页权威。
- Frodo Index：高级筛选数据源 + 只读 Subject Metadata Overlay。
- Overlay 禁止修改任何 Personal State。
- 普通豆瓣网页原生筛选始终 DOM；清空高级筛选也回 DOM。

# 2026-08-19：手动重读个人页缓存

后台“观影浏览器连接状态”窗口新增 `重读个人页缓存`：

- 顺序完整重读 `collect / wish / do` Frodo Index。
- 使用现有 `ForceFullReconcileAsync`，不另建 Personal State 同步路线。
- 显示当前状态与 `Loaded / Total` 进度。
- Douban Plus 正在运行时重建同一内存 Index，并立即让当前 DOM 页重新应用公共评分 Metadata Overlay。
- Douban Plus 未运行时，从 `frodo-personal-index-v1.json` 读取现有 ProfileId 后后台重建。
- 该按钮只更新高级筛选索引和公共元数据缓存，不改变 DOM 个人状态权威。

评分显示统一一位小数：`9` -> `9.0`。

# 2026-08-20：公共评分强制读取缓存

Metadata Overlay 不再按当前 collect/wish/do 分区读取评分。
现在固定为：DOM SubjectId -> 扫描整个 Frodo Personal Cache -> 任一缓存副本有 Score 就覆盖。
仅允许读取 Score / RatingCount；禁止使用缓存 Personal Status 修改 DOM 状态。
手动完整重读使用 count=100；普通 UI 分页仍保持 20。

# 2026-08-20：Frodo 全量缓存分页空洞修复

实机缓存证明：

- `collect total=1066`，实际仅 1055 items，却错误标记 `complete=true`。
- 日志中短页缺口数量合计正好为 11：
  - start=0: count=50 / raw=46 -> 缺4
  - start=250: raw=49 -> 缺1
  - start=400: raw=48 -> 缺2
  - start=450: raw=48 -> 缺2
  - start=650: raw=49 -> 缺1
  - start=700: raw=49 -> 缺1
- 《嗜血法医 第二季》(2299474) 与《嗜血法医 第八季》(20452294) 整条记录因此没有进入缓存。

修复规则：

1. 仅完整缓存扫描改为 `nextStart = responseStart + RawCount`。
2. API `count` 只作诊断，不再作为全量缓存游标步长。
3. 只有 `unique SubjectId count == stable total` 才允许提交快照。
4. 任何不完整扫描都抛错并保留旧缓存。
5. 普通 DOM、普通分页、高级筛选和 Metadata Overlay 不修改。
