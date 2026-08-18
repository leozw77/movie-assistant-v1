# 当前架构说明（v1.0 Douban Plus）

> 2026-08-12：本文描述 v1.0 独立开发副本。旧版 HTML 影视库、历史导入、缓存型 AI 以及 C# 演职员读取链路已移除；正式 v0.9.0 发布物保持不变。

## 1. WebView2 分工

### 可见 Douban Plus WebView

负责同一窗口中的个人页、搜索页和真实豆瓣详情页重绘/显示，是用户唯一可见的页面。

### DetailWebView

负责详情基础资料读取和当前条目后台探测。演员、头像和人物导航由当前页面/用户脚本自身处理，C# 不读取或缓存这些内容。

### WorkerWebView

负责官方评价读取、保存、删除、回读和搜索等后台任务。评价事务优先，不再承担完整演职员读取任务。

共 3 个 WebView2：1 个可见页面 + 2 个后台页面；它们共享 `WebView2EnvironmentProvider / DoubanProfile`，不复制、不导出 Cookie。

## 2. 普通评价写入

```text
读取官方当前值
→ 打开官方编辑表单
→ 等待语义稳定
→ 填写官方控件
→ requestSubmit() 一次
→ 页面结算
→ 官方回读逐字段匹配
→ 更新本地缓存
```

- 评分/短评：`Keep / Set / Clear`。
- NoChange：不提交，不显示“保存成功”。
- 未确认：不写目标值。

## 3. 删除 v2

`do` 使用个人列表按 SubjectId 精确定位，`wish / collect` 使用影片详情页；成功后才写 tombstone。删除只清评价镜像字段，保留识别和软件观看记录。

## 4. 本地数据边界

- 识别缓存、软件状态、设置和播放记录按现有识别链路保留。
- 不再保存 `Actors`、`Cast`、`FullCast`、人物 ID、人物链接或头像缓存。
- 旧 JSON 中这些字段在反序列化时被忽略，后续保存时不会再写回。
- 新 AI 不读取本地历史 JSON，直接从当前豆瓣页面读取剧情、短评和长评，再复用正式评价写入协调器。

## 5. 故障与过期结果保护

`BrowserProcessExited` 会暂停队列、释放失效 WebView2、复用 `DoubanProfile` 并恢复；详情结果继续使用 `SubjectId + RequestId` 防止快速切换条目时串台。

## 6. 安全边界

禁止导出 Cookie/Profile、直接调用豆瓣写入 API、用 fetch 模拟保存/删除，以及在删除未经官方回读确认前清理本地记录。
