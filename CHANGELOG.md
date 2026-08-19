# Frodo Personal Store Refactor v2：状态秒切 + BoundedSync + 永不清空可用 Store - 2026-08-19

- 状态按钮改为本地 Store 优先：已有 `collect / wish / do` 完整快照时直接 Query + 本地分页，切状态不再重新读取整个个人库。
- 后台同步固定为首屏 + 最多 5 页 BoundedSync；正常 `total +N` 找齐 N 个新 SubjectId 立即停止。
- 删除 `InvalidateStatusForRebuildAsync` 的“遇到异常先 `_statuses.Remove(status)`”行为；任何网络、解析或语义不确定都保留现有 Store。
- `total -N` 启动后台 DeletionReconcile：完整收集稳定云端 SubjectId 后只删除差集，失败时 Store 原样保留且 UI 不阻塞。
- 同 total 陌生条目、delta 不匹配或 5 页内未找齐时标记诊断为 `NeedsDeepReconcile`，普通状态切换和普通刷新均不允许隐式 Full Build。
- 完整扫描调用语义拆成 `BootstrapStatusAsync` 与 `ForceFullReconcileAsync`；完整 snapshot 先独立构建，并要求扫描期间 cloud total 稳定且无映射 skip，成功后才替换旧 snapshot。
- 本地筛选、本地无限滚动以及评价后的 UI 刷新都不再依赖 Provider 当前状态，完整 Store 模式与 Provider 分页会话解耦。
- 自检新增：5 页预算、同 total 陌生条目不破坏可用 Store；原有 InterestId、增量 UPSERT、状态迁移、权威评分优先测试继续保留。

# Frodo Personal Store Refactor v1：单一权威库 + total 最小增量同步 - 2026-08-19

- 将现有 `FrodoPersonalIndexService` 收拢为个人库唯一持久化 Store；`FrodoPersonalProvider` 只保留当前页面的临时分页/显示缓冲，不再作为第二份个人库权威状态。
- Provider 每次拿到 Frodo interests 页面后，先把云端整条记录 UPSERT 到 Store。`CloudTotal == LocalTotal` 时只覆盖已观察记录的评分/短评等字段；`CloudTotal > LocalTotal` 时按差额从当前 source-slot 继续找新 SubjectId，找到所需数量立即停止。
- 状态迁移（如 `wish -> collect`）由增加侧的新 SubjectId 定位；同一 SubjectId 写入目标状态时会从其它已完成状态快照移除，避免 Provider/Index 两边或状态之间出现重复事实。
- `CloudTotal < LocalTotal`、同 total 却出现未知 SubjectId、或差额无法按最小扫描解释时，不猜删除对象：仅将该状态标记为需要完整 reconcile，页面先正常显示，现有后台索引流程随后只重建这一状态。
- 新增 `InterestId` 映射自 Frodo `interest.id`；缓存 schema 升至 v4，旧 v3 缓存仅在升级后的首次使用重建一次。
- 修复 authoritative precedence：已经拿到的新鲜 Frodo 记录，其 `MyRating / Comment / MarkedDate / InterestId` 不再被旧 `record.Rating` 等本地字段覆盖，解决“普通页五星、五星筛选仍是旧值”的根因。
- 新增无网络自检，覆盖 InterestId、`total +1` 首屏增量 UPSERT、`wish -> collect` 单 Store 状态迁移、Frodo 权威记录优先级。
- 不接入 RSS；不修改 `ReviewWriteCoordinator.cs` / `ReviewWriteVerifier.cs` / `ReviewTargetResolver.cs` / `ReviewWriteModels.cs`；已验证的 Frodo `start=0,20,40...` source-slot 分页算法保持原样。
- 已知边界保持独立：手机只修改很老的历史评分/短评、且 total 不变又不出现在已读取 Frodo 页面时，仍需手动/低频完整 reconcile；本轮不为该边缘场景增加后台轮询。

# Frodo 个人库筛选 Step 5：新增影片 UPSERT + 正确可播放 + 评分滑块 - 2026-08-19

- 修复新评价影片只出现在刷新后的 Provider、却没有进入完整筛选索引的问题：官方确认后先 UPDATE，索引不存在该 SubjectId 时从 Frodo 最新个人页短重试回读并 INSERT，形成真正 UPSERT。
- 新条目 UPSERT 同时修正 Provider 内存和完整 Index；当前处于本地筛选时立即按原条件重新 Query，默认个人页则自动刷新一次首屏，不再出现“刷新能看见，一筛选就消失”。
- `可播放` 不再读取猜测字段 `is_playable`；个人 interests 的主字段改为 `subject.has_linewatch`，并兼容 `actions` 中的“可播放”和非空 `linewatches`。
- 完整索引缓存 schema 升到 v3，强制淘汰 v2 中可能全部为 false 的错误 Playable 缓存；索引完成日志增加 Playable 统计，Frodo schema 日志只记录字段存在性，不记录原始响应。
- 豆瓣评分 Query 不改；仅重做评分区间浮层为自定义双圆点轨道。拖动视觉连续，松开后按 0..10 的整数评分吸附，和豆瓣原生 1 分步进保持一致。
- 固定 Frodo `start=0,20,40...` 源槽分页、pending buffer、评价官方回读和双评分卡片均保持不变。

# Frodo 个人库筛选 Step 4：统一筛选状态 + 可播放 + 评分区间 - 2026-08-19

- 个人页筛选统一为一套 Frodo 本地 `FrodoPersonalFilterCriteria`；第一层不再显示 `筛选影片 / 正在热映 / 在线观看`，也不再从这些入口切换 DOM 数据源。
- Frodo `subject.is_playable` 映射为 `FrodoPersonalItem.Playable`；完整索引 schema 升为 v2，旧缓存自动失效重建，避免旧条目被错误视为不可播放。
- 第一层固定一整排：`状态 / 影片类型 / 排序 / 可播放 / 豆瓣评分 / 筛选`；宽度不足时整排横向滚动，不再把 `筛选` 单独换行。
- `可播放` 是第一层即时本地筛选条件，可与电影/电视、星级、年代、地区、题材、豆瓣评分区间任意组合。
- `豆瓣评分` 点击后弹出 0-10 分双滑块浮层；确定后以 `ScoreMin/ScoreMax` 查询完整索引，设置评分区间时无豆瓣评分条目自动排除。
- 点击 `筛选` 后只出现 `我的评分 / 年代 / 地区 / 题材` 四个分类；一次只展开当前点击分类的选项，不再一次铺满所有国家/题材。
- `年代` 单一维度直接显示近 5 个年份和更早年代：如 `2026/2025/2024/2023/2022/2020年代/2010年代/.../60年代`；删除独立年份下拉入口。
- 评价官方确认后的索引/Provider 即时同步、个人卡片左下我的星级和右下豆瓣评分继续保留。
- 不修改评价提交/官方回读核心文件，Frodo Provider 已验证的固定 `0/20/40...` 源游标算法保持不变。

# Frodo 个人库筛选 Step 3：即时同步 + 简洁筛选 + 双评分 - 2026-08-19

- 修复官方评价保存/删除确认后完整个人库索引不立即更新的问题：确认后同时更新 `FrodoPersonalIndexService` 本地完整快照和当前 `FrodoPersonalProvider` 已加载卡片，避免继续滚动时旧累计页把已移走条目重新追加。
- 当前使用星级/年份/地区/题材筛选时，评价确认后立即用原 criteria 重新查询并刷新筛选结果；普通个人列表则只原地更新/移除对应卡片，不重绘整页海报。
- 个人页恢复简洁第一层：状态、筛选影片、影片类型、排序，最后只增加一个 `筛选` 按钮；高级条件仅在点击后于下方展开。
- 高级筛选按语义分区：我的评分、年代、具体年份、地区、题材。`筛选影片`、电影/电视、排序不再在高级区重复。
- `可播放/有视频` 不再作为高级筛选重复项；第一层沿用 `正在热映/在线观看` 入口，底层仍复用既有 DOM 网页条件。
- 新增年代条件，与具体年份互斥；地区和题材保持独立，不再混成豆瓣 App 那种“大标签池”。
- 个人卡片海报左下显示“我的星级”，右下固定显示 Frodo `subject.rating.value` 映射的豆瓣评分；探索/搜索页右下豆瓣评分行为保持不变。短评标识上移，避免与左下星级冲突。
- 未开放“演员”高级筛选：当前 Frodo mapper 的演职员字段并不能证明是完整演员表，避免把不完整人物数据包装成全库筛选。
- 不修改 `ReviewWriteCoordinator.cs` / `ReviewWriteVerifier.cs` / `ReviewTargetResolver.cs` / `ReviewWriteModels.cs`，官方回读仍是写入成功唯一确认依据。

