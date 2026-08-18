# BuildFix9 Windows 验收清单

## A. 构建

- [ ] 在新的空目录完整解压源码 ZIP。
- [ ] 双击 `BUILD_PREVIEW.cmd`，确认 restore、build、publish 全部成功。
- [ ] `review-self-test-result.txt` 全部通过。
- [ ] 发布目录 `BUILD_INFO.txt` 明确显示 BuildFix9，而不是 BuildFix6/7/8。

## B. 详情首屏

- [ ] 点击已有缓存的影片后，详情弹窗立即出现，不再白等数秒。
- [ ] 初始可见标题、海报、缓存简介/演员和上次确认评价。
- [ ] 显示“影片资料正在更新”和“我的评价正在同步”。
- [ ] 影片资料完成后只局部更新资料区域。
- [ ] 官方评价完成后只更新评价区域并解锁“保存到豆瓣”。
- [ ] 日志中同一详情的 `detailReview` 应优先进入隐藏 WebView2 队列；较慢的元数据刷新不阻塞评价区变为可保存。
- [ ] 官方回读为空评分时，随后完成的元数据刷新不得把旧评分重新显示出来。

## C. 完整演职员

- [ ] 普通打开详情时，日志不出现 `Full cast START`。
- [ ] 只有点击“查看全部演职员”后才读取 `/celebrities`。
- [ ] 已缓存完整演职员时再次点击不重复访问豆瓣。

## D. 保存前快照复用

- [ ] 官方评价刚同步完成后 10 秒内立即保存，日志出现 `fresh snapshot reused`。
- [ ] 此时仍执行提交结算与保存后官方回读。
- [ ] 等待超过 10 秒再保存，日志出现快照拒绝或正常保存前官方读取。
- [ ] 在此期间让隐藏 WebView2 导航到其他条目，再保存时不得复用旧快照。

## E. 前台优先与登录检查

- [ ] 详情打开期间不会每 5 秒刷新登录状态。
- [ ] 保存期间日志可出现 `session cookie check skipped; Reason=foreground-navigation-busy`，但不得阻塞保存。
- [ ] 关闭详情且空闲约 60 秒后，登录状态仍能更新。

## F. 权威性回归

- [ ] 状态、评分、短评普通修改：官网回读一致后才成功。
- [ ] Keep / Set / Clear 三态行为保持正确。
- [ ] 想看清除评分保持 BuildFix8 服务器确认式事务。
- [ ] 在看/看过清分保持两段事务。
- [ ] 官方读取失败时保存按钮不解锁，且本地不写目标值。
- [ ] 保存后回读不一致时显示 Uncertain/Failed，不伪造成功。
- [ ] 删除按钮仍禁用。

## G. 性能日志建议

比较同一条目 BuildFix8 与 BuildFix9：

- 点击至详情弹窗首次可见时间；
- `HTML detail metadata refreshed` 耗时；
- `HTML detail review refreshed` 耗时；
- 保存是否出现 `fresh snapshot reused`；
- 保存开始至 `HTML review v2 final result` 时间；
- `HTML bridge operation start/completed` 能否明确指出触发的是 `detailReview`、`detailMetadata`、`libraryPage` 还是保存；
- 是否仍有非用户操作触发的历史页访问。
