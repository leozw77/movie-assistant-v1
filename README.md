# 观影助手 / Douban Plus v1.0.0

## 当前边界

本目录是从当前用户确认可用的 Douban 原生搜索稳定源码重新建立的 v1.0.0 个人开发基线。核心业务代码保持不变；本次只重建版本元数据、发布物和 Git 管理起点。

当前产品只使用同一窗口中的 Douban Plus WebView2 页面：个人页、搜索页和真实豆瓣影片详情页由 Douban Plus 重绘或直接显示。个人页 `collect / wish / do` 支持无限滚动和详情返回位置恢复；搜索页保留当前页适配与原生分页。旧版本地 HTML 影视库、托盘历史导入、历史 JSON 持久化和旧版 AI 问答流程已移除。

## 当前保留能力

- 豆瓣登录、个人页、搜索页和真实影片详情页访问。
  - 详情页基础资料；演员、人物链接和图片全部由当前豆瓣页面/Douban Plus 自身处理，C# 不读取或缓存。
- 豆瓣官方评价读取，以及评分、状态、短评的正式写入/删除协调器和官方回读确认链路。
- `DetailWebView`、`WorkerWebView` 与 Douban Plus WebView2 的浏览器进程恢复和登录会话复用。
- IMDb、Metacritic、Rotten Tomatoes 等识别辅助能力，以及 PotPlayer 相关现有功能。
- AI 写评论所需的正式豆瓣写入后端仍保留；AI 读取逻辑将改为直接读取当前豆瓣页面的短评、长评和剧情信息后再重建。

## v1.0.0 暂不包含

- 选电影页 `movie.douban.com/explore` 适配。
- 搜索页无限滚动；当前只使用当前页解析和豆瓣原生分页。
- 新的实时内容 AI UI。

## 明确移除

- 旧版原生/HTML 影视库页面及其资源目录。
- “从豆瓣导入历史”托盘入口、导入窗体、历史分页同步和相关 CDP 回退。
- `douban-history.json`、`douban-history.before-cdp-repair.json`、`douban-search-cache.json` 的读取和写入。
- 依赖历史缓存的旧 AI 问题/影评流程及其专用测试。

## 构建与验证

环境要求：Windows 10/11、.NET 8 SDK、Microsoft Edge WebView2 Evergreen Runtime。

```text
BUILD_PREVIEW.cmd
```

构建脚本会执行 Douban Plus 用户脚本、嵌入脚本、.NET 编译和自检。静态检查、构建通过和进程启动不能替代真实豆瓣页面、登录状态和官方回读验收；本基线继承原稳定源码的用户验收边界。下一阶段 AI 影评问题按 `AI_HANDOFF.md` 独立处理。

## 文档入口

- `DEVELOPMENT_20260812.md`：本轮清理、数据删除和进程处理约定。
- `DOUBAN_PLUS_CLEANUP_HANDOFF_20260812.md`：清理范围与交接记录。
- `AI_HANDOFF.md`：下一阶段 AI 页面内容读取与写评论重建边界。
- `docs/STATUS.md`：当前项目状态与发布边界。
- `docs/CURRENT_ARCHITECTURE.md`：当前 WebView2、识别和写入架构。
- `CHANGELOG.md`：版本更新日志；历史 review 文件只作取证，不作为当前实现说明。