# Frodo 个人库筛选 Step 2：完整库筛选 UI 接入 - 2026-08-19

- 默认个人页仍先走现有 `FrodoPersonalProvider` 快速显示首屏，不等待完整索引。
- 后台加载/建立 `FrodoPersonalIndexService` 当前状态完整索引；建立期间 Shell 明确显示进度，不把已加载前 20/60 部误当成完整筛选范围。
- 索引完成后个人页新增本地筛选控件：影片类型、我的 1-5 星/未评分、年份、地区、题材，以及最近标记/我的评分/豆瓣评分/年份/标题排序。
- 新增 `FrodoPersonalQuerySession`，完整库筛选结果仍按 20 部分页送给 Shell，继续复用现有 IntersectionObserver 无限滚动，不一次向 WebView 灌入上千条记录。
- 本地筛选分页使用 `dom.source=frodo-local-index`，只 append 新的 20 部卡片；v13.1 已验证的 `frodo-api` append 行为保持不变。
- `可播放` / `有视频` 仍保留为“网页条件”，点击后继续走现有 DOM fallback，因为目前没有可靠 Frodo 等价字段。
- 本轮不修改详情 API、评价写入/删除/官方回读链路，也不改变已实机验证的 Frodo 固定 0/20/40... Provider 游标算法。

# Frodo 个人库筛选 Step 1：日志瘦身 + 全库索引基础 - 2026-08-19

- 诊断日志不再持续写 `Unified Shell data posted` 的完整累计 Payload，改为 RequestId / Generation / Source / Operation / Status / Items / Bytes / Error 摘要。
- DOM Source read 的完整 `ReadResult` 不再写入日志，只保留字节数；成功的 poster fallback 高频日志静默，真实失败继续记录。
- FrodoClient 首次拿到非空 interests 时只记录字段名 schema，不记录字段值，用于确认真实 response 是否存在 tag/tags 类字段。
- 新增 `FrodoPersonalIndexService`：与现有可见分页 Provider 解耦，按固定 API 槽位 `0/20/40...` 完整扫描个人状态库，SubjectId 去重，成功后原子写 `frodo-personal-index-v1.json`。
- 索引层已实现电影/剧集、我的评分/未评分、年份、Genre、国家地区筛选，以及最近标记/我的评分/豆瓣评分/年份/标题排序。
- 本 Step 只落后端索引基础，尚未把本地筛选控件接到 Shell；现有 Frodo 首屏、pending、无限滚动、DOM fallback、详情和评价写入链路不改。

# Phase 1 v13.1：修复 Frodo append 来源标记丢失 - 2026-08-19

- v13 实机日志确认：1200px 提前触发已经生效，但个人页分页仍会重画旧卡片并重新触发旧海报 fallback。
- 根因不是 `render()` 或 Frodo 分页，而是 C# `ForwardDoubanSourceResultToShellAsync()` 重新组装 Shell 消息时漏掉了 Provider 原始 payload 的 `dom` 字段。
- v13 前端 append 条件要求 `message.dom.source === "frodo-api"`；由于 `dom` 被宿主层剥离，该条件永远为 false，个人页继续走 replace/rebuild。
- 本轮只把 `dom` 原样 Clone 转发给 Shell，使既有 v13 append 条件真正生效；非默认个人筛选的 DOM fallback 仍不会被错误强制 append。
- 1200px 提前加载、固定 `0/20/40...` Frodo 游标、Provider pending 缓冲、筛选 fallback、详情、评价写入和官方回读均保持不变。
# Phase 1 个人页无限滚动修正：追加渲染 + 提前加载 - 2026-08-19

- 实机确认 Frodo 固定 20 槽位分页与 20 卡可见批次正确后，修复个人页加载更多仍整网格重绘的问题。
- 原 Shell 只对 Explore/Search 分页启用 append；Frodo 个人页虽然后端返回累计 20/40/60… items，前端却会清空旧卡片再重画全部内容，造成到底部时闪一下并重复触发旧海报 fallback。
- Frodo 个人页分页改为 append：仅在 `message.dom.source == "frodo-api"` 时复用现有 SubjectId 去重追加逻辑；个人页 DOM fallback 保持原渲染语义不变。
- 个人页无限滚动触发距离从底部 720px 提前到 1200px；Explore 继续保持 720px，减少用户真正滚到底部后才等待下一批的割裂感。
- 不修改 Frodo API 请求、固定 20 槽位游标、Provider pending 缓冲、筛选 fallback、详情、评价写入或官方回读。
# Phase 1 分页最终修正：固定 20 槽位游标 + 20 卡可见批次 - 2026-08-19

- v11 实机反证了“按 RawCount 推进”：`collect start=16` 立即出现 3 个重复，`wish start=19` 立即出现 1 个重复，说明 `start` 指向固定源槽位而不是“已实际返回多少条”。
- 最终规则：Frodo 仍请求 `count=20`，下一段按 API 窗口推进；`nextStart = responseStart + ApiCount`，当响应 count 缺失时才回退配置的 PageSize。
- underfilled 页仍可能只有 16/19 个可见 interests；这类空槽不回退游标、不重复读取，Provider 用 pending 缓冲从后续 20 槽位窗口补齐用户可见批次。
- Shell 每次仍尽量新增 20 个唯一影片，匹配当前 5 列 × 4 行；真实尾页除外。
- v11 的 RawCount 游标结论保留为历史试验记录但明确标记已被后续实机纠正。
- 不修改 Shell UI、Douban Plus、Explore、详情、评价写入、官方回读或 DOM fallback。
# Phase 1 分页修正（v11 试验，已被后续实机纠正）：按实际返回推进 - 2026-08-19

- 实机确认 `count=20` 时 Frodo 首屏可能只有 `Raw=16`，且 Mapper `Skipped=0 / Duplicates=0`；这不是前端或 Mapper 丢数据。
- 参考已验证的 Frodo 导出实现：个人 interests 页可能因已下架/删除条目而少于请求 `count`，分页游标必须按实际 `interests` 数量推进，不能按请求 count 跳过。
- API 请求大小继续保持 20；游标从 `start += ApiCount` 改为 `start += RawCount`，例如首屏 `Raw=16` 后下一次请求从 `start=16` 继续。
- Provider 新增 pending 缓冲：内部按实际游标继续取数，Shell 每次尽量发布 20 个唯一影片，匹配当前 5 列 × 4 行界面；只有真正到列表末尾才允许不足 20。
- 去重覆盖已显示和 pending 条目；单次可见批次最多 10 次内部请求，防止异常 API 响应造成死循环。
- 日志保留逐页诊断，并新增 `Buffered / Published / Pending / InternalRequest / ApiHasMore`，方便确认实际游标和 UI 批次。
- 不修改 Shell UI、Douban Plus、Explore、详情、评价写入与 DOM fallback。
# Phase 1 实机修正：Frodo 映射诊断与日志上限 - 2026-08-19

- 实机已确认个人页 `Source=Frodo` 与连续分页可用；新增每页 `Raw / Mapped / Skipped / Duplicates / Added` 统计，区分 API 实际少返回、Mapper 跳过和 SubjectId 去重。
- Mapper 不再静默吞掉异常记录；仅记录索引、SubjectId、Frodo 状态和短原因，不写整条 API JSON。
- `diagnostic.log` 单文件限制 10 MiB，最多保留 3 个轮转归档；历史超大日志在升级后首次写入时直接丢弃，避免继续保留数百 MB 旧文件。
- 单条诊断消息限制 16384 字符，现有 `Payload=` / `ReadResult=` 等超长日志自动截断，避免完整 JSON 持续膨胀日志。
- 本轮不改 Explore、详情、评价写入、Shell UI 或 DOM fallback；仍在同一 Phase 1 分支上继续实机验收。
# v1.0 API 迁移 Phase 1：个人页 Frodo 读取 - 2026-08-19

