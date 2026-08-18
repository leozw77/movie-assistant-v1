# 2026-08-19：API 迁移 Phase 1 交接（个人页 Frodo）

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
