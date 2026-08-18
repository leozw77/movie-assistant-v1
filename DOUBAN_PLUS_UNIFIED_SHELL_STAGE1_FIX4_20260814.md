# Douban Plus Unified Shell Stage 1 — Shell 消息边界修复

日期：2026-08-14

## 现象

Source 日志已经显示从豆瓣真实 Explore DOM 读取到 20 部电影，但可见 Shell 仍停留在“正在读取电影卡片…”。

## 根因

数据链路的前半段正常：

`豆瓣 Explore DOM → Source bridge JSON → C#`

问题位于最后一段：

`C# PostWebMessageAsJson → Shell WebView message event`

WebView2 入站消息在不同运行状态下可能以对象或 JSON 字符串形式出现在 `event.data`。Shell 原逻辑只按对象读取 `message.type`，如果收到字符串就直接忽略，导致一直保留初始 loading。

## 修复

- Shell 消息处理同时支持对象和 JSON 字符串；
- Shell 收到并应用数据后回传 `doubanShellDataApplied`；
- 宿主记录 `data posted` 与 `data applied` 两个边界日志；
- 不改变 Source DOM 读取、不改变统一卡片渲染、不改变详情双 WebView。

## 手动测试

1. 启动本交付包并进入“探索电影”；
2. 等待豆瓣页面加载完成；
3. 预期不再停留在“正在读取电影卡片…”，出现电影卡片并显示“已从豆瓣真实页面读取 N 部电影”；
4. 点击卡片进入详情，再返回，确认仍回到统一 Shell 层级；
5. 若仍异常，查看日志中是否同时存在：

   - `Unified Shell data posted`
   - `Unified Shell data applied`

若只有第一行，说明仍是 WebView2 消息投递问题；若两行都有但界面不变，则是 Shell DOM 渲染问题。