- 个人页默认 `看过 / 想看 / 在看` 改为 Frodo `/api/v2/user/{uid}/interests` 首选读取，不再为首屏等待个人网页 DOM。
- 新增 `Api/FrodoOptions.cs`、`FrodoSigner.cs`、`FrodoClient.cs`、`FrodoModels.cs`、`FrodoPersonalMapper.cs`、`FrodoPersonalProvider.cs`、`DoubanStatusMapper.cs`。
- Frodo `done / mark / doing` 在 API 边界统一映射为既有 `collect / wish / do`，不向 Shell 或评价业务泄漏移动端状态名。
- Frodo 输出保持现有个人页 Shell item/paging/filter payload；API 分页在 C# 累积，继续兼容现有个人页“加载更多”替换式渲染。
- 非默认个人筛选和 API 失败继续走原 `QbDoubanPersonalSourceBridge` DOM 路线；旧 Douban Plus、Source WebView 与评价写入/官方回读均保留。
- 本阶段不修改 Explore、详情读取、评价写入或 `ReviewWriteCoordinator`。
- 真实登录态验收仍是发布门禁；静态检查/编译通过不能替代三状态、连续分页、筛选 fallback 和评价回归实测。
# v1.0 独立开发版：旧体系清理与头像链路移除 - 2026-08-12

- 保留影片文件名识别、自动绑定缓存、人工更正和 PotPlayer/播放结束提醒入口。
- 完整移除 C# 演职员头像读取、头像补全、头像缓存、头像资源代理及相关 Worker 任务。
- C# 演职员读取与缓存链路一并移除；演员、人物链接和页面图片由豆瓣页面/Douban Plus 自身处理。
- 旧 HTML 影视库、历史导入、历史 JSON 持久化和旧缓存型 AI 流程继续保持删除状态。
- 正式评分/状态/短评写入、删除和官方回读确认后端保留，供下一阶段实时内容 AI 接入。

# v1.0 独立试用版：搜索页面适配 - 2026-08-12

- 修复搜索页有结果却显示“没有找到相关作品”：兼容相对/协议相对影片链接，并处理结果晚于首屏挂载的情况。
- 重构搜索结果卡片：评分、评价人数、年份、可播放状态、影片事实和主创信息分层显示，评分独立高亮并禁止错行。
- 搜索卡片移除顶部绿色结果标识、可播放标签和评分后的尾部小数字；海报改为保持原始比例、不裁切不拉伸。
- 详情页评分数字改为高亮显示，星级和评价数固定同组，长元信息不再错行。
- 从新版搜索页或个人页打开详情后，右上角返回恢复原来源页面，不再直接落回旧影视库。
- 豆瓣页面导航期间隐藏本地影视库 WebView；保留本地 JSON 数据、详情缓存、评价事务和消息桥接作为后台依赖。
- `search.douban.com/movie/subject_search` 进入统一 Douban Plus 深色页面。
- 读取官方搜索结果 DOM，重绘结果卡片、海报、标题、元信息和分页链接。
- 搜索结果卡片可进入同窗口 Douban Plus 影片详情页。
- 待看按钮、右键菜单和搜索结果详情操作暂不纳入本轮，继续按阶段接入。
- 本轮仅属于 v1.0 独立试用版；正式 v0.9.0 发布目录、EXE、ZIP 和 SHA-256 保持不变。

# v0.9.0 Stable BuildFix12 R11 - 2026-08-09

## 开发副本：官方头像补全与 AI 证据容错 - 2026-08-10

- 首页演员缺头像时，在 WorkerWebView 读取同一影片官方 `/celebrities` 页面；不进入人物主页。
- 完整演职员解析限定 `#celebrities .celebrities-list > li.celebrity`，增加分批滚动懒加载、完整水合和连续稳定签名。
- 头像只接受 HTTPS 豆瓣图片域名下的 personage/celebrity 路径；跨人物重复 URL 直接隔离，下载检查图片类型、大小和文件签名。
- Worker 队列增加 `OfficialCastAvatarRead`，与显式 `FullCastRead` 按 SubjectId 合并；官方评价读取可抢占，评价完成后会重新安排，不让补全永久丢失。
- `CastParserVersion` 升级为 4，`FullCastParserVersion` 升级为 5，仅失效旧演职员缓存，不清评价。
- Wikidata 补充资料改为验证实体自身 `P345`；同 IMDb、同年份允许地区译名，冲突仍阻止。缓存升级为 `knowledge-v3-`。
- AI 保持 10 道内容题和恰好 3 道必答多选；可选补充保持可空。观点选项不再要求逐字影片证据；问题证据/结构失败时使用本地官方字段安全十题兜底。
- 修复影视库同步后筛选范围缩小为豆瓣当前 15 条在线页的问题；输入筛选词时改为搜索完整本地镜像，在线页继续增量合并，不再把旧影片错误显示为空。
- 开发构建 0 错误；内置自检 106/106、评价专项 18/18、AI 静态门禁 25/25、演员专项 34/34。真实豆瓣头像、真实 DeepSeek 和自动提交仍待端到端验证。

## 演职员头像来源收紧 - 2026-08-09

- 修复完整演职员页把豆瓣用户图标、海报或侧栏图片当成人物头像的问题。
- 头像现在只接受豆瓣 `/view/personage/` 或 `/view/celebrity/` 官方人物图片路径；读不到或路径不可信时保持无头像。
- 移除头像缺失时的人物主页导航和人物头像缓存回填，不再点击进入人物页；也不跨演职员卡片复制头像。
- `CastParserVersion` 升级为 3、`FullCastParserVersion` 升级为 4，启动时清理旧头像绑定缓存。

## AI 影评外部资料身份拦截 - 2026-08-09

- 修复 AI 生成问题前直接信任 IMDb/Wikidata 外部页面的问题：现在校验豆瓣片名、年份、IMDb 编号与外部资料页面标题/年份，不一致时直接阻止生成。
- 发现《天才游戏》取到《天才衝衝衝》资料时，清理错误外部缓存并提示重新读取豆瓣详情或重新识别。
- IMDb 已存在时不再退回仅按片名搜索的维基页面；外部资料缓存升级为 `knowledge-v2-`，避免继续读取旧版未校验缓存。
- 增加自测和静态门禁覆盖片名不一致阻止、片名/年份一致放行。

- 将已验证的 0.9.0-preview.6 / BuildFix12 R11 业务基线登记为 v0.9.0 稳定版。
- 不改变 R10 业务逻辑；仅更新稳定版版本元数据、构建入口、交接文档和发布摘要。
- 历史稳定版 v0.8.9 / stable-v0.8.9 保留，不覆盖原发布目录和哈希登记。

## 0.9.0-preview.6 BuildFix12 R11 - 2026-08-09

- 发布前文档/交接收尾；业务逻辑保持 BuildFix12 R10。
- README、STATUS、AI_HANDOFF、BuildFix12 实施报告、验收清单和包清单统一到当前主线。
- 新增 `docs/DEVELOPMENT_HISTORY.md`，集中记录 BuildFix5～BuildFix12 每轮更新。
- 新增 `docs/CURRENT_ARCHITECTURE.md` 与 `review/RELEASE_CANDIDATE_SUMMARY.md`。
- 构建脚本更新 R11 标识并携带当前 Markdown 交接文档。
- 增加 R11 文档一致性门禁。
- 当前长期稳定版仍为 v0.8.9；正式 v0.9.0 登记留作独立发布动作。

## BuildFix12 R5 - 2026-08-09

