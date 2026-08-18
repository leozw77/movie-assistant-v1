# 观影助手 v1.0 独立试用版交接文档

> 新对话或其他 AI 首先阅读本文件。
>
> 更新时间：2026-08-10

## 1. 当前结论

本独立开发副本版本为 **v1.0 独立试用版**；正式稳定基线仍是 **v0.9.0 BuildFix12 R11**。此前的 v0.8.9 仅作为历史稳定版本，不是本轮新功能开发基线。

本轮针对“文件名识别为错误 IMDb 影片并永久缓存”的识别问题，已在独立开发副本完成第一阶段修复；随后继续完成了 AI 问题生成和官方演员头像链路修复。当前开发副本已经编译成功，内置自测 **106/106 通过**、评价专项自测 **18/18 通过**，但尚未完成真实网络、PotPlayer、登录豆瓣头像补全和 AI 自动提交端到端验证，因此暂未覆盖正式 v0.9 稳定发布包。

## 2026-08-10 官方演员头像与 AI 问题修复

头像不再回退旧的人物主页读取。首页演员卡片缺图时，仅在 `WorkerWebView` 中读取同一 `SubjectId` 的官方 `/subject/{id}/celebrities` 页面；解析范围严格限定为 `#celebrities .celebrities-list > li.celebrity`，按 `CelebrityId` 回填。后台页面会分批 `scrollIntoView` 触发懒加载，完成全部卡片滚动后还要连续三次得到相同卡片/头像签名才接受。头像 URL 必须是 HTTPS 豆瓣图片域名且路径为 `/view/personage/` 或 `/view/celebrity/`；不同人物共享同一 URL 时整组隔离，下载内容还会检查大小和 JPEG/PNG/GIF/WebP 文件签名。人物主页、用户 `/icon/u...`、侧栏和跨卡片搜索继续禁止。缓存版本升级为 `CastParserVersion=4`、`FullCastParserVersion=5`。

AI 外部资料缓存升级为 `knowledge-v3-`。现在会读取并验证 Wikidata 实体自身的 `P345`，不再把请求的 IMDb 编号原样写成“已验证编号”。IMDb 与年份精确一致时，允许《天才枪手 ฉลาดเกมส์โกง》/《模犯生》这类同片地区译名；IMDb 或年份冲突仍阻止使用。问题保持 10 道内容题、恰好 3 道必答多选；末尾补充说明独立且可不填。影片事实仍要求问题级证据，观点选项不再错误要求逐字引用，因此消除了日志中的 `option-evidence` 双重失败。若 AI 返回的问题证据或结构仍不可用，改用豆瓣官方字段生成本地证据安全十题，不继续追加联网重试，也不引入资料外的人物、关系、事件或场景。

影视库历史页同步成功后，旧前端会把筛选范围从完整本地镜像缩成豆瓣当前在线页（通常 15 条），造成“天才”等旧影片实际仍在 1051 条记录中却显示空白。2026-08-10 已改为：筛选框非空时始终搜索完整本地镜像；在线页继续用于无筛选远端分页并增量合并。本地实测数据中“天才”可匹配《天才游戏》和《天才枪手》，未发生数据丢失。

本轮构建输出仅在：

```text
D:\chatgpt\观影助手-0.9-recognition-fix\build-verify-avatar-ai-filter
```

正式 v0.9 BuildFix12 R11 目录没有修改。详细文件清单和验证边界见 `review/AVATAR_AI_FIX_20260810.md`。

## 下一阶段：参考 Douban Plus 重做影视详情页（尚未开始编码）

参考项目：`https://github.com/ZlatanCN/douban-plus`。目标是复刻其信息架构和连续沉浸式体验，不把用户脚本整体嵌入程序，也不改变现有官方豆瓣写入事务。

建议实现顺序：

1. 第一期先重做现有 `WebAssets/MediaLibrary` 详情层：Hero 主视觉、海报/背景、片名年份、豆瓣评分和个人状态、AI 问答写影评主按钮、吸顶分区导航，以及简介/演职员/详细资料分区。优先复用现有 C# DTO 和 WebMessage，不增加新的网络来源。
2. 第二期增加剧照瀑布流、预告片弹窗、短评/长评/讨论预览、系列、相关推荐和播放平台。所有豆瓣数据继续由受控 WebView2 官方页面读取，携带 `SubjectId + RequestId`，分阶段缓存并拒绝旧请求覆盖。
3. 第三期再实现全部照片、全部短评、全部影评子页面，以及用户明确点击后才打开的人物页。人物页不得重新成为头像读取回退；头像仍只允许同片官方 `/celebrities` 卡片按 `CelebrityId` 解析。

