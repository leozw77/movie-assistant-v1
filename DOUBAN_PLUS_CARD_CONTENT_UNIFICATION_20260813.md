# Douban Plus 卡片内容统一交接

日期：2026-08-13  
工作副本：`D:\chatgpt\观影助手\开发\v1.0-douban-plus`  
版本：`1.0.1`

## 实现原则

个人页、Explore 页和本地待看页保留各自的数据读取逻辑，但不再各自拼接卡片 DOM。三处统一调用：

- `WebAssets\DoubanPlus\douban-card.js`：共享卡片视图模型与渲染器。
- `WebAssets\DoubanPlus\douban-card.css`：共享卡片字段、标签、评分和间距 token。

各页面只负责把来源数据映射为：

```text
标题 → 类型/年份 → 辅助信息 → 状态/评分/日期
```

缺失字段不渲染空标签，标题固定紧跟海报，卡片底部字段统一对齐。

## 三处映射

- 个人页：状态、上映年份、个人备注、我的评分、标记日期。
- Explore：电影/电视剧、上映/首播年份、原生副标题、豆瓣评分。
- 本地待看：待看状态、上映年份、加入时间、移出待看操作。

本地待看仍是独立本地数据，不代表豆瓣官方 `wish` 状态；没有评分时不主动请求详情补齐。

## 后续扩展

接入电视剧或新增字段时，优先修改对应页面的 model 映射；统一顺序、字号、间距和标签样式只修改 `douban-card.js/css`。不要重新在三个页面复制卡片 DOM。

## 验证

- Douban Plus-only：39 项通过。
- Explore 静态验证：0 failed。
- 个人页静态验证：PASS。
- 待看静态验证：PASS。
- 共享卡片与页面调用断言：通过。
- Release：0 警告、0 错误。
- 评审专项自检：18/18。
- 综合自检：71/71。

## 交付

- EXE：`artifacts\观影助手-v1.0.1-dual-webview-explore-tv-ui-20260813-160527-win-x64\观影助手.exe`
- ZIP：`artifacts\观影助手-v1.0.1-dual-webview-explore-tv-ui-20260813-160527-win-x64.zip`
- EXE SHA-256：`6009FF4E9080252B46B6165D802A0E813808D90F7D277A3575F388E64CFE7571`
- ZIP SHA-256：`0DEA7433561F9BD71AB45F3FDECF87F141FCD7F275E1A27AC57C7A853AC322BD`

正式 v0.9.0 和既有稳定产物未覆盖。真实登录态 WebView2 UI 验收仍需在用户环境完成。