- 修复 Windows 编译错误 CS0136：`DeleteV2.cs` 同一方法作用域重复声明 `listWarning`。
- 仅重命名内部诊断变量，不改变 do-list 删除路由、官方回读或 tombstone。

## BuildFix12 R2 - 2026-08-09

- 修复 Windows 编译错误 CS0173：`null` 与 `JsonElement` 条件表达式改为显式 `JsonElement?`。
- 仅修诊断对象类型，不改变删除事务行为。

## BuildFix12 R1 - 2026-08-09

- 重新启用删除 v2 独立事务入口 `deleteEntry`，旧 `delete` WebMessage 保持禁用。
- 删除与保存同为 Worker 最高优先级；DetailWebView 不为删除跳转。
- 删除前读取官方状态，官方 DOM 操作后必须结算与官方回读确认才允许本地更新。
- 引入本地 tombstone：只清豆瓣个人评价镜像，保留影片资料、海报、演员、头像缓存和软件观看记录。
- 外部网页删除在后续官方无记录回读时也可同步 tombstone，防止旧历史分页复活。

## BuildFix12 R10 - 2026-08-09

- 恢复豆瓣历史自动同步：启动后缓存先显示、当前标签后台自动刷新。
- 切换看过/想看/在看自动同步，不再要求每次点击手动同步。
- 增加 5 分钟节流、前端 in-flight 防重和既有 Worker HistoryRead 合并保护。
- 登录重新可用或 WebView2 自动恢复后，当前标签自动恢复同步。
- 手动按钮改名“立即同步豆瓣”，继续作为强制刷新入口。

## BuildFix12 R9 - 2026-08-09

- Fixed Windows C# compile error CS0157 in `WorkerJobQueue.PumpAsync`: no control-flow exit from the `finally` clause.
- Browser-recovery pause is now recorded in `stopPumpAfterJob` during cleanup; `return` occurs only after `finally` completes.
- Preserves all BuildFix12 R8 WebView2 crash-recovery and do-delete performance behavior.

## BuildFix12 R8 - 2026-08-09

- `BrowserProcessExited` 改为宿主统一重建 Detail/Worker 两个 WebView2 控件，不再对失效 CoreWebView2 尝试原地初始化。
- 恢复期间暂停 Worker 队列，浏览器生命周期令牌立即熔断死亡导航；恢复后自动重读当前影片。
- 重复 HistoryRead 按状态/页合并，减少启动和恢复期重复导航。
- 浏览器崩溃错误不再回退 CDP 或误报未登录；有 dbcl2 时 connection-error 可恢复为 cookie-saved。
- do 删除每次点击后的旧长被动轮询改为 550ms 后新鲜重载 do 页。
- 自动第二击复用已经新鲜重载的当前 do 页，取消一次重复导航。
- do 删除成功后取消最终 wish/do/collect 重复扫描；fresh do settlement 本身保留为硬列表证据。
- 删除后官方详情回读改为一次导航 + 两个轻量 DOM 稳定样本，不再打开完整编辑表单两遍。
- do 删除成功条件不变：列表消失 + 详情状态/评分/短评/日期均空后才写本地 tombstone。

## BuildFix12 R7 - 2026-08-09

- 在看 do 删除支持豆瓣两阶段行为：第一次真实列表点击后若目标仍稳定存在，单次用户事务内部自动重新定位并执行第二次官方列表真实点击。
- 自动第二击仅在新鲜重载 /do 页面连续确认同一 SubjectId 仍存在时触发，最多两击。
- 最终成功仍要求 /do 列表消失、影片详情连续官方回读为空、do 列表硬复核通过。
- 增强 Pass=1/2、AutoSecondPass、clickPasses 诊断日志。

## BuildFix12 R6 - 2026-08-09
- `do / 在看` 删除第一页改用精确无参数 `/people/{ProfileId}/do`。
- R5 的 synthetic `HTMLElement.click()` 改为 Chromium `Input.dispatchMouseEvent` 真实鼠标输入。
- 删除按钮必须在 SubjectId 对应卡片内唯一匹配，并通过中心点遮挡检查。
- 增强 do-list 删除诊断日志；成功条件仍为列表消失 + 详情连续官方空回读。

# BuildFix12 R4（2026-08-09）

- `do / 在看` 删除固定改走豆瓣个人 `/people/{ProfileId}/do` 列表，不再使用影片详情页删除入口。
- 列表分页按 SubjectId 精确定位目标 `.grid-view .item`，删除控件只能来自该卡片内部且必须唯一。
- 删除仍只执行官方 DOM `node.click()`，不 fetch、不直接 POST、不导出 Cookie。
- do 删除成功必须经过：fresh do 页连续两次目标消失 → 两次官方详情读取状态/评分/短评/日期全空 → do 列表最终硬复核。
- 如果官方状态为 do 但列表找不到目标，明确返回未确认，不回退详情页删除。
- `wish / collect` 暂时维持 R3 的 SubjectDetail 路由，避免扩大变更面。
- 删除事务新增 `DeleteRoute` 日志和前端结果显示。
- 静态门禁：BuildFix12 44/44；BuildFix11 34/34；综合 95/95；协议 6/6；嵌入 JavaScript 全部通过。

# BuildFix12 R3（2026-08-09）

- 重做豆瓣个人评价删除：官方 DOM 删除控件、结算等待、连续详情回读、三状态列表复核、本地 tombstone。
- 删除和保存同为 Worker 最高优先级，开始后不可取消；DetailWebView 保持当前详情页。
- 删除成功只清个人评价镜像，保留影片资料、海报、演职员、头像缓存和软件真实观看记录。
- 豆瓣网页外部手动删除后，官方无评价回读会生成 tombstone，避免旧历史分页复活。
- 实机诊断确认豆瓣个人 wish/do/collect 列表可能在删除后短暂滞后于影片详情页；R3 将列表复核降级为诊断，不再反向否定已连续稳定确认的详情页删除结果。
- “在看”直接删除时，即使 do 列表短暂仍显示影片，只要官方详情连续两次确认 `status=none` 且删除控件消失，就按删除成功写入 tombstone，并记录列表传播延迟警告。

## 0.9.0-preview.6 BuildFix11 R3 - 2026-08-09

- 修复 FullCast 缺失头像没有进入人物主页最后一级补全的问题。
- `castAvatarUpdated` 增加 `castScope=home/full`，完整演职员列表显示期间可以逐张增量补图。
- 同一人物多职务只访问一次人物主页，但所有对应卡片分别保留并更新。
- WorkerWebView 从 1×1 改为 1024×768 离屏视口，提高豆瓣懒加载/IntersectionObserver 兼容性。
- `FullCastParserVersion` 升级为 3，只失效旧 FullCast 缓存，不影响评价、日期和首页 Cast。
- 复核确认 R2 的 `readImage()` 空返回已经位于节点循环之后，因此没有进行不存在的“提前 return”修复。
- 评价提交、官方回读、NoChange、删除禁用和 Worker 优先级语义均未改变。

## 0.9.0-preview.6 BuildFix11 R2 - 2026-08-09

- 修复 R1 Windows 构建后 legacy comprehensive self-test 的 3 个警告。
- 完整演职员头像读取在每个独立人物卡片内部补回 `data-background`、`::before`、`::after` 背景图兼容，同时保持禁止跨卡片/共同父节点取图。
- 更新旧 SelfTest 的缓存探针，使其显式使用 `CastParserVersion=2` / `FullCastParserVersion=2`，与 BuildFix11 缓存迁移规则一致。
- 不改变评价事务、Worker 队列、缓存 TTL、删除禁用和双 WebView2 架构。
- R1 已由用户在 Windows 成功编译；R2 当前仅完成源码与静态/JavaScript 验证，仍需在 Windows 再运行一次 `BUILD_PREVIEW.cmd`。

## 0.9.0-preview.6 BuildFix11 R1 - 2026-08-05

