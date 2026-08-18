# BuildFix12 R11 最终实施报告

## 总结

BuildFix12 从“删除功能重做”扩展到删除可靠性、WebView2 崩溃恢复、删除性能和自动历史同步恢复。R11 本身不改业务逻辑，仅完成发布前文档与交接冻结。

## 最终业务能力

### 删除路由

```text
do      -> PersonalDoList
wish    -> SubjectDetail
collect -> SubjectDetail
```

`do` 通过个人 `/people/{ProfileId}/do` 页面、SubjectId 精确卡片匹配和 Chromium 真实鼠标输入删除。针对豆瓣实测的“评分+短评时一次删除可能只完成第一阶段”行为，一次用户事务允许最多两次官方真实点击。

最终成功证据：

- fresh `/do` 连续稳定确认 SubjectId 消失；
- 影片详情一次导航后的两个稳定轻量样本确认 Status/Rating/Comment/MarkedDate 均空；
- 然后才写本地 tombstone。

### 崩溃恢复

`BrowserProcessExited` 时：

- Worker 队列 pause/fence；
- 旧导航立刻取消；
- 失效 Detail/Worker WebView2 被真正销毁；
- 复用同一 DoubanProfile 创建新控件；
- 恢复后重读当前影片并恢复当前历史同步。

### 自动历史同步

R10 恢复：

- 启动缓存先显示后自动同步当前标签；
- 切换 collect/wish/do 自动同步；
- 5 分钟节流；
- 前端/后端双层去重；
- HistoryRead 低优先级可抢占；
- 登录/浏览器恢复后自动继续；
- 手动“立即同步豆瓣”保留为强制刷新。

## R1～R10 关键变更索引

详细逐轮说明见 `docs/DEVELOPMENT_HISTORY.md`：

- R1：删除 v2 事务和 tombstone。
- R2：CS0173 编译修复。
- R3：删除后详情/列表传播诊断。
- R4：do 改 PersonalDoList。
- R5：CS0136 编译修复。
- R6：JS click 改 Chromium 真实鼠标。
- R7：do 两阶段自动第二击。
- R8：BrowserProcessExited 真重建 + 删除提速。
- R9：Worker finally CS0157 编译修复。
- R10：恢复自动历史同步。
- R11：文档与发布候选收尾。

## 保留不变量

- 普通 ReviewWrite v2 不重写。
- DetailWebView 不为删除/历史/FullCast 跳走。
- failed/unconfirmed 不清本地。
- 不 fetch 模拟写入、不直接 POST、不导出 Cookie。
- `CastParserVersion=2`、`FullCastParserVersion=3`。
- `stable-v0.8.9` 不覆盖。
