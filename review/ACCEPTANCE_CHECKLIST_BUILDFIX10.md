# BuildFix10 Windows 验收清单

## 构建
- [ ] 在全新目录解压源码，运行 `BUILD_PREVIEW.cmd`。
- [ ] `BUILD_INFO.txt` 显示 BuildFix10，专项评价自检全部通过。
- [ ] EXE 可启动，WebView2 登录状态正常。

## 详情首屏
- [ ] 打开有本地缓存的影片时立即显示缓存内容。
- [ ] 日志顺序为 `detailMetadata` 完成/失败后再进入 `detailReview`。
- [ ] 基础资料 24 小时内命中缓存时，不发生影片页导航；官方评价仍重新读取。
- [ ] 首屏只显示豆瓣首页实际展示的最多 6 位演职员，顺序与网页一致。
- [ ] 任一头像缺失只显示默认头像，不自动访问 `/celebrities` 或人物主页。

## 全部演职员
- [ ] 未点击“全部演职员”时日志中不访问 `/celebrities`。
- [ ] 点击后先显示“正在读取全部演职员……”。
- [ ] 读取成功后显示完整名单；再次打开在 7 天内立即使用缓存。
- [ ] 官方返回空名单时显示“豆瓣返回为空”，不会每次重复读取。
- [ ] 读取失败时保留首页 6 位并显示可重试提示，详情页不整体报错。

## 评价读取与保存
- [ ] 已有评价但编辑表单初始为空时，日志出现初始化重采样，空值不覆盖本地评分。
- [ ] 状态变化保存后，本地 `MarkedDate` 与豆瓣官方新日期一致。
- [ ] 看过状态不变，仅修改评分或短评后，本地日期保持豆瓣原日期。
- [ ] 完全相同的目标显示“无需重复保存”，`Submitted=false`、`NoChange=true`。
- [ ] 真实提交并回读匹配后才显示“豆瓣官方已确认保存成功”。
- [ ] SubjectId/URL 不一致时操作被阻止并写入诊断日志。
- [ ] 删除按钮继续禁用。

## 日志与诊断
- [ ] `%LOCALAPPDATA%\\DoubanBrowserReminder\\logs\\review-transactions.jsonl` 每次保存新增一行。
- [ ] 事务行包含标题、ID、保存前状态/评分/日期、目标、提交状态、官方回读、耗时。
- [ ] 诊断包包含 `review-transactions.jsonl` 和 `COLLECTOR_INFO.txt`。
- [ ] `COLLECTOR_INFO.txt` 包含 CollectorGeneratedAt、LogFirstEntryAt、LogLastEntryAt、CurrentProcessId、CurrentSessionVerifiedAt。