- 新增长期存在的 DetailWebView 与 WorkerWebView，共享同一 WebView2 Environment、Profile 和豆瓣登录状态。
- 详情资料与官方评价改为双通道并行启动；保存和完整演职员不再让详情控制器跳离当前影片。
- 新增 Worker 单消费者优先级队列：评价保存、官方读取、完整演职员、头像补全、后台任务依次让路。
- 首页演职员改为按豆瓣原始卡片保留最多 6 张，同一人物不同职务不再被人物级去重。
- 完整演职员改为逐个 `li.celebrity` 卡片解析，头像只取当前卡片，并在 `complete` 后进行双签名稳定采样。
- 新增统一头像有效性过滤、人物头像长期缓存与 `castAvatarUpdated` 增量更新。
- 新增 SubjectId + RequestId 过期结果保护；旧任务可写缓存但不能覆盖新条目界面。
- 新增 CastParserVersion/FullCastParserVersion=2，只失效旧演职员缓存，不清状态、评分、短评和日期。
- 评价事务语义保持 BuildFix10 已验证逻辑；删除评价继续禁用。
- 当前交付仅完成源码与静态测试，未在 Windows/.NET 8 环境真实编译运行。

## 0.9.0-preview.6 BuildFix10 - 2026-08-05

- R1 自检修正：旧综合自检不再把 `personage-default` 占位头像过滤关键词误判为“访问人物主页补头像”；运行逻辑未改变。
- 详情页改为本地缓存后优先读取影片首页资料，再读取每次必刷新的官方评价。
- 新增 24h/7d/7d 分层详情缓存 TTL。
- 首页演职员限制为豆瓣原序 6 位；完整名单仅点击后读取，不再逐个人物主页补头像。
- 编辑表单增加唯一状态语义完整性与 250ms 有限重采样。
- 官方 MarkedDate、NoChange、SubjectId 一致性和独立事务日志增强。
- 删除评价继续禁用。

## 0.9.0-preview.6 BuildFix9 - 2026-08-05

- 详情页改为缓存立即渲染，影片资料与官方评价分阶段、独立局部刷新。
- 评价官方读取完成前禁用保存，继续坚持豆瓣官方值为唯一权威。
- 主详情不再自动打开完整演职员页；完整演职员只在用户点击后读取。
- 同一完整影片页可复用元数据页面，减少重复导航。
- 新增 10 秒、一次性、同影片页、完整字段的官方评价快照复用；保存后结算和官方回读不变。
- 登录 Cookie 检查由约 5 秒降为 60 秒，并在详情、保存或导航锁忙时跳过。
- 保留 BuildFix8 清除评分事务、Keep/Set/Clear 协议、Uncertain 语义和删除禁用。

## 0.9.0-preview.6 BuildFix8 - 2026-08-05

- 修复“在看/看过有评分 → 想看并清除评分”在提交前被错误阻止。
- 清除评分改为服务器确认式事务：想看提交结算后官方回读确认无评分。
- 在看/看过清分改为“想看清分事务 → 最终状态事务”两阶段官方提交。
- 隐藏评分字段仍只读，绝不直接赋值。
- 失败但成功同步官方当前值时，不再显示“本地缓存已完成”。
- 保留官方回读、性能优化、诊断工具和删除禁用。

# Changelog

## AI 影评接入影视库 - 2026-08-09（独立开发目录）

- 影视库详情页新增“AI 问答写影评”面板：先选择 1-5 星，再回答基于当前豆瓣官方资料生成的 10 道多角度具体问题。
- 优化 AI 提问约束：问题和选项必须引用影片证据编号，拒绝“整体感受/喜欢吗”等泛化问题，并按评分方向追问优点、缺点或两面原因。
- 增加最后一项“补充说明（可选）”，可留空；填写内容会作为观众原话依据参与影评生成。
- AI 生成评分和短评后，复用现有豆瓣官方 DOM 表单、结算与官方回读事务自动提交；未获官方确认时不更新本地镜像。
- PotPlayer 播放完成提醒改为定位到影视库详情页，自动搜索并进行严格候选匹配；定位不确定、未登录或风控时阻止自动选择。
- 新增 `tests/validate_ai_review.py` 与 AI 相关内置自检，覆盖证据校验、重复问题拒绝、330 字上限、桥接白名单和可选补充。
- 详细文件范围、行为边界、验证结果和真实验证待办见 `AI_REVIEW_INTEGRATION.md`。
- 修复 AI 问题必失败缺陷：系统自动追加的“其他（请说明）”没有影片证据编号，旧校验器却要求所有选项必须有证据；现已仅豁免自由表达选项，具体影片选项仍严格校验。
- AI 问答增加硬性结构：10 道题中必须恰好有 3 道必答多选题，其余为单选题。

## 0.9.0-preview.6 BuildFix7 - 2026-08-05

- 将豆瓣官方确认与本地缓存更新拆分为独立结果字段。
- 在 HtmlMediaLibraryForm 完成本地权威覆盖后再生成最终前端回传。
- 连接器日志使用 `ConnectorCacheUpdate=Deferred`，不再将中间层 `LocalUpdated=False` 表达为最终失败。
- 成功 WebView2 导航不再输出误导性的 `Error=Unknown`。
- 前端以 `officialConfirmed` 判断豆瓣操作成功，并分别展示官方确认与本地缓存状态。
- 保留 BuildFix6 的详情性能优化、评分清除安全路径与删除禁用边界。

# BuildFix6 (2026-08-05)

- Corrected the failed `img#star0` assumption using a verified official-control wish round-trip for rating Clear.
- Added read-only verification before and after restoring the requested final status.
- Removed the redundant detail metadata status-capability form read.
- Made library startup and tab switching cache-first; online history refresh is explicit.
- Preserved requestSubmit, settlement, authoritative readback, and disabled deletion.

# 0.9.0-preview.6 - 2026-08-05

- 从 `preview.4` 完整源码集成评价写入 v2，不再依赖旧 EXE 的 IL 热补丁。
- 评分与短评改为 `Keep / Set / Clear` 三态协议。
- 保存前以豆瓣网页当前评价为准，官方明确为空时覆盖本地旧值。
- 官方表单通过真实控件和 `form.requestSubmit()` 提交；提交前复核字段。
- 结算需要连续稳定信号，并在官方回读逐字段匹配后才更新本地。
- 无法确认时返回 `unconfirmed`，不把目标值写入本地。
- 修复“创建表单可见即推断当前无评价”的风险。
- 已知不可靠的旧删除链路在前端和 WebMessage 白名单中停用，等待第二阶段重写。

# 0.9.0-preview.4（2026-08-05）

- 修复已删除条目无法由用户明确重新添加的墓碑死锁：保存前保留墓碑，只有豆瓣官方提交并稳定回读成功后才清除。
- 修复认证徽章 `ic_verify@2x.png` 被误识别成人物头像；排除 `/f/shire/` 界面素材并从人物页补取真实头像。
- 发现旧缓存中存在被拒绝的头像 URL 时，重新读取完整演职员页。
- 保留 `diagnostic.log`、`crash-*.log` 和一键诊断导出能力。

# 更新日志

## v0.9.0-preview.2 源码修复预览（2026-08-05，未正式发布）

### 状态能力统一

- 详情读取会打开豆瓣官方编辑表单并读取真实 `interestOptions`，将表单能力作为 `wish / do / collect` 的首选来源。
- 详情元数据缺失或不完整时，不再永久隐藏“在看”；能力未知时保留按钮并提示“保存时重新确认”。
- 官方表单已明确不支持某状态时，按钮保持可见但禁用，并显示具体原因。
- 状态能力来源、是否已确认和探测错误会随详情数据持久化，成功写入后再用官方回读能力刷新。

### 写入与回读稳定性

- 官方表单必须连续 3 次读取到相同关键字段后才允许填写或回读。
- 提交后增加明确的 `settled / timeout / captcha / login` 结果；只有连续稳定确认影片页已恢复且表单关闭，才进入官方回读。
- 提交未稳定、回读表单未稳定或请求字段不匹配时返回 `unconfirmed`，`localUpdated=false`，保留本地旧值。
- 结果模型和界面增加“提交稳定”信息，诊断日志记录表单采样次数、稳定样本数、能力来源和完整探针结果。

### 验证

