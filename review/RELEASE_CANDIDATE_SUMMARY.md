# BuildFix12 R11 发布候选收尾摘要

## 目的

R11 是文档/交接收尾版，不改变 R10 的运行逻辑。用户已确认当前主线需求均已实现，因此本轮重点是消除“源码已经到 R10，但文档仍停在 R4/R8”的状态漂移。

## 本轮收尾内容

- README 更新到 R11 当前能力。
- `docs/STATUS.md` 从旧 R4 状态更新为当前发布候选状态。
- `AI_HANDOFF.md` 重写为唯一主交接文档。
- 新增 `docs/DEVELOPMENT_HISTORY.md`，逐项记录 BuildFix5～BuildFix12 R11。
- 新增 `docs/CURRENT_ARCHITECTURE.md`，冻结当前双 WebView2/Worker/删除/同步不变量。
- BuildFix12 实施报告与验收清单更新到 R11。
- 包清单更新到 R11。
- CHANGELOG 补齐 BuildFix12 R1/R2/R5/R11 等之前缺失的条目。
- Build-Preview.ps1 更新 R11 banner/BUILD_INFO，并将当前 Markdown 文档复制到发布目录。
- 增加 R11 文档一致性门禁。

## 当前业务基线

业务代码仍为 R10：

- ReviewWrite v2 Keep/Set/Clear；
- Detail/Worker 双 WebView2；
- 首页 6 位 + FullCast + 头像补全；
- do PersonalDoList 两阶段真实鼠标删除；
- tombstone；
- BrowserProcessExited 真重建；
- HistoryRead 去重/低优先级；
- 启动/切标签自动历史同步。

## 用户实机状态

截至 R11 收尾时，用户明确确认当前要求均已实现；此前已明确反馈：

- 评价修改正常；
- 首页 6 位演员正确；
- do 两阶段删除测试正常；
- 删除性能已经满意；
- 当前要求进入收尾阶段。

## 正式版登记前建议

R11 仍保留 `0.9.0-preview.6` 版本号，避免直接冒充已登记的正式 `v0.9.0`。正式登记建议单独执行：

1. Windows 完整构建 R11。
2. 最小冒烟：详情、保存、do 删除、自动同步。
3. 记录 EXE 和 ZIP SHA-256。
4. 新建不可覆盖发布目录。
5. 更新稳定版登记文件/标签。

当前长期稳定版 `v0.8.9` 在上述步骤前继续冻结。


## R11 静态回归

- 文档一致性：22/22。
- 自动同步：14/14。
- R9 编译风险：4/4。
- R8 恢复/性能：18/18。
- 删除专项：51/51。
- BuildFix11：34/34。
- 综合 review/source：98/98。
- 评价协议：6/6。
- 嵌入 JavaScript / app.js / launcher 格式：通过。
