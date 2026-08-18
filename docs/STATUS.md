# 项目状态

## 当前版本

- 当前版本：`v1.0.1 Douban Plus 稳定版`
- 状态：**稳定发布 / 持续维护**
- 历史正式稳定版本：`v0.9.0 BuildFix12 R11`（保持不变）

## 当前结论

Douban Plus 页面主线保留；旧版 HTML 影视库、历史导入、缓存型 AI 和 C# 演职员读取/缓存链路已切除。新的实时内容 AI 尚未接入，后续直接读取当前豆瓣页面的剧情、短评和长评。

v1.0.1 已由用户完成真实 UI 验收：个人页无限滚动、个人页详情返回位置恢复、搜索页当前页展示和原生分页均纳入稳定边界。本次稳定包新增 Explore 横版筛选布局；Explore 无限滚动仍留待下一轮开发和真实登录态 WebView2 验收。

## 已实现主线

### 评价与状态

- `wish / do / collect` 官方状态读取与修改。
- 星级评分、短评 `Keep / Set / Clear` 三态协议。
- 官方 DOM 表单 + `requestSubmit()`，保存后等待结算并官方回读确认。
- SubjectId 一致性检查、事务日志和删除 tombstone。

### 三个 WebView2

- 可见 Douban Plus WebView：个人页、搜索页和真实影片详情页的同窗重绘/显示。
- `DetailWebView`：详情基础资料读取和后台探测。
- `WorkerWebView`：评价读写删除、搜索等后台任务。
- 三者共享同一 DoubanProfile/Cookie；SubjectId + RequestId 防止旧结果串台。

### 页面与数据边界

- 演员姓名、角色、人物 ID、人物链接、头像均不进入 C# 本地模型或缓存。
- 演员和图片展示由豆瓣页面/Douban Plus 页面自身处理。
- 旧 JSON 中的演员字段不会被继续写回。
- 识别缓存、软件状态和播放联动保留。

### 删除 v2

```text
do      -> PersonalDoList
wish    -> SubjectDetail
collect -> SubjectDetail
```

只操作目标影片的官方控件；官方回读确认成功后才写本地 tombstone。

## 下一阶段

- 选电影页 `/explore` 独立适配：先做只读探针，再实现独立分页加载器，不改写个人页和搜索页边界。
- 实时内容 AI：从当前豆瓣详情页读取剧情、短评和长评，提取关键词并生成评论，再接入正式写入/官方回读协调器。
- 识别链路继续保留，后续相关修改需单独做识别专项验证。