- 源码与安全边界静态检查：45/45 通过。
- Chromium DOM/UI fixture：26/26 通过，覆盖电视剧“在看”能力未知、官方不支持时的禁用原因、一次提交和稳定状态展示。
- Windows 启动器格式检查通过；`app.js` 和全部写入相关内嵌 JavaScript 通过 Node.js 语法检查。
- 当前 Linux 环境没有 .NET SDK 和 Windows WebView2 Runtime，未执行 WinForms 编译、`--self-test` 或真实豆瓣账号回归；本包不包含伪造的 preview.2 EXE。


## 0.9.0-preview.1 launcher hotfix (2026-08-04)

- Replaced `BUILD_PREVIEW.cmd` with an ASCII-only CRLF launcher.
- Added explicit Windows PowerShell path and quoted script invocation.
- Added `build-preview.log` and launcher byte-format validation.
# 更新日志

## v0.9.0-preview.1 开发候选（2026-08-04，未正式发布）

### 统一豆瓣保存

- 将状态、评分和短评合并为唯一“保存到豆瓣”操作，不再由 HTML 界面暴露三个独立写入入口。
- 固定当前豆瓣 Profile 与影片快照，探测官方编辑表单能力，填写请求字段后只点击一次官方提交控件。
- 提交后返回影片页、重新打开官方编辑层并回读 `interest/rating/comment`；只有全部请求字段匹配才更新本地豆瓣镜像。
- 评分使用 nullable 语义；未选择时不提交，不再用 0 表示评分。`wish + rating` 在前端禁用并由 C# 再次拒绝。
- 官方状态、评分或短评控件缺失时提交前阻止；提交已可能触发但回读不确定时返回 `unconfirmed`，本地保持不变。
- 写入结果包含 `phase/stage/requested/official/localUpdated/error`，界面保留具体错误、重试和重新读取入口。
- 删除仍为独立危险操作；修复删除失败后保存区域持续锁死的问题。

### 诊断与兼容

- 合并 WebView2/CDP JSON 信封、JavaScript 异常上下文、剧情简介多级回退和演职员头像多级提取修复。
- WebView2 脚本错误日志包含错误描述、堆栈、当前 URL、标题和 `readyState`。

### 本环境验证

- `app.js` 与 7 段豆瓣写入内嵌 JavaScript 通过 Node.js 语法检查。
- Playwright + Chromium DOM fixture 验证：状态、评分、短评只提交一次；评分控件缺失时零提交；nullable 评分不会清除网页已有评分。
- Playwright UI fixture 验证：仅发送一个 `saveDoubanEntry`；失败保留编辑值；删除失败后恢复界面。
- 当前 Linux 容器没有 .NET SDK，未在本环境执行 WinForms 编译或真实登录豆瓣 WebView2 写入；包内提供 Windows 一键构建脚本。

## v0.8.9 源码修复补丁（2026-08-04，未变更版本号）

### 修复

- 修复豆瓣详情页 `Runtime.evaluate` 对复杂对象返回不稳定时，剧情简介和初始演员信息等待约 30 秒后全部为空的问题。WebView2 与 Chrome CDP 现在统一在页面内序列化结果，并记录 JavaScript 异常、异常描述、堆栈、当前 URL、标题和 `readyState`。
- 连续 3 次脚本异常后立即停止无意义重试，保留本地数据并输出明确失败原因；页面尚未完成正常加载时仍保留原有等待机制。
- 扩展详情页元数据回退：片名、海报、类型、导演、演员和剧情简介支持传统豆瓣 DOM、Open Graph 与 JSON-LD。
- 修复完整演职员页姓名可读但头像始终为空的问题。人物卡片不再只依赖旧版 `.celebrity/.list-item/li`，支持通用 `.item/card/person` 结构，并从 `currentSrc/src/srcset`、懒加载 `data-*`、内联及计算后的 `background-image`、伪元素、dataset 和外层 HTML 中提取头像。
- 人物详情页头像增加 Open Graph、JSON-LD、匹配图片和内联脚本回退；协议相对 URL 统一补全为 HTTPS，并过滤默认占位图。

### 验证

- 三段豆瓣 DOM 脚本均通过 Node.js 语法检查。
- 使用 Chromium 构造详情页、完整演职员页和人物页 DOM 回归：片名、剧情简介、初始演员头像、背景图头像、懒加载头像和 Open Graph 头像均成功提取。
- 增加内置静态自检，覆盖 JSON-LD/简介回退、新版头像来源、协议相对 URL 与默认头像过滤。
- 当前打包环境未安装 .NET SDK，因此未在本环境执行 WinForms `dotnet build`；源码未改动项目架构、数据格式或版本号。

## v0.8.9

日期：2026-08-02

类型：HTML 影视库与内置豆瓣登录

### 新增

- 保留 .NET 8 WinForms 托盘外壳，新增独立本地 WebView2 影视库，使用本地 HTML/CSS/JavaScript 展示历史、搜索、详情和演职员。
- 新增豆瓣官方扫码登录窗口；登录页和隐藏读取实例共享观影助手专用 WebView2 Profile，不读取、输出或复制 Cookie。
- 登录后优先使用内置 WebView2 完成豆瓣搜索、分页、详情和完整演职员读取；现有 CDP 链路继续作为回退。
- 豆瓣“想看、在看、看过”按 DOM 原文分开展示，并明确标记当前状态。

### 安全与兼容

- 本地界面和远程豆瓣使用不同 WebView2 控件与不同安全边界；远程页面不启用 WebMessage 或宿主对象。
- 本地桥接只接受固定虚拟来源、固定操作和受限 JSON；头像继续通过 C# 的 300MB、4 路并发缓存提供。
- 旧版 WinForms 影视库、独立 Chrome/Edge、PotPlayer、爱奇艺、PT-Depiler 和原有数据格式均保留。
- 缺少 WebView2 Evergreen Runtime 时显示中文说明和微软官方安装入口，非 Web 功能不随之移除。

### 验证

- 豆瓣历史列表改为实时分页优先：内置 WebView2 读取当前页并同步本地镜像，读取失败时明确回退到本地数据；卡片和详情均显示“我的评分”。
- 切换详情时先清空上一部影片的海报、评分、状态和演职员，且忽略过期的异步详情响应。
- 修复已扫码登录后历史页仍被判定为未登录的问题：不再把 HTML 中任意登录链接当作退出状态，增加最终 URL、页面结构和登录表单的联合判断及诊断日志。
- 豆瓣实时同步改为解析当前账号的 `people/{id}/collect|wish|do` 页面，从“看过”页面读取个人星级、短评、标签、日期和海报，不再依赖首页数据。
- 修复 WebView2 `ExecuteScriptAsync` 执行异步 `fetch` 返回空结果的问题：先导航到当前账号页面发现 Profile ID，再导航到目标列表页，用同步 DOM 脚本读取评分和短评；未登录、验证码、页面未就绪分别处理。
- Release 构建 0 警告、0 错误；内置自检扩展为 51 项。

### 后续修复

- 豆瓣 `has_douban` 默认图改为“待补全”状态；存在豆瓣人物页链接时，受限读取官方人物页头像，读取失败仍保留姓名和岗位，不把默认图展示为真实头像。
- 登录窗口不再在后台轮询成功后瞬间关闭，改为显示“已验证”并等待用户显式关闭；补充 WebView2 进程、导航和未观察异常日志，降低扫码窗口闪退时的无提示退出。
- 静态自检覆盖 Runtime 探测、本地资源、消息来源/操作、导航边界、专用 Profile、图片 MIME 和豆瓣状态结构；真实扫码及页面回归结果以发布报告为准。

### 本轮会话与在线同步修复（验证包）

