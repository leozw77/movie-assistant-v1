# Douban Plus v1.0 清理交接

日期：2026-08-12

目录：`D:\chatgpt\观影助手\开发\v1.0-douban-plus`

## 当前结论

v1.0 当前只使用 Douban Plus WebView2 页面。旧 HTML 影视库、旧原生影视库、托盘历史导入和历史 JSON 持久化已移除。

正式 v0.9.0 发布目录、EXE、ZIP 和哈希登记未修改。

## 已删除的数据

应用数据目录 `%LOCALAPPDATA%\DoubanBrowserReminder` 中已删除：

- `douban-history.json`
- `douban-history.before-cdp-repair.json`
- `douban-search-cache.json`

开发备份中的对应历史副本也已删除。v1.0 不再读取或写入这些文件。

保留的数据边界：

- `douban-session.json`：豆瓣登录会话
- `state.json`：PotPlayer、爱奇艺和软件观看状态
- `settings.json`：软件设置
- `cache\`：影片文件名识别的自动绑定/人工确认缓存及其他非历史缓存
- `WebView2\DoubanProfile`：豆瓣登录和浏览器配置

## 已删除的代码和功能

- 旧 `WebAssets\MediaLibrary` 页面及其资源桥接。
- 旧原生影视库窗体、历史导入窗体和相关托盘入口。
- 历史分页读取、`HistoryRead` 队列任务和旧搜索/历史持久化接口。
- 旧缓存型 AI 问题/影评流程及专用测试。
- `MovieInputForm.cs`、`PersonAvatarCache.cs`。
- `AvatarWebResourceService.cs`、`AvatarImageCache.cs`。
- 演职员头像字段、头像 URL 解析、头像补全 Worker、头像资源代理和头像图片自检。

当前 C# 不再读取或保存演职员姓名、岗位、人物 ID、人物链接和头像；演员展示由豆瓣网页/Douban Plus 自身处理。

## 明确保留

- `RecognitionMatcher`、`ForeignMetadataService`、DeepSeek 辅助识别和识别缓存。
- PotPlayer/爱奇艺播放状态、播放结束提醒和“更正影片/重新识别”。
- Douban Plus 个人页、搜索页和真实影片详情页。
- 豆瓣官方评分/状态/短评读取、保存、删除和官方回读确认后端。
- WebView2 登录会话、双 WebView2 恢复、IMDb/外部评分辅助能力。

“识别”是通过视频文件名提取片名/年份，匹配豆瓣/IMDb 影片，并把确认结果缓存下来；它不是历史导入，也不依赖 `douban-history.json`。

## 下一阶段 AI 边界

AI 不再从本地历史或旧缓存取内容。新流程应直接读取当前豆瓣详情页的剧情、短评和长评，提取关键词后生成短评，并复用正式豆瓣写入协调器完成官方页面提交与回读确认。

当前正式写入后端保留，但新的 AI UI 尚未接入。

## 当前源码中的过渡代码

`HtmlMediaLibraryForm.cs` 仍是当前 Douban Plus 宿主类名，且保留正式评价写入协调调用。旧本地 HTML 页面没有启动入口、资源目录或事件订阅；后续 AI 页面接入完成后，可继续拆分或重命名该宿主。

## 验证要求

- Douban Plus-only 源码检查通过。
- 嵌入 JavaScript 检查通过。
- Douban Plus 用户脚本 `node --check` 通过。
- .NET Release 编译无错误。
- `--self-test` 和 `--review-self-test` 通过。
- 发布包不包含 `WebAssets\MediaLibrary`、头像资源服务或旧测试文件。
