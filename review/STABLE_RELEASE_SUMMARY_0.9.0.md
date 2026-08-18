# v0.9.0 稳定版发布摘要

## 发布对象

- 版本：`v0.9.0`
- 基线：BuildFix12 R11，业务代码保持 BuildFix12 R10
- 发布类型：Windows x64、framework-dependent、single-file、`SelfContained=false`
- 历史稳定版 `v0.8.9` / `stable-v0.8.9` 保留，不覆盖原目录和哈希登记

## 本次登记的功能

R11 本身不增加运行时功能；稳定版登记包含此前已验证的主线能力：

- 官方 DOM 评价读取、保存、结算和逐字段回读；Keep/Set/Clear 三态评分/短评协议。
- Detail/Worker 双 WebView2、共享 DoubanProfile、Worker 单消费者优先级队列。
- 首页六位演职员、FullCast 稳定采样、头像缓存与后台补图。
- `wish / do / collect` 路由；`do` PersonalDoList 两阶段真实鼠标删除与 tombstone。
- BrowserProcessExited 后双 WebView2 真重建、Worker 熔断恢复和当前页/历史标签恢复。
- 缓存优先的自动历史同步、五分钟节流、前后端去重和手动立即同步入口。

## 当前未实现或非主线

以下不是 v0.9.0 核心缺陷：

- API Key 安全与数据迁移方案仍是长期事项。
- 豆瓣 Tags 主动写入未实现；当前只保留既有标签数据。
- PotPlayer 等外围播放链路不属于本版本新增范围，修改后需独立回归。

## 发布证据

静态门禁、JavaScript 协议/嵌入脚本检查和 Windows .NET Release build/publish 由构建脚本写入 `BUILD_INFO.txt`、`PACKAGE_SHA256SUMS.txt` 和构建日志。真实登录豆瓣的最终页面冒烟应在实际使用账号上完成并单独留存记录。