必须保留的安全边界：AI 自动提交继续复用官方 DOM 表单、结算和官方回读；外部评分/资料必须先精确校验 IMDb 和年份；历史同步只增量合并本地镜像；缺失区块使用空状态而不是跨页面猜数据。若直接复用 Douban Plus 的 MIT 源码，需要保留许可证和版权声明。

## 2026-08-09 AI 影评外部资料身份拦截

AI 影评的豆瓣官方详情已作为主资料，但 IMDb 补充资料曾按编号直接信任 Wikidata 页面并缓存，未比较外部页面标题/年份。实测 `SubjectId=35159709`《天才游戏》、`tt40398957` 取到了《天才衝衝衝》资料，导致证据校验失败。

该段记录的是 2026-08-09 的第一版门禁，已被上面的 2026-08-10 实体 `P345` 验证和地区译名规则取代。带 IMDb 时仍不回退仅按片名搜索；当前缓存键为 `knowledge-v3-`。

## 2026-08-09 演职员头像误绑定修复

实测发现完整演职员页的部分卡片返回了豆瓣用户图标（`/icon/up...jpg`）或其他页面图片，旧逻辑又会在缺图时进入人物主页读取，可能把侧栏推荐用户头像绑定到演职员。2026-08-10 修复在此安全边界上增加了“同片官方完整演职员页精确补全”和懒加载稳定采样；当前版本为 `CastParserVersion=4`、`FullCastParserVersion=5`。正式发布目录未修改。

## 2. 工作目录与发布边界

### 当前识别修复开发目录

```text
D:\chatgpt\观影助手-0.9-recognition-fix
```

后续识别相关开发先在此目录进行。

### v0.9 正式稳定发布目录

```text
D:\chatgpt\观影助手\发布版本\观影助手-v0.9.0-豆瓣评价写入删除与自动同步-BuildFix12R11-net8轻量版
```

当前正式包保持不变：

- EXE：`观影助手-v0.9.0-BuildFix12R11.exe`
- EXE SHA-256：`A35408ED8F2D2AE17DE4E50CC6F977F6582D9EB7B34A963532369BD9624C2B92`
- ZIP SHA-256：`5D9E1EBF115A042EA200823A8D54E22ABF15D81DB33A42479940A1D2F07F9592`
- 目录中仅保留 `SHA256SUMS.txt` 作为独立 TXT，其余历史 TXT 已合并到 `DEVELOPMENT_HISTORY.md`。

禁止直接覆盖正式 v0.9 目录、正式 ZIP、历史发布目录或 `stable-v0.8.9`。

### 原主工作树

```text
D:\chatgpt\观影助手
```

当前仍是 `feature/douban-write-v0.9.0` 工作树，包含未提交的豆瓣写入/删除/同步改动。识别修复不要直接混入此工作树。

## 3. 已确认的识别故障链路

测试文件：

```text
[长夜将尽] Wild.Nights.Tamed.Beasts.2025.1080p.WEB-DL.x265.mkv
```

期望识别：

- 中文名：《长夜将尽》
- 英文名：`Wild Nights, Tamed Beasts`
- 年份：2025
- IMDb：`tt37151954`

旧程序实际识别为：

- `Five Nights at Freddy's 2`
- IMDb：`tt30274401`

根因：

1. 旧 `MediaHelpers.cs` 删除了全部方括号内容，丢失最可靠的中文片名。
2. 旧 `ForeignMetadataService.cs` 只验证摘要长度、年份和 IMDb 格式，没有验证候选标题与输入标题是否相似。
3. Wikipedia 返回结果后，`TrayContext.cs` 直接接受，DeepSeek 和人工确认不会再介入。
4. 错误结果写入 `auto-binding-v2-文件名`，以后永久复用，没有版本迁移或纠错入口。

## 4. 已完成的识别修复

### 文件名证据保留

`MediaHelpers.cs` 新增 `MovieTitle.ParsePath()` 和 `MovieTitleParts`：

- 保留方括号、全角括号中的中文别名。
- 保留英文标题和年份。
- 仅过滤明确的分辨率、片源、编码、字幕等发布标签。
- `FromPath()` 继续兼容旧调用。

### 候选标题硬门槛

`RecognitionMatcher.cs`：

- 中文标题命中、英文标题规范化命中、英文 token 重合分别评分。
- 候选页面标题和输入别名没有明显重合时，直接禁止自动接受。
- 年份冲突时禁止强匹配。
- 合法 IMDb 格式本身不增加标题匹配分。
- “Five Nights at Freddy's 2” 对本次文件会被拒绝。
- “Wild Nights, Tamed Beasts” 会获得强匹配。

### 候选列表与证据

`ForeignMetadataService.cs`：

- 搜索多个别名和中英文 Wikipedia。
- 不再直接返回第一条结果。
- 去重后按匹配分返回最多前三项。
- 返回标题、年份、IMDb、来源、匹配分和匹配证据。

### 缓存版本与纠错

缓存版本：

