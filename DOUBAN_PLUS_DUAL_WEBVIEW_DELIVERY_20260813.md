# Douban Plus 双 WebView 开发交付说明

日期：2026-08-13  
工作副本：`D:\chatgpt\观影助手\开发\v1.0-douban-plus`  
版本：`1.0.1`  
交付性质：独立开发交付包，不覆盖正式 `v0.9.0` 或既有稳定产物

## 本轮实现

- 列表 WebView 保留个人页、搜索页和 Explore 电影 / 电视剧页的 DOM、筛选状态、已加载卡片与滚动位置。
- 新增独立的影片详情 WebView；两个可见 WebView 使用同一 WebView2 environment/profile，共享登录 Cookie。
- 个人页、搜索页、Explore 进入影片详情时，只隐藏列表 WebView并显示详情 WebView。
- 返回时只切换回列表 WebView，不调用 `/explore`、个人页或搜索页重新导航，不依赖历史回退恢复列表状态。
- 详情 WebView 的消息、刷新、导航和恢复状态与列表 WebView 分开维护。
- 个人页既有无限滚动与点击影片后的卡片定位逻辑保持不变；Explore 在原生“加载更多”代理之上增加窗口滚动哨兵、请求锁、SubjectId 去重、追加渲染和末页/失败状态。
- Explore 与个人影片页保持独立路由，但统一为“看过 / 想看 / 在看 / 探索”四项同级视觉导航；Explore 内提供电影 / 电视剧切换，个人页与 Explore 下滑后显示复用且主题统一的全局搜索入口。
- 列表页不显示宿主返回按钮；只有详情页显示按钮，按钮文案统一为“返回”，不再区分返回来源。
- 本轮 UI 修复：列表切换时先隐藏旧 WebView 并显示单一宿主遮罩，避免豆瓣原始页面闪现；Explore 主导航补齐圆角矩形；个人页筛选栏改为与 Explore 同款面板并移除重复“类型”；待看入口使用黄色按钮，待看页标题显示为“我的待看”。
- 本轮稳定性修复：Explore 根节点改为条件等待，避免在 DOM 尚未挂载时立即重导航；待看脚本同时识别个人页与 Explore 根节点。
- 本轮卡片统一：个人页、Explore 页和本地待看页共用 `douban-card.js/css`，统一标题、类型/年份、辅助信息、状态/评分/日期顺序；待看页标题顺序同时修正。

## 验证结果

- Release 编译：0 警告，0 错误。
- Explore 静态验证：0 failed。
- 个人页静态验证：PASS。
- Douban Plus-only 验证：39 source files checked，0 failed。
- 嵌入脚本验证：all embedded scripts valid，failures=0。
- 待看静态验证：PASS。
- 双 WebView 协议验证：ALL_DUAL_WEBVIEW_PROTOCOL_TESTS_OK。
- JavaScript 语法检查：通过。
- Explore 电影 / 电视剧路由与类型切换静态验证：通过。
- Explore 无限滚动专项静态验证：滚动哨兵、锁定、去重、追加、末页/失败和筛选重置均通过。
- UI 修复专项静态验证：导航圆角、单一切换遮罩、筛选栏、待看标题和按钮样式检查通过。
- Explore 恢复与待看入口专项验证：条件等待、360 次 Explore 等待上限和 Explore 待看挂载断言通过。
- 共享卡片渲染器与三处页面调用断言通过。

## 尚未替代的验收

静态验证和 Release 构建不能替代真实登录态 WebView2 验收。仍需在实际环境确认：

1. 个人页无限滚动后进入详情并返回，卡片与滚动位置保持。
2. Explore 电影 / 电视剧筛选、加载更多后进入详情并返回，筛选文字、卡片数量与滚动位置保持。
3. 搜索页进入详情并返回，当前搜索结果保持。
4. WebView2 浏览器进程恢复、登录 Cookie、右键刷新和返回首页路径。

正式 `v0.9.0` 发布目录、EXE、ZIP 和 SHA-256 未修改。

## 本次卡片内容统一交付包

- EXE：[观影助手.exe](D:/chatgpt/观影助手/开发/v1.0-douban-plus/artifacts/观影助手-v1.0.1-dual-webview-explore-tv-ui-20260813-160858-win-x64/观影助手.exe)
- ZIP：[观影助手-v1.0.1-dual-webview-explore-tv-ui-20260813-160858-win-x64.zip](D:/chatgpt/观影助手/开发/v1.0-douban-plus/artifacts/观影助手-v1.0.1-dual-webview-explore-tv-ui-20260813-160858-win-x64.zip)
- EXE SHA-256：`6009FF4E9080252B46B6165D802A0E813808D90F7D277A3575F388E64CFE7571`
- ZIP SHA-256：`F0F8C95AACC182A2FCC357FA6C649C7BF342079B6C3C255DA2E129CFFA250FCF`
- 交付状态：`independent-trial`，版本 `1.0.1`；正式 v0.9.0 保持不变。
