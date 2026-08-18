# Frodo 个人页迁移 Phase 1

基线：`25e199bd1cc5f70e44f1caf6a3c5b294849d5a90`

## 范围

- 仅迁移个人页 `collect / wish / do` 默认列表读取。
- Frodo 状态统一映射：`done -> collect`、`mark -> wish`、`doing -> do`。
- 输出保持 `douban-personal-source-bridge.js` 的 Shell payload 与 item 字段契约。
- Frodo 分页在 C# 累积，个人页 Shell 继续收到累计 items，保持现有个人页分页行为。
- 非默认个人筛选（类型、可播放/视频、评价/标题排序等）仍走原 DOM 路线，避免第一阶段静默丢功能。
- Frodo 首屏失败时自动导航旧个人页 DOM；没有删除 Douban Plus、Source WebView 或评价写入链路。

## 字段矩阵

| Shell 字段 | Frodo 来源 | 处理 |
|---|---|---|
| subjectId | `interest.subject.id` | 必须为数字 |
| subjectUrl | `interest.subject.url` | 无效时按 subjectId 生成官方详情 URL |
| posterUrl | `subject.cover_url` / `subject.pic.large/normal` | 按顺序兜底 |
| title | `subject.title` | 缺失时显示豆瓣条目 ID |
| year | `subject.year` | 直接映射 |
| countries | `subject.countries` | 缺失时仅从 `card_subtitle` 的国家段保守提取 |
| genres | `subject.genres` | 直接映射 |
| cast | `subject.actors[].name` | 与现有 bridge 一样只输出前 2 位 |
| directors | `subject.directors[].name` | 直接映射 |
| contentType | `subject.type/subtype` | `tv` 否则 `movie` |
| score | `subject.rating.value` | 豆瓣公众评分 |
| ratingCount | `subject.rating.count` | 评价人数 |
| myRating | `interest.rating.value` | 归一到 1-5 星 |
| status | `interest.status` | 统一映射到 collect/wish/do |
| markedDate | `interest.create_time` | 直接映射 |
| comment | `interest.comment` | 直接映射 |
| intro | `subject.card_subtitle` | 保留原始结构化摘要 |

## fallback 规则

1. 默认个人页：Frodo 首选。
2. Frodo 请求/JSON/映射失败：导航原 `movie.douban.com/people/{uid}/{status}`，继续由 `QbDoubanPersonalSourceBridge` 读取。
3. 个人页筛选 URL 只要不是默认语义（`start=0/sort=time/type=all/filter=all/mode=grid/tags_sort=count`），直接走旧 DOM。
4. API 模式“加载更多”失败时不伪造成功，立即切回当前状态的旧 DOM 默认页；切换状态或刷新后会再次优先尝试 Frodo。

## 实机验收

- 登录后依次打开 看过 / 想看 / 在看，确认日志出现 `Source=Frodo` 且首屏卡片、海报、个人星级、日期、短评正常。
- 连续滚动至少 3 页，确认没有重复 SubjectId，卡片累计数量递增。
- 点击“电影/电视”“可播放/有视频”“按评价/按标题”任一非默认筛选，确认日志出现 `Fallback=DOM` 且功能仍可用。
- 暂时断网或人为提供错误 `DOUBAN_FRODO_API_SECRET`，确认首屏自动回退 DOM，不出现空列表冒充成功。
- 修改/删除评价回归一轮，确认 `ReviewWriteCoordinator` 及官方回读链路未受影响。
## 实机诊断补充（2026-08-19）

真实账号 `collect` 已确认 Frodo 首屏与连续分页生效。为解释 API 游标与 Shell 卡片累计数的差异，后续日志必须优先查看：

- `Frodo personal page mapped`：包含 `ApiCount / Raw / Mapped / Skipped / Duplicates / Added / ShellItems / Total / NextStart`。
- `Frodo personal row skipped`：仅记录无法映射的单条原因，不记录原始完整 JSON。
- `Frodo personal duplicate skipped`：表示该 SubjectId 在当前累计列表中已经存在。

判断顺序：`Raw < ApiCount` 先视为 API 本页实际返回数量不足；`Raw == Mapped + Skipped` 用于核对 Mapper；`Duplicates` 单独解释累计数减少。未确认原因前，不得把 `nextStart - ShellItems` 直接等同于“丢数据”。

诊断日志保护：`diagnostic.log` 上限 10 MiB，保留 `diagnostic.1.log`～`diagnostic.3.log`；单条消息最多 16384 字符。历史版本遗留的超大 current/archive 日志不会继续保留。
## Underfilled page 分页规则（v11 试验，已被后续实机纠正）

真实 `collect` 日志：`count=20`，Frodo 返回 `Raw=16`，同时 `Mapped=16 / Skipped=0 / Duplicates=0`。因此 16 不是 Mapper 或 Shell 丢失，而是 API 本页本身 underfilled。

Frodo 社区实机实现记录：个人 interests 可能因已下架/删除条目而少于请求 `count`；此时 `total` 仍可能包含这些历史位置。分页不得使用固定 `start += count`，应使用本次响应实际 `interests` 数量推进。

