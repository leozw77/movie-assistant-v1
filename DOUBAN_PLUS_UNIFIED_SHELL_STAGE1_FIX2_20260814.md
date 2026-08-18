# Douban Plus 统一 Shell 第一阶段修复 2

## 根因

用户复测仍停留在“正在读取电影卡片”。最新日志显示：

- Source 导航成功；
- `BridgeProbe.present=true`；
- `ReadCall=null`；
- 宿主没有收到 `doubanSourceResult`。

因此问题不在 DOM bridge 注入，而在隐藏 Source WebView 的 `chrome.webview.postMessage` 反向消息回传链路。

## 修改

- `readPage()` 改为同步返回结构化 JSON；
- C# 在 Source 导航完成后直接执行 `readPage()` 并接收 JSON；
- C# 再把 JSON 转发给可见 Shell；
- DOM 选择器扩展为 `.subject-list-list`、`.subject-list-main` 和 `.subject-list` 下的链接；
- JSON 增加 `readyState`、候选链接数量、列表节点数量和正文长度诊断字段。

## 仍保持的边界

- 数据仍来自真实豆瓣 Explore DOM；
- Source WebView 仍隐藏；
- Shell 仍是唯一可见列表界面；
- 未加入筛选、电视剧和无限滚动。
