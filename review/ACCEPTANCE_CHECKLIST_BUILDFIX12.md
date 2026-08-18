# BuildFix12 R11 发布候选验收说明

## 已完成实现

- [x] 普通 wish/do/collect 评价保存主线保留 ReviewWrite v2 官方回读确认。
- [x] Detail/Worker 双 WebView2 分工。
- [x] 首页最多 6 位演职员按原始卡片读取。
- [x] FullCast 逐卡解析、稳定采样和后台头像补全。
- [x] do 删除固定 PersonalDoList 路由。
- [x] do 列表 SubjectId 精确匹配与真实鼠标输入。
- [x] do 两阶段删除自动第二击，最多两击。
- [x] 删除 tombstone 与重新添加保护。
- [x] BrowserProcessExited 双 WebView2 真重建。
- [x] Worker 死亡导航熔断、HistoryRead 去重/低优先级。
- [x] 删除性能优化。
- [x] 自动历史同步恢复，5 分钟节流，手动强制刷新保留。
- [x] R11 当前 Markdown 状态/架构/历史/交接统一。

## 用户实机确认

- [x] 评价状态修改正常。
- [x] 首页六位演员正确。
- [x] do 两阶段删除测试正常。
- [x] 删除速度达到满意水平。
- [x] 本轮用户确认当前要求均已实现并进入收尾阶段。

## 正式 v0.9.0 登记前最小冒烟

以下属于“正式版本登记流程”，不是 R11 未实现功能：

- [ ] 在最终 R11 包上执行一次 Windows `BUILD_PREVIEW.cmd`。
- [ ] 打开一部影片，确认详情和官方评价读取。
- [ ] 修改一次评价并确认豆瓣网页一致。
- [ ] 删除一条 do 记录并确认 `/do` 和详情都已清空。
- [ ] 启动/切标签确认自动同步。
- [ ] 保存最终 EXE/ZIP SHA-256。
- [ ] 单独登记 v0.9.0，不覆盖 stable-v0.8.9。
