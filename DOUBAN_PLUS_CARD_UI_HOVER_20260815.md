# Douban Plus 卡片悬停 UI 开发版交接（2026-08-15）

> 历史实验记录：本文件描述的整卡悬停层不属于当前稳定版，也不是当前卡片实现的验收标准。当前实现和基线以父目录 `DEVELOPMENT_BASELINE.json`、`DEVELOPMENT_DIRECTORY_INDEX.md` 及配套源码中的实际 `douban-card.js/css` 为准。

## 本轮交付方式

本轮只交付单 EXE，不要求解压或携带 `WebAssets` 目录。

- EXE：[观影助手-v1.0.1-unified-shell-ui-20260815-040744.exe](D:/chatgpt/观影助手/开发/v1.0-douban-plus-unified-shell-20260814/artifacts/观影助手-v1.0.1-unified-shell-ui-20260815-040744.exe)
- SHA256：`398009D2E5D33F66590DC55D8D157A7F18C6956D7F9AA4516D82DCB4BD390420`
- 文件大小：`4,949,250` bytes

Douban Plus 的脚本和 CSS 已嵌入 EXE。单独复制 EXE 到无 `WebAssets` 目录的临时目录后，`--self-test` 仍通过资源读取检查。

## 本轮 UI 改动

- 合拍国家：卡片默认只显示前两个国家；完整国家进入悬停信息层；
- 类型：取消正文类型的强加粗，底部“电影/电视剧”标签改为低强调样式；
- 默认卡片：保留海报、两行标题、年份/前两个国家/前两个类型、演员预览、评分；
- 演员：Source 不再静默截断为 8 人，卡片显示前 6 位并标记“等 N 人”，悬停层显示完整列表；
- 悬停层：显示完整国家、完整类型、完整演员/演职员和短评；
- 键盘：卡片获得键盘焦点时也显示悬停层；
- 海报继续使用完整比例显示，不裁剪；
- 个人页继续隐藏状态和日期，评分仍显示为“我的 ★★★”。

## 自动验证

- Source/Explore/Shell 验证：0 失败；
- JavaScript 语法和嵌入脚本验证：通过；
- Review pipeline：18/18；
- Legacy comprehensive self-test：72/72；
- 单 EXE 无外置 `WebAssets` 运行 `--self-test`：72/72，包含“Douban Plus UI 资源可从单 EXE 读取”。

## 用户实测重点

1. 电影 Explore：悬停卡片后是否浮出完整国家、类型、演员和短评；
2. 合拍片：默认国家是否只显示前两个；
3. 演员很多的影片：卡片是否显示“等 N 人”，悬停层是否能看到完整演员；
4. 个人页：类型、演员、我的评分和短评是否与 Explore 使用同一层级；
5. 电影/电视剧切换、筛选、加载更多、详情返回是否保持原有行为。

本 EXE 是 UI 开发版，稳定基线仍是 `观影助手-v1.0.1-unified-shell-stable-20260815-020000-win-x64`，待真实登录态 WebView2 实测确认后再决定是否冻结为下一稳定版。
