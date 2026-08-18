# BuildFix12 R11 收尾说明

## Scope

R11 是文档/发布候选收尾版。**业务代码基线保持 BuildFix12 R10，不主动改变已经实机验证的功能。**

## Changes

1. README 更新为当前 R11 完整能力，不再以 R8 为主体。
2. `docs/STATUS.md` 从旧 BuildFix12 R4 状态更新到当前 Release Candidate 状态。
3. `AI_HANDOFF.md` 重写为唯一主交接文档。
4. 新增 `docs/DEVELOPMENT_HISTORY.md`，逐项记录 BuildFix5～BuildFix12 R11。
5. 新增 `docs/CURRENT_ARCHITECTURE.md`，冻结双 WebView2、Worker、评价、删除、缓存、同步和崩溃恢复不变量。
6. 更新 BuildFix12 实施报告、验收清单和包清单。
7. 新增 `review/RELEASE_CANDIDATE_SUMMARY.md`。
8. CHANGELOG 补齐此前缺失的 BuildFix12 R1/R2/R5，并增加 R11。
9. Build-Preview.ps1 改为 R11 banner/BUILD_INFO，并把当前 Markdown 文档复制到发布目录。
10. 新增 `tests/validate_buildfix12_r11.py` 文档一致性门禁。

## Runtime behavior

No intended behavior change from R10.

## Current user conclusion

当前主线需求均已实现，进入收尾阶段。正式 v0.9.0 登记应作为下一独立发布动作执行。