- 单独持久化豆瓣会话状态和 Profile ID；界面显示“已登录、登录信息已保存、未登录、需要验证、连接异常”等明确状态。
- 有已保存 Profile ID 时直接进入当前账号 `people/{id}/collect|wish|do` 页面读取在线数据，不再每次先访问首页；没有 Profile ID 才执行一次登录验证。
- 移除登录窗口后台轮询，改为用户扫码后手动点击“验证登录”；登录窗口打开期间禁止并发历史同步，并等待现有读取任务空闲，降低 WebView2 进程冲突。
- 影视库显示“在线（内置豆瓣/浏览器回退）”或“本地缓存”数据来源，手动同步按钮仍可用于回归测试。
- Release 构建 0 警告、0 错误；内置自检 54/54；HTML JavaScript 语法检查通过。
- 用户已完成当前验证包实际使用测试并确认运行正常，同意按正式发布规范登记为稳定版。

### 稳定版登记

- 源码标签 `v0.8.9` 固定在功能提交 `805a4b2`；正式发布登记提交为 `40e17dc`。
- 正式发布目录使用不可覆盖名称，EXE、`VERSION.json` 与 `SHA256SUMS.txt` 哈希一致。
- v0.8.9 登记为新的长期稳定版，稳定标签为 `stable-v0.8.9`；旧 v0.8.6 稳定基线继续冻结保留。

## v0.8.8

日期：2026-08-02

类型：影视库整合、在线搜索分页与 PT-Depiler 跳转修复

### 修复

- 豆瓣在线搜索改为真正的分页切换：上一页和下一页按每页 15 条映射豆瓣 `start` 参数，每次替换当前结果并回到列表顶部。
- PT-Depiler 搜索不再模拟键盘输入，避免中文输入法改写 IMDb 编号或落入 Google；现在直接打开扩展自己的搜索页并使用 `imdb|tt...` 查询格式。

### 优化

- 将“看过、想看、在看、软件记录、豆瓣在线搜索”合并为同一个“影视库”窗口和托盘入口。
- 在线搜索详情继续写入独立 `douban-search-cache.json`，不会混入豆瓣历史或软件观看记录。

### 验证

- 真实后台搜索“蜘蛛侠”：`start=0` 与 `start=15` 均返回 15 条候选，首条 subject ID 分别为 `36246195`、`3766061`，确认下一页不是重复或无响应。
- 真实打开 PT-Depiler 标签页，最终 URL 为扩展内部 `#/search-entity` 路由，查询参数为 `imdb|tt0316654`，程序空白 Target 为 0。
- 《来福大酒店》真实详情回归：简介 183 字，首页姓名/岗位/头像/图片解码 8/8，完整演职员姓名/岗位/头像/图片解码 27/27。
- Release 构建 0 警告、0 错误；内置自检 31/31。

## v0.8.7

日期：2026-08-02

类型：豆瓣在线搜索与 PT-Depiler 跳转

### 新增

- 新增豆瓣在线搜索窗口；候选项按豆瓣搜索结果的原始可见文本、海报和顺序显示，支持明确选中后再读取详情。
- 搜索候选不写入“看过、想看、在看”历史；用户打开过的详情仅保存在独立搜索缓存中。
- 详情读取到合法 IMDb 编号后，可显式打开观影浏览器的 PT-Depiler 关键词搜索。

### 验证

- 真实后台搜索“蜘蛛侠”返回 15 条豆瓣候选；原始可见文本包含标题、评分、评价人数、影片信息、主创摘要及用户状态。
- 搜索结束后浏览器的程序专用空白 Target 为 0。
- Release 构建 0 警告、0 错误；内置自检 29/29。

## v0.8.6

日期：2026-08-02

类型：后台标签页静默清理与头像缓存加速

### 优化

- 每次豆瓣详情读取结束后通过精确的 CDP Target ID 关闭程序创建的后台标签页，不再把它导航为空白页后遗留在浏览器中。
- 启动后台连接时静默清理历史遗留的 `about:blank` 和程序专用标记页；不会清理真实豆瓣、爱奇艺或其他网页。
- 主详情读取期间已经取得的完整演职员立即写入本地历史，点击“查看全部演职员”时直接复用，不再重复访问完整演职员页。
- 增加 300MB 演职员头像缓存，以原始头像 URL 为键；命中时直接显示，并按最近使用时间自动淘汰旧图片。
- 保持最多 4 路头像下载并发，不增加 NuGet 包、运行库或额外 DLL。

### 验证

- 真实观影浏览器验证前有 14 个页面，其中 10 个为历史遗留空白页；运行后剩余 4 个原有真实页面，空白页和程序标记页均为 0。
- 《来福大酒店》：简介 183 字、首页头像 8/8、完整演职员头像 27/27 下载并解码成功。
- 内置自检覆盖精确空白页判定、头像缓存读写和 300MB 容量配置。

## v0.8.5

日期：2026-08-02

类型：完整演职员头像与 WebP 兼容修复

### 修复

- 修复完整演职员界面绑定 `FullCast`、图片下载却错误遍历 `Cast`，导致全部头像请求未执行的问题。
- 豆瓣 `.webp` 头像优先尝试同路径官方 `.jpg` 变体，避免 `System.Drawing.Image.FromStream` 无法解码 WebP。
- 图片下载限制为最多 4 路并发，降低完整列表集中请求造成的失败风险。
- 图片成功、HTTP 错误、格式错误和解码错误写入持久诊断日志，不再只写调试输出。
- 不增加图片解码库或 DLL；JPEG 失败时仍保留原始 WebP URL 作为回退。

### 验证

- 《第一次遇见花香的那刻2》：首页可用头像 6/6、完整演职员 12/12 下载并解码成功。
- 《来福大酒店》：首页 8/8、完整演职员 27/27 下载并解码成功，其中包含 10 张原始 WebP 头像。
- 内置自检增加 `FullCast` 下载目标和 WebP/JPEG 候选顺序回归测试。

## v0.8.4

日期：2026-08-02

类型：豆瓣简介、主演头像与完整演职员修复

### 修复

- 修复主详情页图片节点为空时布尔值进入 `getAttribute` 导致整段 JavaScript 中断的问题，恢复标题、导演和剧情简介读取。
- 主详情页按豆瓣主演链接保留前 8 人，并从完整演职员页补齐缺失的原始头像 URL；不使用截图。
- 修复 CSS `background-image` 头像已经提取却因姓名链接选择错误而丢失的问题。
- “查看全部演职员”读取并显示所有有个人页面的演职员姓名、头像和岗位，不再只显示演员。
- 首页主演与完整演职员使用独立缓存，完整列表不再覆盖首页 8 名主演。
- 详情完成状态改为校验片名、简介、导演和 1 至 8 名主演；旧版空简介或被完整演职员覆盖的记录会自动重试。

### 验证

- 真实豆瓣影片验证：简介 303 字，首页 8 人/8 头像/8 岗位，完整演职员 56 人/56 头像/56 岗位。
- 首张原始头像由程序 `HttpClient` 返回 HTTP 200、`image/jpeg`，并由 `Image.FromStream` 解码为 540×761。
- 后台 Chrome 无窗口弹出；失败时保留姓名和空白头像位，不启用截图兜底。

## v0.8.3

日期：2026-08-02

类型：浏览器窗口恢复与豆瓣读取并发修复

### 修复

- 用户主动启动观影浏览器时，通过 CDP 和 Win32 将最小化、隐藏或屏幕外窗口恢复到正常位置和尺寸。
- 后台启动不再把窗口移动到 `-32000,-32000` 或缩成 `1×1`。
- 豆瓣详情和全部演职员读取使用同一导航锁，禁止并发改写后台 Target。
- 豆瓣读取固定使用专用后台 Target，不再导航用户正在查看的豆瓣页面。
- 明确识别 `/misc/sorry`、“禁止访问”和机器人验证页面，停止读取并保留原缓存。
- 页面未真正就绪时禁止接受 `Title=`、`CastCount=0` 等空结果。
- 详情自动补全增加缓存优先和失败冷却，减少重复请求及风控概率。
- 批量补全不再用空字段覆盖已有导演、演员和简介。

## v0.8.2

日期：2026-08-02

类型：独立轻量发布与不可覆盖版本管理

### 发布调整

