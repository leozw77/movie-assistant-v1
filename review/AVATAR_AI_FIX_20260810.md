# 官方演员头像与 AI 问题修复记录（2026-08-10）

## 范围

仅修改独立开发目录 `D:\chatgpt\观影助手-0.9-recognition-fix`。未修改正式 v0.9 BuildFix12 R11 发布目录、ZIP、EXE 或哈希登记。

## 根因与修复

### 演员头像

- 首页卡片在懒加载尚未完成时可能返回空头像，原安全收紧版又没有官方第二来源。
- 旧人物主页和宽范围 `.avatar` 搜索会混入评论用户、关注者、侧栏图片或 `/icon/up...`，不能恢复。
- 新逻辑只在 WorkerWebView 读取同一 `SubjectId` 的 `/subject/{id}/celebrities`，限定官方演员容器和当前 `li.celebrity` 卡片。
- 演员卡分批滚入视口；完成全部卡片水合后，卡片和头像签名连续三次一致才返回。
- 只按 `CelebrityId` 回填首页演员。不同人物共享同一 URL 时隔离，不按姓名猜测。
- URL、Content-Type、单文件大小和 JPEG/PNG/GIF/WebP 文件签名均受检查。
- `OfficialCastAvatarRead` 与显式 `FullCastRead` 按 SubjectId 合并；官方评价读可抢占，评价完成后重新调度。

### AI 问题

- Wikidata 搜索结果不再直接信任；逐候选读取实体 claims，只有实体自身 `P345` 与请求 IMDb 完全相同才使用。
- 同 IMDb 且年份一致时，允许同一影片的地区译名；IMDb 或年份冲突继续阻止。
- 外部资料缓存从 `knowledge-v2-` 升级到 `knowledge-v3-`。
- 保留 10 道内容题、恰好 3 道必答多选和独立可空补充说明。
- 问题事实前提继续要求有效 evidenceRefs/evidenceQuotes；纯观点选项不再错误要求逐字引用。
- 首次出现 `question-evidence` 时直接使用官方字段本地安全十题，不做第二次联网修复；其他结构错误最多修复一次，仍失败也转本地安全十题。

### 影视库同步后筛选为空

- 日志确认豆瓣“看过”第一页正常返回 15 条，但前端同步后把筛选数据源从完整本地镜像切换成了当前在线页；因此旧影片虽然仍在 1051 条本地记录中，输入片名也会显示空白。
- 真实本地镜像中“天才”可匹配《天才游戏》和《天才枪手》，数据没有丢失。
- 现在只要筛选框非空，就始终在完整 `cacheTabs` 镜像中搜索；在线当前页仍用于无筛选时的远端分页，并在每次成功读取后合并进本地镜像。

## 修改文件

- `AiServices.cs`
- `AvatarWebResourceService.cs`
- `BrowserCdpService.cs`
- `DoubanWebView2Connector.cs`
- `HtmlMediaLibraryForm.cs`
- `WorkerJobQueue.cs`
- `DetailCachePolicy.cs`
- `SelfTest.cs`
- `tests/validate_ai_review.py`
- `tests/validate_review_pipeline.py`
- `tests/validate_buildfix11.py`
- `tests/validate_buildfix12_r10.py`
- `tests/fixtures/full-cast-delayed-stability.json`
- `webassets/MediaLibrary/app.js`
- `AI_HANDOFF.md`
- `CHANGELOG.md`
- `docs/CURRENT_ARCHITECTURE.md`

## 已完成验证

- Release/win-x64 编译：0 错误；1 个 NU1900（当前环境无法读取 NuGet 漏洞源）。
- `--self-test`：106/106。
- `--review-self-test`：18/18。
- AI 静态门禁：25/25。
- BuildFix11 双 WebView/演员专项：34/34。
- 评价管线门禁：98/98；删除事务门禁：51/51。
- BuildFix12 R8/R9/R10/R11：18/18、4/4、15/15、22/22。
- 内嵌 JavaScript：全部通过；评价协议：6/6。
- 构建输出：`D:\chatgpt\观影助手-0.9-recognition-fix\build-verify-avatar-ai-filter`。
- 验证 EXE：`观影助手.exe`，151,552 字节，SHA-256 `09D5C94C19C808F5291442EC7E83C204739AAFCDF72724A7734B44FE2D48393A`。
- 业务 DLL：SHA-256 `7A63298FF231B78CEEB0BF2376C9FE66B8FA83F1C46A1218DD905077B9FE0515`；筛选前端 `WebAssets/MediaLibrary/app.js`：SHA-256 `9A4CE6A711E2DD9070C778F03D1615AD83DACD9604CF4CB94219B175E91CFCAB`。
- 已关闭旧验证进程并启动上述新目录，诊断日志确认启动路径正确且进程响应正常；由于 Windows 自动控制组件被系统权限拒绝，本轮未宣称完成真实点击/截图验收。

## 尚未完成的真实验证

- 登录豆瓣后打开《天才枪手》`SubjectId=27024903`，确认查侬·散顶腾古由同片 `/celebrities` 卡片正确补全。
- 复测曾出现曼城队徽/用户头像的 52 人条目，确认错误图片不会恢复。
- 使用真实 DeepSeek 生成 10 题，确认一般情况不触发本地兜底，并检查题目角度和响应时间。
- 完成一次 AI 生成影评、官方表单自动提交、官方回读确认。
- 完成 PotPlayer 播放结束入口的真实端到端验证。
- 在新验证版界面输入“天才”，确认《天才游戏》和《天才枪手》两张卡片同时显示。

未完成以上验证前，本目录只能称为开发验证副本，不能覆盖正式 v0.9 发布包。
