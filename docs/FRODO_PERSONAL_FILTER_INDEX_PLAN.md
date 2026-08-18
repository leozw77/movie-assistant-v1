# Frodo 个人库快速筛选：字段矩阵与全库索引方案

日期：2026-08-19  
基线分支：`chatgpt/frodo-personal-20260819`  
检查 HEAD：`983f9e408d43b89bcb169a3cff92a821f5adfedb`

## 1. 本阶段边界

本阶段只推进：

1. 诊断日志瘦身。
2. Frodo 筛选字段能力矩阵。
3. 全个人库索引方案。
4. 为下一步本地筛选实现确定协议边界。

本阶段不修改评价写入/删除/官方回读，不修改已通过实机验证的固定 `0/20/40...` Frodo 分页算法，不迁移 Explore/详情 API，不做奖项筛选。

## 2. 当前代码已稳定映射的字段

| Shell / Model 字段 | Frodo 来源 | 当前是否已有 | 可直接本地筛选/排序 | 备注 |
|---|---|---:|---:|---|
| `subjectId` | `subject.id` | 是 | 是 | 去重主键 |
| `title` | `subject.title` | 是 | 是 | 可标题排序 |
| `year` | `subject.year` | 是 | 是 | 可年份筛选/排序 |
| `countries` | `subject.countries` | 是 | 是 | 缺失时当前 Mapper 会尝试从 `card_subtitle` 推断 |
| `genres` | `subject.genres` | 是 | 是 | 可 Genre 筛选 |
| `contentType` | `subject.type/subtype` | 是 | 是 | 当前统一为 `movie/tv` |
| `score` | `subject.rating.value` | 是 | 是 | 豆瓣评分排序 |
| `ratingCount` | `subject.rating.count` | 是 | 可 | 当前第一版筛选不必使用 |
| `myRating` | `interest.rating` | 是 | 是 | 支持 1-5 星和未评分 |
| `status` | `interest.status` | 是 | 是 | collect/wish/do |
| `markedDate` | `interest.create_time` | 是 | 是 | 最近标记排序 |
| `comment` | `interest.comment` | 是 | 可 | 第一版不做全文搜索 |
| `directors` | `subject.directors` | 是 | 可 | 第一版不做导演筛选 |
| `cast` | `subject.actors` | 部分 | 可 | Mapper 当前只保留前 2 位演员 |
| `posterUrl` | `subject.cover_url` / `pic` | 是 | 否 | 展示字段 |
| `intro` | `card_subtitle` | 是 | 否 | 当前并非完整剧情简介 |

## 3. 当前不能宣称 API 已支持的字段

### 用户个人标签 / tags

当前 `FrodoModels`、`FrodoPersonalMapper` 都没有 tags 字段，也没有已提交的真实 raw response fixture 可证明 `/api/v2/user/{uid}/interests` 一定包含或一定不包含标签。

因此结论只能是：

- 当前程序：**未支持**。
- 当前接口真实 response：**待实机字段确认**。
- 在确认之前：不能把 DOM 标签筛选包装成 API 标签筛选。

建议后续只记录首个有效 response 的**字段名 schema**，不记录字段值和整段 JSON，例如：

`Frodo schema; InterestKeys=...; SubjectKeys=...; HasTags=True/False`

这样既能确认 tags，又不会把日志重新做大。

### 可播放 / 有视频

当前 Frodo personal model 没有稳定对应字段。继续保留旧 DOM fallback，直到确认 Frodo 有等价字段/接口。

### 奖项

不属于 Frodo 个人 interest 基础字段。后续使用 `个人 SubjectId 集合 ∩ 本地奖项 SubjectId 索引`，不进入第一版个人筛选。

## 4. 第一版筛选能力

只使用现有稳定字段：

- 状态：看过 / 想看 / 在看
- 内容类型：全部 / 电影 / 剧集
- 我的评分：全部 / 1-5 星 / 未评分
- 年份
- Genre
- 国家/地区
- 排序：最近标记 / 我的评分 / 豆瓣评分 / 年份 / 标题

筛选规则：

- 不同维度之间使用 AND。
- 同一维度第一版先保持单选，避免 UI 和查询协议过早复杂化。
- “未评分”必须作为显式值，而不是把 0 星和缺失混在其它分组。
- 排序使用稳定次序，主排序相同后以 `markedDate`、`subjectId` 做确定性兜底。

