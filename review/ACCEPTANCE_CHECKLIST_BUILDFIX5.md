# BuildFix5 完整源码验收清单

## 构建

1. 将完整源码 ZIP 解压到新的空目录，不要覆盖旧源码。
2. 双击根目录 `BUILD_PREVIEW.cmd`。
3. 确认 `dotnet restore`、Release build 和 win-x64 single-file publish 成功。
4. 确认评价专项自检全部通过。
5. 记录旧综合自检结果；该项保留为诊断，不覆盖评价专项硬门槛。
6. 确认 `artifacts\观影助手-v0.9.0-preview.6-win-x64\` 内生成 EXE、`BUILD_INFO.txt`、两份自检结果和 SHA-256。

## 性能与普通评价

1. 第一次打开详情页，记录详情主体出现时间及官方评价刷新时间。
2. 再次打开同一影片，日志应优先出现 `Douban review performance reuse`。
3. 测试无评价 → 想看、在看、看过。
4. 测试想看、在看、看过互相切换。
5. 测试设置评分、修改评分、清除评分；清除评分应识别豆瓣 `img#star0`。
6. 测试设置短评、修改短评、清空短评。
7. 只修改状态时，网页最新评分和短评必须按 Keep 保留。
8. 网页端修改评价后重新进入软件，网页值必须覆盖本地缓存。
9. 每次保存只有在官方回读逐字段一致后才能提示成功并更新本地。
10. 无法确认时必须返回 `Uncertain`，不得写入目标评价。

## 删除与诊断

1. 删除功能继续保持禁用，不允许回退到旧 `node.click()` 删除路径。
2. 复现问题后双击 `COLLECT_DIAGNOSTICS.cmd`。
3. 确认源码根目录生成 `DoubanReview-Diagnostics-日期-时间.zip`。
4. 诊断 ZIP 应包含已有的构建日志、自检结果、`BUILD_INFO.txt`、运行日志及最近崩溃日志。
5. 诊断收集器不得复制 WebView2 Profile、Cookie、密码或浏览器存储。