- 默认改为依赖本机 `.NET 8 Desktop Runtime x64` 的 framework-dependent 单文件发布，不再携带完整 .NET 运行时。
- 正式发布目录统一为 `发布版本`，禁止使用 `bin`、`obj` 或旧 `release` 目录交付。
- 发布文件夹明确包含版本号、核心改动、Git 提交号和 `net8轻量版` 标识。
- 每个版本附带 `VERSION.json`、版本说明、内置自检结果和 SHA-256 校验值。
- 发布脚本发现同名目录时立即失败，禁止覆盖既有版本。
- 重新构建 v0.5.0 至 v0.8.1 的全部历史版本轻量发布物。

## v0.8.1

日期：2026-08-02

类型：豆瓣 DOM/CDP 诊断增强

### 优化

- 记录 Runtime.evaluate 的异常详情、执行结果、页面 URL、标题、readyState 和页面摘要。
- 记录完整演职员页首个人物节点及头像节点 HTML，便于按真实 DOM 修复选择器。
- 扩展完整演职员头像提取：src、currentSrc、srcset、懒加载属性和 CSS 背景图。
- 保留姓名和 ProfileUrl，即使头像 URL 为空也不会丢失人物。

### 兼容性

- 不改变 Biography、HttpClient、缓存或 CDP Target 架构。

## v0.8.0

日期：2026-08-02

类型：后台观影浏览器与完整演职员列表

### 新增

- 打开“我的观看历史”时自动复用已有观影浏览器 Profile，在后台启动 Chrome，不弹出浏览器窗口。
- 详情页先显示豆瓣当前页面的前 8 位演职员。
- 增加“查看全部演职员”按钮，在当前详情窗口加载完整演职员列表。
- 完整列表包含导演，缺少头像时保留姓名和空白头像位。

### 调整

- 移除主演 Biography 对电影详情读取的阻塞；当前只读取姓名和头像。
- 保留已有豆瓣登录 Profile，登录失效时仍可通过手动登录流程重新授权。

## v0.7.2

日期：2026-08-02

类型：修复主演数据未注入 Biography 流程

### 修复

- 将历史记录中已有的 `DoubanCastMember` 传入豆瓣详情读取脚本。
- 当电影页演员选择器返回空结果时，使用已有演员姓名、头像和 ProfileUrl 继续 Biography 流程。
- 合并电影页新提取演员与历史演员数据，避免 `CastCount=0` 直接跳过主演简介读取。
- 在人物页读取前记录演员姓名和 ProfileUrl。

### 兼容性

- 不改变人物主页 fetch 架构，不使用截图或第三方 API。

## v0.7.1

日期：2026-08-02

类型：主演简介执行链诊断

### 优化

- 启动时记录应用版本、Git 提交、EXE 路径和构建时间。
- 记录主演简介流程的开始、逐演员结果、错误和最终统计。
- 每次读取后强制将主演简介状态从 `pending` 闭合为 `success`、`empty` 或 `failed`。
- 记录每个演员简介的最后尝试时间，便于确认是否真正执行并写回历史数据。

### 诊断日志

- 日志位置：`%LOCALAPPDATA%\\DoubanBrowserReminder\\logs\\diagnostic.log`
- 本版本暂不改变人物主页读取架构，先用于确认执行链和保存链路。

## v0.7.0

日期：2026-08-02

类型：增加主演个人简介

### 新增

- 通过豆瓣电影页面中的演员主页链接读取主演个人简介。
- 保存主演简介、读取状态、失败原因和最后尝试时间。
- 在主演卡片中显示演员姓名和个人简介。

### 兼容性

- 继续使用豆瓣页面上下文，不接入第三方 API，不使用截图。
- 既有历史数据可在重新读取豆瓣详情后补充简介字段。
- 演员主页访问失败时保留演员姓名和头像，不影响其他演员。

## v0.6.1

日期：2026-08-02

类型：豆瓣演员头像诊断与绑定修复

### 修复

- 修复演员头像因筛选数组索引导致的演员与图片错位。
- 扩展豆瓣懒加载图片属性、`srcset` 和多种演员卡片结构的提取。
- 增加头像来源、加载状态、失败原因和最后尝试时间。
- 增加 HTTP 状态码、Content-Type、内容长度和图片解码失败诊断。

### 兼容性

- 不使用截图、不接入第三方 API，不改变既有用户数据目录。
- 图片下载失败时不影响其他演员头像加载。

## v0.6.0

日期：2026-08-02

类型：豆瓣演员图片直链优化

### 优化

- 直接保存并下载豆瓣页面返回的演员头像图片链接，不再将浏览器截图作为图片来源。
- 优先尝试豆瓣演员大图规格，失败后回退到页面原始图片链接。
- 已保存的演员图片链接可在没有浏览器连接时直接加载。

### 兼容性

- 不改变既有豆瓣历史 JSON 数据格式和用户数据目录。
- 图片无法直接访问时不生成替代截图，界面保留空白图片位。

## v0.5.0

日期：2026-08-02

类型：项目接管与版本合并

### 新增

- 将原普通版、AI 版和浏览器版统一为“观影助手”项目。
- 以浏览器版 v4（历史稳定分页 UI）作为唯一源码主线。
- 建立 README、VERSION、CHANGELOG、STATUS、PROJECT_AUDIT 和 `.gitignore`。

### 优化

- 保留最新 v4 单文件发布包，移除旧版本发布目录和重复压缩包。
- 项目程序集名称统一为 `观影助手`。
- 保留原 `%LOCALAPPDATA%\DoubanBrowserReminder` 数据目录以兼容已有用户数据。

### 修复

- 本次未重构业务核心；审计中记录的行为问题留待独立修复提交。
# v1.0.1 稳定版：稳定版交接与版本递增 - 2026-08-13

- 当前版本正式递增为 `v1.0.1`，状态明确为稳定版，稳定标签为 `stable-v1.0.1`。
- 重新生成 v1.0.1 稳定版交接文档，明确当前包含能力、已删减内容、未纳入范围、验证结果和真实 UI 验收边界。
- v1.0.0 作为上一稳定版保留，正式 v0.9.0 和历史 v1.0.0 产物不覆盖。
- 业务功能边界保持已验收的 Douban Plus 个人页、搜索当前页、真实详情页、个人页无限滚动和详情返回位置恢复。
- 选电影页 `/explore` 适配、搜索页无限滚动和新 AI UI 继续留待后续版本。

# v1.0.0 稳定版：Douban Plus 页面主线 - 2026-08-13

## 稳定版结论

- 当前版本正式登记为 `v1.0.0`，稳定标签为 `stable-v1.0.0`。
- 用户已完成真实 UI 验收：个人页无限滚动正常；进入详情页后返回可恢复原有位置。
- Release 编译 0 警告、0 错误；评价专项自检 18/18；综合自检 70/70；Douban Plus-only 检查 39 项通过。
- 正式 v0.9.0 BuildFix12 R11 的 EXE、ZIP、发布目录和哈希保持不变；历史 v0.8.9 不覆盖。

## 本版新增与修复

- 保留 Douban Plus 个人页 `collect / wish / do`，支持同源分页读取、SubjectId 去重、加载失败重试和无限滚动。
- 修复个人页进入详情后返回位置丢失：保存页面 scope、SubjectId、视口偏移和 scrollY，返回时先加载目标卡片再恢复位置。
- 搜索页保留当前页解析、卡片重绘和豆瓣原生分页，不启用搜索页无限滚动。
- 修复搜索卡片事实信息分隔符错误显示为“路”，恢复为中点“·”。
- 详情页继续使用同窗口 Douban Plus 页面，并支持返回原来源页面。

## 本版删减与边界

- 删除旧本地 HTML/原生影视库、历史导入、历史 JSON 持久化和旧缓存型 AI 流程。
- 删除 C# 演职员读取、人物/头像读取补全、头像缓存和资源代理；演员与图片由当前豆瓣页面/Douban Plus 处理。
- 删除搜索页无限滚动实验及其自动分页 fetch、iframe fallback、sentinel 和自动追加逻辑。
- 选电影页 `/explore` 适配不属于 v1.0.0，留待独立 ExplorePageAdapter 阶段。
- 新的实时内容 AI UI 不属于 v1.0.0；正式豆瓣评价写入/删除/回读后端保留。