本项目固定规则：

1. API 仍请求 `count=20`，不为了某次 `Raw=16` 改成 16。
2. 游标：`nextStart = responseStart + RawCount`；`RawCount=0` 视为 API 真正耗尽。
3. Shell 可见批次目标仍为 20 个唯一影片，匹配 5 列 × 4 行。
4. API underfilled 时 Provider 自动继续请求下一段并补齐；超过当前可见批次的条目进入 pending 缓冲。
5. 去重范围覆盖已显示 + pending；异常情况下单个可见批次最多 10 次内部请求。
6. 最终真实尾页允许少于 20，其余加载应尽量保持完整 20 卡。

诊断字段：`Raw` 决定 API 游标推进；`Buffered` 表示本页进入 pending 的新唯一条目；`Published` 表示本页实际发布到 Shell 的数量；`Pending` 表示留给下一次可见批次的数量。
## Underfilled page 最终分页规则（固定槽位窗口，2026-08-19）

v11 曾尝试用 `RawCount` 推进游标，但真实账号再次测试后出现稳定重叠：`collect start=16` 有 3 个重复，`wish start=19` 有 1 个重复。这证明 `start` 不是“已返回条数”，而是固定源槽位位置。

最终规则：

1. API 请求保持 `count=20`。
2. `nextStart = max(responseStart, requestedStart) + ApiCount`；若响应 `count<=0`，使用配置 PageSize 作为窗口宽度。
3. `RawCount` 只表示该 20 槽位窗口实际可返回多少个 interests，不再参与游标推进。
4. API 窗口按 `0,20,40,60...` 前进；underfilled 窗口缺少的可见卡片由 Provider 继续读取后续窗口并通过 pending 缓冲补齐。
5. Shell 用户可见批次仍尽量为 20 个唯一影片（5 列 × 4 行）；只有真实列表尾部允许不足 20。
6. 正常固定窗口分页应不再因窗口重叠出现 SubjectId duplicate；若仍有 duplicate，保留诊断并单独调查服务端重复。

验收日志重点：`CursorAdvance=20`、`RequestedStart=0/20/40...`、最终 `ShellItems=20/40/60...`，并观察 `Duplicates=0`。
## 个人页无限滚动显示规则（2026-08-19）

固定槽位分页与 20 卡 Provider 缓冲通过实机后，个人页仍存在滚到底部时短暂闪烁。原因是 Shell 分页渲染条件只把 Explore/Search 当作 append；personal paging 收到累计 items 后会清空网格并重建所有卡片。

最终显示规则：

1. Frodo personal paging response 使用 append，不清空已显示卡片。
2. append 仅在 `message.dom.source === "frodo-api"` 时启用，避免改变非默认筛选走 DOM fallback 时的旧行为。
3. `render()` 继续使用现有 `data-subject-id` / SubjectId 集合去重；虽然 Provider 返回累计 20/40/60… items，前端实际只创建新增卡片。
4. personal 无限滚动 IntersectionObserver 的底部预触发距离为 1200px；Explore 仍保持 720px。
5. API/Provider 分页规则不变：`count=20`、固定 `0/20/40...` 槽位游标、pending 补齐 20 卡可见批次。
6. 目标体验：用户连续向下滚动时，下一批在真正触底前开始读取；已有海报与卡片不被重建，视觉上接近连续无限列表。

验收时除分页日志外，重点观察旧 20/40/60 卡是否保持稳定、不出现整页闪一下；poster fallback 不应因每次个人页分页而对全部旧卡再次触发。
## v13.1 append 协议修正（2026-08-19）

v13 实机日志确认两个事实：

1. personal 1200px IntersectionObserver 已经提前触发，首屏完成后约 0.7 秒即可开始下一批，因此“没有无感接上”不是因为预触发没有运行。
2. 加载 40/60 后，第一页已经显示过的 SubjectId 又重复触发 poster fallback，证明 personal paging 仍在重建旧卡片。

根因位于 C# 到 Shell 的转发层：Provider payload 的 `dom.source = "frodo-api"` 只被写进诊断日志，没有进入真正的 `doubanShellData`。而 v13 的 append 条件正是依赖 `message.dom.source === "frodo-api"`，所以条件始终为 false。

修正规则：

1. `ForwardDoubanSourceResultToShellAsync()` 必须原样转发 payload 的 `dom` 对象；没有 `dom` 时发送空对象。
2. v13 的前端 append 条件保持不变，只让真正的 Frodo personal paging append。
3. DOM fallback 保持旧行为；不为了修 Frodo 闪烁而扩大到所有 personal 分页。
4. personal 预触发仍为 1200px；本轮不引入更复杂的数据/海报双预取。
5. 分页仍为固定 `0/20/40...` 槽位游标 + pending 补齐 20 卡，不重新讨论已通过实机的分页算法。

验收：20 -> 40 -> 60 连续滚动时，旧卡 DOM 与旧海报保持不动；日志中的 poster fallback 应主要对应新增加的 SubjectId，不再整批重复第一页/前几页。
