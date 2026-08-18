# BuildFix6 完整源码验收清单

## 构建

- [ ] 双击 `BUILD_PREVIEW.cmd`
- [ ] restore 成功
- [ ] Release build 0 警告、0 错误
- [ ] win-x64 single-file publish 成功
- [ ] 评价专项自检全部通过
- [ ] 旧综合自检结果已生成
- [ ] 发布目录、ZIP、BUILD_INFO 与 SHA-256 已生成

## 详情性能

- [ ] 启动后影视库立即显示本地缓存，不自动占用隐藏 WebView2 同步历史
- [ ] 点击详情时日志不再出现紧邻的 `Douban status capability form`
- [ ] 详情仍执行一次 `Douban review v2 official-read`
- [ ] 网页端修改评价后重新打开详情，网页值覆盖本地
- [ ] 点击“刷新”仍可主动同步当前豆瓣历史页

## 普通评价

- [ ] 无评价 → 想看
- [ ] 无评价 → 在看
- [ ] 无评价 → 看过
- [ ] 想看/在看/看过互相切换
- [ ] 设置评分
- [ ] 修改评分
- [ ] 清除评分
- [ ] 清除评分日志显示 `ratingClearMethod=wish-roundtrip` 或 `explicit-control`
- [ ] 设置、修改、清空短评
- [ ] 只改状态保留网页最新评分和短评
- [ ] 所有成功均经过结算和官方回读
- [ ] 失败或不确定时本地不写目标值

## 删除

- [ ] 软件内删除仍禁用
- [ ] 未实现服务器回读确认前不写墓碑
