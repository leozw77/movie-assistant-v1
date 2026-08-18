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
