# 0.9.0-preview.6 BuildFix12 R11 完整源码包清单

## 当前包定位

- 业务基线：BuildFix12 R10。
- R11：发布前文档、交接、构建元数据收尾，不改变业务逻辑。
- 长期稳定版仍为 v0.8.9，不覆盖。

## 核心源码能力

- BuildFix11 R3 双 WebView2、演职员、头像和普通评价保存基线。
- BuildFix12 do PersonalDoList 两阶段删除。
- tombstone。
- BrowserProcessExited 真重建。
- Worker 队列熔断/去重/优先级。
- faster do delete settlement。
- 自动历史同步。

## 当前 Markdown 文档

- `README.md`
- `CHANGELOG.md`
- `AI_HANDOFF.md`
- `docs/STATUS.md`
- `docs/DEVELOPMENT_HISTORY.md`
- `docs/CURRENT_ARCHITECTURE.md`
- `review/IMPLEMENTATION_REPORT_BUILDFIX12.md`
- `review/ACCEPTANCE_CHECKLIST_BUILDFIX12.md`
- `review/RELEASE_CANDIDATE_SUMMARY.md`
- `review/PACKAGE_MANIFEST.md`
- `BUILD_FIX12_R11_NOTES.md`

旧 txt/诊断文件保留为历史证据。

## 构建与诊断

- `BUILD_PREVIEW.cmd`
- `COLLECT_DIAGNOSTICS.cmd`
- Windows build/publish/review self-test/legacy diagnostics/SHA-256/ZIP 流程。

## 安全边界

- 不直接 POST/fetch 写入豆瓣。
- 不导出 Cookie/Profile。
- failed/unconfirmed 不提前改本地。
- do 不回退影片详情删除。
- 评价保存/删除优先于后台历史、FullCast 和头像。
