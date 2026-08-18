# Douban Plus Unified Shell Stage 1 — Explore 卡片异步 DOM 修复

日期：2026-08-14

## 现象

统一 Shell 能显示，但 Explore 卡片为空，Shell 显示“豆瓣没有返回电影卡片”。

## 根因

隐藏 Source WebView 的 `NavigationCompleted` 只表示文档导航完成，不表示豆瓣 Explore 的原生电影列表已经异步写入 DOM。此前在导航完成后立即执行 Source bridge，读取到的是：

- `readyState=complete`；
- `.subject-list-list` 数量为 0；
- 电影候选锚点数量为 0。

因此 JSON 桥接链路本身是通的，但读取时机过早。

## 本次修复

- Source bridge 继续只读取豆瓣真实 Explore DOM，不生成 Shell、不恢复旧 Explore 自定义代理；
- C# Source 读取改为条件轮询：每 150ms 重新读取真实 DOM，直到出现至少一个有效电影卡片，最长等待 12 秒；
- 只有读到真实电影卡片才结束等待；超时仍返回最后一次 DOM 诊断，不把固定延时当作成功条件；
- 增加 `title/htmlLength/exploreMenuCount/listMainCount` 诊断字段，便于区分异步未完成和豆瓣页面无列表；
- 详情页现有双 WebView 保持不变。

## 本轮静态/构建验证

- Source bridge Python validation：0 failures；
- Source bridge Node.js syntax：通过；
- Release `dotnet build -r win-x64 --no-restore`：0 warnings / 0 errors。

## 用户自测

1. 启动本交付包；
2. 进入“探索电影”；
3. 等待最多约 12 秒；
4. 预期 Shell 卡片区出现豆瓣电影卡片，状态显示“已从豆瓣真实页面读取 N 部电影”；
5. 点击任意卡片，确认详情页仍在外部详情 WebView 打开；
6. 返回后确认 Shell 的 Explore 页面仍保持统一层级，不出现豆瓣原始 Explore 页面闪现。

如果仍为空，请把 `%LOCALAPPDATA%\DoubanBrowserReminder\logs\diagnostic.log` 中本次启动对应的以下行发回：

`Unified Shell Source DOM JSON forwarded`

其中的 `Dom`、`Items`、`WaitMs`、`Attempts` 和 `TimedOut` 可直接判断是豆瓣未返回 DOM，还是选择器需要适配。
