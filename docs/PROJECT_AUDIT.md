# 项目全面审计

审计日期：2026-08-02

## 项目名称

观影助手。原项目名称为“豆瓣评分提醒器”，曾分为普通版、AI 版和浏览器版；本次合并后只保留一个桌面 EXE 项目。

## 项目用途

监控 PotPlayer 和独立 Chrome/Edge 观影浏览器的播放完成状态，在用户实际观看达到阈值后提醒评分，并生成有资料约束的豆瓣观后短评。

## 当前技术栈

- .NET 8 Windows Forms，C#，Windows x64 自包含单文件发布
- PotPlayer 原生窗口状态读取
- Chrome DevTools / 独立浏览器配置控制
- Wikipedia、Wikidata、IMDb、豆瓣公开数据
- DeepSeek Chat Completions API

## 运行环境

- Windows 10/11、.NET 8 Windows 运行环境（发布包自包含）
- 可选 Chrome、Edge、PotPlayer、qBittorrent
- 需要 DeepSeek API Key 才能使用 AI 身份识别、问题和短评生成

## 当前版本判断

合并后定为 `0.5.0`：最新浏览器版已经包含主要功能和稳定 v4 发布包，但没有历史 Git、统一版本文件和完整自动化/真实端到端验收，因此暂不升至 1.0。

## 三个旧项目判断

- `观影助手`：最早的 PotPlayer + qBittorrent 基础版，功能较少。
- `观影助手`：加入外部资料、DeepSeek、评分问答和短评流程。
- `观影助手`：在 AI 版上加入独立 Chrome/Edge、浏览器 CDP、历史导入、详情和分页 UI，并在 2026-08-02 更新到 v4，功能覆盖前两版，是唯一保留主线。

## 已实现功能

- 托盘程序、设置和提醒。
- PotPlayer 真实播放检测、片名匹配、观看状态和完成阈值。
- 独立 Chrome/Edge 启动、登录态隔离和页面状态读取。
- 浏览器播放与 PotPlayer 记录分离，统一进入评价工作流。
- 豆瓣历史导入、详情读取、分页、搜索、排序和海报按需加载。
- 外部影视资料检索、身份置信度、IMDb 与豆瓣确认。
- 评分、7 题观点问答、多文风和 350 字短评。
- `--self-test`、`--pot-smoke-test`、`--browser-smoke-test` 检查入口。

## 未完成或待验证

- 未建立统一的自动化测试项目和 CI。
- 仍需在真实 Chrome、Edge、PotPlayer、豆瓣验证页环境执行完整回归。
- DeepSeek Key 仍由客户端直接保存/请求，正式发布前应考虑服务端中转。
- 旧命名仍存在于命名空间、兼容数据目录和部分 User-Agent 中，暂不修改以避免数据和兼容性变化。

## 核心文件说明

- `Program.cs`：程序入口、命令行自测入口和单实例互斥。
- `TrayContext.cs`：托盘生命周期、菜单、浏览器启动和提醒协调。
- `BrowserCdpService.cs`：独立 Chrome/Edge 的 CDP 连接与页面状态。
- `PotPlayerClient.cs`：PotPlayer 窗口状态、进度和播放信息读取。
- `ForeignMetadataService.cs`、`AiServices.cs`：外部资料与 AI 调用。
- `DoubanServices.cs`、`DoubanConfirmForm.cs`：豆瓣读取和身份确认。
- `ReviewFlowForm.cs`：评分、问题、回答和短评生成流程。
- `MyWatchHistoryForm.cs`、`DoubanHistoryForm.cs`、`MovieDetailForm.cs`：历史和详情界面。
- `Models.cs`：设置、观看记录、历史记录和数据结构。
- `SelfTest.cs`：无 GUI 自测与结果输出。

## 主要风险与已知问题

- 浏览器 CDP 和视频网站页面结构可能随版本变化。
- 豆瓣可能跳转 `sec.douban.com` 验证页，读取链路需要真实环境确认。
- AI 返回内容虽有校验，但外部资料错配仍需用户确认。
- 历史数据、观看状态和浏览器配置依赖 `%LOCALAPPDATA%`，迁移前必须备份。
- 直接删除旧发布包后，旧版 EXE 不再作为项目资产维护；当前只保留最新 v4 包。

## 合并与清理范围

- 保留：浏览器版 v4 源码、最新 v4 发布包、必要 NuGet 配置和源码内 README 信息。
- 移除：普通版、AI 版、浏览器版旧发布目录、旧 zip/rar 压缩包、`bin/obj` 构建产物和本地 NuGet 缓存。
- 未删除：用户运行时 `%LOCALAPPDATA%\DoubanBrowserReminder` 数据；该目录不在项目目录中，且为兼容性数据。

