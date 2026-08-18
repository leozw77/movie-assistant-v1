# v1.0 Douban Plus 开发记录（2026-08-12）

## 本次清理

- 删除“从豆瓣导入历史”托盘入口和导入窗体。
- 删除导入专用的历史分页读取、CDP 回退和 `HistoryRead` 队列任务。
- 停用并移除旧的缓存型 AI 问题/影评流程；新的 AI 将基于当前豆瓣详情页、短评和长评重新设计。
- `Store` 不再读取或写入 `douban-history.json`、`douban-search-cache.json`。
- 保留豆瓣登录、详情读取、评分/状态/短评正式写入和官方回读确认链路。
- 删除无运行时引用的 `MovieInputForm.cs`、`PersonAvatarCache.cs` 和旧版 AI/BuildFix 静态测试。
- 将正式评价写入所需的 `IOfficialReviewCacheWriter` 契约独立为 `ReviewCacheContracts.cs`；删除不再使用的旧缓存合并策略。
- 构建脚本不再把旧版 review 报告和已失效测试复制到 v1.0 包中。
- 完整移除 C# 演职员头像读取、头像补全、头像缓存、头像资源代理和相关 Worker 任务；页面图片由豆瓣网页重绘自身负责。

## 数据删除

已删除应用数据目录中的：

- `douban-history.json`
- `douban-history.before-cdp-repair.json`
- `douban-search-cache.json`

同时删除了已确认的开发备份副本。现有历史数据不再作为后续 AI 的输入。

## 进程处理约定

如果文件被正在运行的观影助手进程占用，先确认进程属于当前 `v1.0-douban-plus` 开发副本，再由开发流程自行关闭该进程后继续删除或构建；不询问用户。

无法确认进程归属、或疑似正式版/其他应用的进程，不自动关闭。

## 验证边界

- 当前一次 `dotnet build -c Release --no-restore`：0 错误，仍有 2 个既有警告待后续整理。
- 当前清理尚未重新生成最终 v1.0 发布包；发布包必须在本轮验证完成后重建。
- 正式豆瓣写入链路未删除；新的实时内容 AI 尚未开始重做。
