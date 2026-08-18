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
