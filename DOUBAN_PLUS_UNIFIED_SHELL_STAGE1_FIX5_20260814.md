# Douban Plus Unified Shell Stage 1 — 海报与 Explore 详情点击

日期：2026-08-14

## 本批实现

- Source 仍从豆瓣真实 Explore DOM 读取电影和海报 URL；
- 宿主使用豆瓣 Referer 下载海报，并转为 data URI 交给统一 Shell，避免 `about:blank` 页面直接请求豆瓣图床被拒；
- Explore 统一卡片继续通过 `doubanShellOpenDetail` 消息进入现有详情双 WebView；
- 详情来源仍记录为当前 Shell 列表，返回不重新创建列表页面；
- 未接入个人影片和 Explore 筛选，本批不扩大到下一阶段。

## 验证

- Shell validation：0 failures；
- Shell JavaScript Node 语法：通过；
- Release dotnet build：0 warnings / 0 errors。

## 手动验收

1. 关闭旧版本 EXE，启动本包；
2. 进入“探索电影”，等待卡片出现；
3. 确认卡片显示真实海报，不再显示“暂无海报”；
4. 点击卡片主体，确认进入详情；
5. 点击详情返回，确认回到统一 Explore Shell，卡片仍在；
6. 个人影片入口和 Explore 筛选仍属于后续批次，当前不作为本包通过条件。

若海报仍为空，检查日志中的 `Unified Shell poster materialization failed`；若点击无反应，检查 `Unified Shell detail requested`。
