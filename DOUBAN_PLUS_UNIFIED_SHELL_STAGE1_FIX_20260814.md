# Douban Plus 统一 Shell 第一阶段问题修复

日期：2026-08-14

## 用户实测现象

统一 Shell 能够显示，但一直停留在“正在读取电影卡片…”，没有卡片。

## 日志证据

- Shell ready：已收到；
- Source NavigationCompleted：成功；
- Source read requested：已触发；
- `doubanSourceReady`：未收到；
- `doubanSourceResult`：未收到。

## 修复内容

1. Shell ready 不再在 Source 导航尚未完成时触发读取；
2. Source 导航完成后记录 `NavigationCompleted` 状态，再开始 DOM 读取；
3. 读取前探测 `window.QbDoubanSourceBridge.readPage` 是否存在；
4. 如果 WebView2 文档创建注入没有生效，由宿主在当前真实 Source 文档中补注入同一 bridge，再执行 `readPage`；
5. 增加 bridge 探针和读取调用结果日志，后续可以明确区分“未注入、未调用、DOM 无卡片、消息未回传”。

## 用户复测

重新运行本包的 `RUN_UNIFIED_SHELL_PREVIEW.cmd`，确认：

- “正在读取电影卡片…”结束；
- Shell 显示电影卡片；
- Source 不抢占可见区域；
- 点击卡片进入详情并返回。
