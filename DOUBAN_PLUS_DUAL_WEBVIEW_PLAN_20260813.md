# Douban Plus 后续开发方案：双 WebView 详情返回

日期：2026-08-13  
适用版本：`v1.0.1` 之后的开发线  
工作副本：`D:\chatgpt\观影助手\开发\v1.0-douban-plus`

## 一、目标

解决从个人页、搜索页或 Explore 进入影片详情后，返回页面丢失原筛选状态、滚动位置或偶发恢复失败的问题。

核心原则：

> 列表页负责保存列表状态，详情页负责显示详情；进入详情不销毁或重新导航列表页，返回只切换可见 WebView。

## 二、目标结构

```text
HtmlMediaLibraryForm
├─ _doubanListView       可见/隐藏的列表 WebView
│  ├─ personal collect/wish/do
│  ├─ search subject_search
│  └─ movie.douban.com/explore
├─ _doubanSubjectView    独立的可见详情 WebView
│  └─ movie.douban.com/subject/{id}
├─ _detailView           现有 1×1 详情/评价后端连接器，暂不复用
└─ _workerView           现有后台请求连接器
```

`_doubanListView` 可以先保留现有字段名 `_doubanPlusView`，减少第一阶段改动；但语义上必须明确它是列表 WebView。新建的 `_doubanSubjectView` 才是可见详情 WebView，不应把现有 `_detailView` 直接改成它，因为 `_detailView` 由 `DoubanWebView2Connector` 管理并承担后台评价/请求职责。

两个可见 WebView 必须使用同一个 WebView2 environment/profile，以共享登录 Cookie 和豆瓣会话；它们的消息、导航 ID、错误状态和生命周期必须分开记录。

## 三、列表上下文模型

进入详情前，宿主保存一个不可依赖 URL 的 `DoubanListContext`，至少包括：

- 来源类型：`personal`、`search`、`explore`；
- 列表 URL 和页面来源 URL；
- Explore 当前模式、类型、地区、年代、排序；
- 评分区间、未看过、可播放状态；
- 已加载卡片数量或列表签名；
- 点击的 `SubjectId`；
- 当前 `scrollY` 和必要的卡片视口偏移；
- 列表 WebView 当前导航代次和请求 ID。

`sessionStorage` 可以继续作为 WebView 被重建时的兜底，但不再作为正常返回的主链路。正常返回不应通过 `Navigate(/explore)`、重新点击筛选按钮或固定延时恢复。

## 四、进入与返回流程

### 进入详情

1. 列表适配器通过现有 `chrome.webview.postMessage` 上报 subject id、详情 URL、来源和当前页面状态。
2. 宿主校验详情 URL 属于允许的豆瓣 subject 地址。
3. 宿主保存 `DoubanListContext`。
4. `_doubanSubjectView` 导航到详情 URL。
5. 详情 WebView 加载期间隐藏或覆盖列表 WebView；列表 WebView 不导航、不 Reload、不释放。
6. 详情导航完成后只显示详情 WebView和返回控件。

### 返回列表

1. 顶部返回按钮、详情页右键返回和 WebView 返回入口统一调用 `CloseDoubanSubjectView()`。
2. `CloseDoubanSubjectView()` 隐藏详情 WebView，显示列表 WebView。
3. 列表 WebView 原有 DOM、筛选按钮状态、评分文字、已加载卡片和滚动位置直接恢复。
4. 宿主用列表上下文做一致性检查；只有列表 WebView 被销毁或进程恢复时，才执行定向重建。
5. 恢复失败时显示可操作的刷新页面和返回首页菜单，不进入无法右键的错误黑屏。

## 五、分阶段实施

### 阶段 1：拆分可见详情视图

- 新增 `_doubanSubjectView` 和独立初始化/释放逻辑。
- 保持 `_detailView`、`_workerView` 的后台连接器职责不变。
- 先让个人页和搜索页使用双 WebView，验证不影响已确认能力。

### 阶段 2：迁移 Explore

- Explore 卡片只上报详情请求，不再让 `_doubanPlusView` 直接导航 subject URL。
- 保存 Explore 的完整筛选上下文和滚动位置。
- 返回时验证模式、筛选文字、评分区间、卡片数量和滚动位置。

### 阶段 3：统一恢复与错误处理

- 统一顶部返回、右键刷新、右键返回首页和导航失败处理。
- 为列表 WebView、详情 WebView 分别维护 navigation ID 和 recovery generation。
- WebView2 重建时先恢复列表上下文，再按当前视图决定是否恢复详情。
- 清理当前只针对单 WebView 的 `_activeDoubanReturnUrl` 分支，避免旧历史回退与新视图切换竞争。

## 六、风险与控制

| 风险 | 控制措施 |
|---|---|
| 两个可见 WebView 增加内存和 GPU 占用 | 详情返回列表后可按策略保留或销毁详情页；先测实际运行占用再决定 |
| 共享 Cookie 导致并发导航相互影响 | 共用 environment/profile，但导航事件、消息处理和状态对象完全隔离 |
| 旧 `_detailView` 被误用造成评价链路回归 | 保持 connector 绑定不变，新建独立 subject view |
| 列表 WebView 被重建后状态仍丢失 | 使用 `DoubanListContext` + 页面脚本恢复；记录恢复代次和超时原因 |
| 返回按钮与右键菜单走不同逻辑 | 所有入口只调用一个 `CloseDoubanSubjectView()` |

## 七、验收清单

- Explore 选择“豆瓣高分”、类型/地区/年代/排序、评分区间、未看过、可播放后进入详情并返回。
- 返回后仍显示原模式和原选项，评分区间按钮显示当前区间。
- 返回后卡片数量、第一屏卡片签名、滚动位置不变。
- Explore 加载更多后进入详情并返回，不回到热门电影默认列表。
- 个人页 `collect / wish / do` 进入详情并返回，原无限滚动位置不变。
- 搜索页进入详情并返回，当前搜索页和原生分页状态不变。
- 详情页导航失败、列表页导航失败、WebView2 重建三种场景均可右键刷新和返回首页。
- 登录 Cookie、评分/状态/短评后台连接器和本地待看右键菜单不回归。
- 通过静态检查、Release 构建、内置自检，并完成真实登录态 WebView2 端到端验收后，才更新稳定版元数据和生成新产物。

## 八、明确不做

- 不用第二个 WebView 去复制当前列表 DOM。
- 不把 `_detailView` 直接改成可见详情页。
- 不以重新导航 `/explore` 代替正常返回。
- 不恢复搜索页无限滚动实验。
- 不修改正式 `v0.9.0` 发布物。