- 自动识别：`auto-binding-v3`
- 人工确认：`confirmed-binding-v1`

旧 `auto-binding-v2` 不会再被读取，因此旧错误缓存会自动失效。

提醒窗口新增“更正影片/重新识别”：

- 清除该文件的自动识别缓存和人工确认缓存。
- 重新搜索候选。
- 仍无法可靠识别时进入人工豆瓣官方页面确认。
- 人工确认和自动识别分开缓存。

### DeepSeek 使用边界

`AiServices.cs` 中影片识别请求使用低随机性参数。DeepSeek 只作为解析/辅助提示，不能单独决定 IMDb 绑定；自动身份必须通过外部候选标题一致性校验。

### 识别证据字段

`MovieIdentity` 新增：

- `CacheVersion`
- `ConfirmationMethod`
- `RecognitionSource`
- `InputFileName`
- `InputAliases`
- `MatchScore`
- `MatchEvidence`

## 5. 当前验证结果

在识别修复副本执行：

```powershell
$repo='D:\chatgpt\观影助手-0.9-recognition-fix'
$env:APPDATA=Join-Path $repo '.build-appdata\Roaming'
$env:NUGET_PACKAGES='D:\chatgpt\DoubanReview_0.9.0_Stable_BuildFix12_R11_2026-08-09\.nuget-packages'
dotnet build "$repo\观影助手.csproj" -c Release -r win-x64 --no-restore -p:ContinuousIntegrationBuild=true
```

结果：

- 编译成功。
- 0 个错误。
- 有 1 个 `NU1900` 警告，原因是当前环境无法访问 `api.nuget.org` 漏洞数据源，不是源码编译错误。

运行：

```text
观影助手.exe --self-test
```

结果：

```text
内置自检：106/106 项通过
```

已覆盖：双语文件名解析、错误候选拒绝、正确英文候选强匹配、年份冲突拒绝、缓存版本升级、Wikidata 实体 IMDb 校验、地区译名放行、AI 安全十题兜底、官方头像域名和重复绑定隔离等。评价专项自检另为 `18/18`。

## 6. 尚未完成的功能或验证

### 识别功能仍可继续增强

- 候选评分目前主要依据标题别名和年份，尚未加入导演、演员、类型等资料。
- 尚未实现“豆瓣在线搜索优先，Wikipedia 只补充剧情”的完整候选链路。
- 尚未专门处理重复 IMDb 条目、多来源冲突和导演剪辑版冲突。
- 当前回归样本只有本次案例及基础年份冲突案例，尚未建立完整样本库。
- 已保存最终识别证据，但尚未保存所有被拒绝候选及逐项拒绝原因。

### 发布前必须完成的真实验证

当前环境无法连接 Wikipedia 网络接口，因此以下项目还没有完成：

1. 使用真实网络搜索本次文件，确认不会绑定 `tt30274401`。
2. 在真实 PotPlayer 中播放本次文件。
3. 连续识别同一文件两次，确认第二次读取正确缓存。
4. 点击“更正影片/重新识别”，确认旧缓存清除并可重新确认。
5. 确认评价窗口显示《长夜将尽》及正确 IMDb `tt37151954`。
6. 在最终修复包上执行 Windows 发布冒烟和 ZIP/EXE 哈希检查。

未完成上述真实验证前，不要把识别修复副本称为正式稳定发布版。

### v0.9 原有可选未来工作

根据正式 `STATUS.md`，这些不是 v0.9 主线缺陷：

- API Key 安全和数据迁移方案。
- 如果未来明确需要，再设计豆瓣 Tags 主动写入。
- 修改 PotPlayer 外围链路后重新执行专项冒烟测试。

## 7. 新对话建议起点

新 AI 应按以下顺序工作：

1. 首先阅读本文件。
2. 确认工作目录是 `D:\chatgpt\观影助手-0.9-recognition-fix`。
3. 不读取或修改 v0.8.9 作为本轮开发基线。
4. 先完成第 6 节真实识别回归，再考虑 P1/P2 候选增强。
5. 任何发布操作都创建新的候选目录，不覆盖现有 v0.9 正式目录。
6. 修改后必须重新编译、运行 `--self-test`，并记录 EXE/ZIP 内容和 SHA-256。

## 8. 主要源码入口

- 文件名解析：`MediaHelpers.cs`
- 标题匹配与缓存版本：`RecognitionMatcher.cs`
- Wikipedia/Wikidata 候选：`ForeignMetadataService.cs`
- 识别流程、缓存和重新识别：`TrayContext.cs`
- 候选确认界面：`DoubanConfirmForm.cs`
- 提醒窗口入口：`ReminderForm.cs`
- AI 辅助识别：`AiServices.cs`
- 识别回归测试：`SelfTest.cs`
- 数据模型和缓存存储：`Models.cs`
