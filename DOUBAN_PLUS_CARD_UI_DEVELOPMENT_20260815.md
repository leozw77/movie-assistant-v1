# Douban Plus 卡片 UI 开发版交接（2026-08-15）

## 基线

本轮 UI 改动从以下已冻结稳定版开始：

- 稳定目录：`artifacts/观影助手-v1.0.1-unified-shell-stable-20260815-020000-win-x64`
- 稳定 EXE SHA256：`8CE6A2B0CCE0BDFBC7B1C8DEBF41BB0C3C81C4A89D6DC8EF96A88B1B6512503A`
- 稳定 ZIP SHA256：`60FACCB641C740B2939EDF888033B386AB5B4EAFC85779349EA56A470F3FD974`

稳定目录没有被本轮修改。正式 v0.9.0 及此前稳定发布物也没有被覆盖。

## 本轮改动

### 统一卡片渲染

`WebAssets/DoubanPlus/douban-card.js/css` 继续作为唯一卡片渲染层，新增或统一了：

- 标题最多两行并固定标题区高度；
- 海报使用 `object-fit: contain`，完整显示，不裁剪；
- 第一行显示年份/国家，影片类型和主要类型使用加粗语义；
- 底部显示年份和媒体类型标签，媒体类型使用金色强调；
- Explore 公开评分显示为 `豆瓣 5.4` 形式；
- 有短评时只显示“短评”按钮，鼠标悬停或键盘聚焦浮出完整内容，不增加卡片常态高度；
- 短评按钮阻止卡片点击冒泡，不会误打开详情。

### 个人页字段隔离

`douban-personal-source-bridge.js` 和旧个人页适配器各自解析个人列表字段，不复用 Explore 的位置假设：

- 类型/题材从整段 `.intro` 的完整分隔片段中识别，即使它位于很后面也不会被当成演员；
- 演员优先使用原生演员选择器，缺少选择器时只使用日期与国家之间的安全片段作为回退；
- 个人卡片不再显示“看过/想看/在看”和状态日期；
- 个人评分显示为 `我的 ★★★`；
- 豆瓣个人列表没有公开评分时保持为空，不伪造 `豆瓣 x.x`。

## UI 开发版产物

- 目录：[观影助手-v1.0.1-unified-shell-stage1-20260815-034451-win-x64](D:/chatgpt/观影助手/开发/v1.0-douban-plus-unified-shell-20260814/artifacts/观影助手-v1.0.1-unified-shell-stage1-20260815-034451-win-x64)
- EXE：[观影助手.exe](D:/chatgpt/观影助手/开发/v1.0-douban-plus-unified-shell-20260814/artifacts/观影助手-v1.0.1-unified-shell-stage1-20260815-034451-win-x64/观影助手.exe)
- ZIP：[观影助手-v1.0.1-unified-shell-stage1-20260815-034451-win-x64.zip](D:/chatgpt/观影助手/开发/v1.0-douban-plus-unified-shell-20260814/artifacts/观影助手-v1.0.1-unified-shell-stage1-20260815-034451-win-x64.zip)
- UI 开发版 EXE SHA256：`8CE6A2B0CCE0BDFBC7B1C8DEBF41BB0C3C81C4A89D6DC8EF96A88B1B6512503A`

本项目的卡片脚本和 CSS 是 EXE 旁边的外置 `WebAssets`，因此本轮 EXE 二进制哈希可能与冻结版相同；测试时请使用完整目录或 ZIP，不要只复制 EXE。

## 已完成的自动验证

- `validate_douban_plus_only.py`：40 个源文件，0 失败；
- `validate_douban_explore.py`：0 失败；
- `validate_douban_shell.py`：0 失败；
- `validate_douban_source_bridge.py`：0 失败；
- JavaScript 语法检查：通过；
- 嵌入脚本检查：通过；
- Review pipeline self-test：18/18；
- Legacy comprehensive self-test：71/71。

构建时 NuGet 漏洞数据源不可访问，只有 `NU1900` 警告；还原、编译、发布均成功。

## 需要用户实测的验收项

使用自己已登录的豆瓣环境启动 UI 开发版，逐项检查：

1. Explore 电影：海报是否完整、标题是否最多两行、底部是否为年份/电影、公开评分是否显示 `豆瓣 x.x`；
2. Explore 电视剧：媒体类型是否为电视剧，卡片布局是否与电影一致；
3. 个人页三状态：是否不显示状态/日期，是否显示年份/国家/加粗类型、演员和 `我的 ★★★`；
4. 个人页的类型位于 intro 很后面时，是否仍进入第一行而不是混入演员；
5. 有短评的个人影片：悬停“短评”是否浮出内容，点击短评是否不进入详情；
6. 组合链路：筛选 → 加载更多 → 详情 → 返回，以及个人页 → 探索页切换是否保持原有行为。

本轮没有用 Computer Use 代替真实 WebView2 验收，也没有把 UI 开发版标记为新的正式稳定版；待以上实测确认后再冻结 UI 版本。
