# 0.9.0-preview.6 评价体系第一阶段实现报告

## 实现范围

本阶段基于 `0.9.0-preview.4` 完整源码合并，不再修改旧 EXE 的 IL，也不再混用旧程序集与新 WebAssets。

已完成四项基础改造：

1. 评价字段使用明确的 `Keep / Set / Clear` 三态协议。
2. 保存前读取豆瓣官方当前评价，以网页数据计算最终目标。
3. 使用官方可见表单、真实控件和 `form.requestSubmit()` 提交。
4. 只有结算稳定且官方回读逐字段匹配后，才以官方结果覆盖本地缓存。

## 关键行为

### 网页为唯一权威来源

- 官方读取完整：覆盖本地状态、评分和短评，包括明确的空评分、空短评与无评价。
- 官方读取不完整：保留本地缓存，并阻止写入；不把“没有读到”误判成“网页为空”。
- 仅打开一个目标状态的创建表单，不再被视为“网页当前无评价”的证据。

### 三态字段协议

- `Keep`：使用保存前刚读取的官方值。
- `Set`：设置指定评分或短评。
- `Clear`：明确清除评分或短评。
- `null` 不再同时承担“不修改”和“清空”两种含义。

### 提交闭环

流程固定为：

`官方读取 → 计算最终目标 → 填写并复核官方表单 → requestSubmit → 连续稳定结算 → 官方重新读取 → 逐字段验证 → 本地覆盖`

按钮点击、确认框接受、URL 变化或固定等待均不能单独产生成功结果。

### 结果状态

- `confirmed`：官方回读与最终目标完全一致。
- `blocked`：官方数据或表单能力不足，提交前停止。
- `unconfirmed`：请求可能已发生，但结算或回读无法确定；不写目标值。
- `failed`：明确提交失败。

### 删除边界

旧删除链路已知会在确认后提前导航，无法保证服务器请求完成。本阶段将删除从前端和 WebMessage 白名单同时停用，避免继续产生假成功。删除会在第二阶段按独立事务重做。

## 主要变更文件

- `ReviewWriteModels.cs`
- `ReviewTargetResolver.cs`
- `ReviewWriteCoordinator.cs`
- `ReviewSettlementPolicy.cs`
- `ReviewWriteVerifier.cs`
- `OfficialReviewCachePolicy.cs`
- `DoubanOfficialFormScripts.cs`
- `DoubanWebView2Connector.ReviewWriteV2.cs`
- `DoubanWebView2Connector.cs`
- `DoubanConnector.cs`
- `HtmlMediaLibraryForm.cs`
- `WebAssets/MediaLibrary/app.js`
- `WebAssets/MediaLibrary/index.html`
- `SelfTest.cs`

## 尚未完成

- 删除事务重写。
- Windows WebView2 真实账号组合回归。
- 当前打包环境缺少 .NET SDK，因此未在本环境生成或声称已验证 EXE。

## 发布要求

必须从本源码完整编译。禁止将新 WebAssets 复制到 `preview.5` 旧 EXE 旁边，也禁止继续对旧 EXE 做定长 IL 补丁。