## 5. 为什么不能直接 filter 当前 Shell items

当前 `FrodoPersonalProvider` 的 `_items` 是“用户已经滚到哪里就累计到哪里”。

例如只滚到 60 条时：

`_items.Count == 60`

如果此时直接在 JS/Shell 过滤，只能得到这 60 条里的结果，不能回答“我的全部日本五星影片”。

因此本地筛选必须依赖一个与可见分页解耦的**完整个人库索引**。

## 6. 推荐全库索引结构

新增独立组件，不改现有可见分页算法：

```text
FrodoPersonalProvider
  └─ 继续负责首屏 + 可见 20 卡分页

FrodoPersonalIndexService
  ├─ 按 profileId 管理完整索引
  ├─ collect / wish / do 独立扫描
  ├─ 固定 start=0,20,40... 读取
  ├─ SubjectId 去重
  ├─ 生成 facet
  ├─ 原子写本地 cache
  └─ 提供 Query()

FrodoPersonalQuerySession
  ├─ 保存当前筛选条件
  ├─ 保存排序结果 SubjectId 序列
  └─ 本地分页 20/40/... 返回 Shell
```

关键原则：

> 全库索引扫描可以复用相同的固定槽位规则，但不要调用/改写 `FrodoPersonalProvider.LoadMoreAsync()` 来“滚完 1062 部”。

Provider 是可见列表状态机；IndexService 是完整数据状态机，两者职责必须分开。

## 7. 索引缓存

建议位置继续使用应用 LocalApplicationData 数据目录，文件采用独立 schema version，例如：

`frodo-personal-index-v1.json`

建议结构：

```json
{
  "schemaVersion": 1,
  "profileId": "123456",
  "builtAtUtc": "...",
  "statuses": {
    "collect": { "complete": true, "total": 1062, "items": [] },
    "wish": { "complete": true, "total": 123, "items": [] },
    "do": { "complete": true, "total": 0, "items": [] }
  }
}
```

写盘采用临时文件 + replace/move，避免程序退出时留下半个 JSON。

第一版优先正确性：完整扫描成功后再把新索引整体切换为 active；扫描失败则继续使用上一个完整缓存，不把“部分索引”冒充全库结果。

## 8. 前台体验

推荐流程：

```text
进入个人页
→ 现有 Frodo Provider 仍快速返回首屏
→ 若有完整本地索引：筛选立即可用
→ 后台刷新完整索引
→ 刷新成功后原子替换
```

首次没有缓存时：

```text
首屏照常显示
→ 后台建立完整索引
→ 筛选区显示“正在建立个人库索引 240 / 1062”
→ 完成后启用全库筛选
```

不要在索引未完整时返回“看起来正常但只覆盖前 60 部”的筛选结果。

## 9. Shell 协议建议

不要把 1000+ 条一次性塞给 WebView。

本地 Query 返回一个新的分页会话：

```text
doubanShellApplyLocalPersonalFilter
→ C# Index.Query(filter, sort)
→ 第一批 20 条
→ Shell render replace
→ IntersectionObserver
→ doubanShellLoadMore
→ C# QuerySession 返回下一批 20
→ Shell append
```

这样保留现有无限滚动体验，同时真正做到全库筛选。

## 10. 日志瘦身修改

本阶段建议先在 `DiagnosticLogger` 中集中做兼容性清洗，避免为纯日志修改大范围动 `HtmlMediaLibraryForm.cs`：

- `Unified Shell data posted; ... Payload={巨大 JSON}`
  → 解析 payload 后只保留：
  `RequestId / Generation / Source / Operation / Status / Items / Bytes / Error`
- `Unified Shell Source read completed; ... ReadResult={巨大 JSON}`
  → 只保留 `ReadResultBytes`
- `Type=doubanShellPosterFallback` 的 generic message success log
  → 静默
- `Unified Shell poster fallback posted` 的逐张 success log
  → 静默
- 真正 poster 下载失败日志继续保留。
- Frodo 分页关键统计完全不变。

这一步不改变程序行为，只改变 `diagnostic.log` 的噪声量。
