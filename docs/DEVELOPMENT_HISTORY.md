# DoubanReview 开发更新总表

本文是 `0.9.0-preview.6 BuildFix12 R11` 的**统一历史入口**。散落的 `BUILD_FIX*.txt`、`review/DIAGNOSTIC_*` 和旧报告继续保留作为原始证据，但后续交接优先阅读本文。

## 基线原则

从 preview.6 起，评价主线始终坚持：

- 豆瓣网页在线数据是唯一权威；本地只作为离线缓存。
- 不直接 POST、不自行拼豆瓣写入 API、不导出 Cookie。
- 状态/评分/短评必须通过官方 DOM 控件提交。
- 只有官方结算并回读确认后才能更新本地并提示成功。
- 无法确认时宁可 `failed/unconfirmed`，不能提前把目标值写入本地。

---

## BuildFix5 — 详情/评价性能第一轮优化

- 基于完整源码合并早期评分 Clear 支持。
- 复用已经加载完整的 subject 页面。
- 官方表单稳定样本从 3 次降为 2 次。
- 结算等待改为更快的自适应轮询。
- 页面导航尚未完成时避免重型详情读取。
- 仍保留提交后的强制官方回读。
- 一键诊断收集器进入正式包。

**结果：**普通状态/评分保存速度明显改善；当时评分 Clear 仍有官方控件假设问题。

## BuildFix6 — 修复评分 Clear 假设 + 暂停自动历史同步

- 证实豆瓣实际表单并不存在可靠的 `img#star0` 清分控件。
- Clear 改用官方可见状态控件的安全往返路径，不直接写 hidden rating。
- 去掉详情元数据阶段重复的状态能力表单读取。
- 页面稳定采样继续保持 2 次。
- 为避免单 WebView2 与历史列表抢占，启动和切标签改为“缓存先显示、手动同步”。

**后续：**R10 在双 WebView2/Worker 去重架构成熟后恢复自动同步。

## BuildFix7 — 区分“官方成功”和“本地缓存更新”

- 增加 `OfficialConfirmed` 语义。
- 连接器中间层不再把延迟到 UI 层完成的缓存更新误记为最终失败。
- `HtmlMediaLibraryForm` 在应用官方快照后生成最终 `LocalUpdated`。
- 前端分别显示官方确认和本地缓存结果。
- 成功导航不再输出误导性的 `Error=Unknown`。

## BuildFix8 — 评分 Clear 两阶段官方事务

- 修复“在看/看过有评分 → 想看并清分”被错误阻止。
- `target=wish + rating Clear`：允许官方 wish 提交后由服务器清分，再通过官方回读确认。
- `target=do/collect + rating Clear`：先完成 wish 清分事务，再提交最终状态事务。
- hidden rating 始终只读，禁止直接赋值。
- 中间阶段成功、最终阶段失败时，本地以已经确认的官方中间状态为准，不保留陈旧数据。

## BuildFix9 — 详情分阶段加载

- 详情打开立即使用卡片数据/本地缓存渲染。
- 拆分 `detailCached / detailMetadata / detailReview`。
- 官方评价未完成前禁止保存。
- 完整演职员退出主详情流程，只在用户点击后读取。
- 增加短时官方评价快照复用，减少保存前重复读取。
- Cookie 定时检查降频并避开详情/保存忙碌阶段。
- 增加 operation/subjectId/page/耗时诊断。

## BuildFix10 — 首页六位、分层 TTL、评价完整性保护

- 缓存 TTL：基础详情 24h；首页 Cast 7d；FullCast 7d；官方评价每次打开重读。
- 首页演职员按豆瓣原顺序最多 6 位。
- 完整演职员只在“查看全部”时读取。
- 已有评价表单必须恰好一个 `wish/do/collect` 被选中；初始化不完整时 250ms 有限重采样。
- MarkedDate 改为豆瓣官方回读值。
- NoChange 独立提示。
- SubjectId 一致性保护和 `review-transactions.jsonl` 增强。
- R1 修正 legacy self-test 对 `personage-default` 过滤关键词的误报。

## BuildFix11 R1 — 双 WebView2 主架构

- 建立长期存在的 `DetailWebView` 与 `WorkerWebView`，共享同一环境/Profile。
- Detail 只负责当前影片详情和首页 Cast；Worker 负责所有会改变 URL 的后台任务。
- 新增 Worker 单消费者优先级队列。
- 首页 Cast 改为“按卡片”而非“按人物”去重，同一人物不同职务可重复。
- FullCast 改为逐 `li.celebrity` 卡片解析，禁止向共同父节点取头像。
- FullCast 在 `readyState=complete` 后进行卡片/头像签名稳定采样。
- 增加统一头像过滤、PersonAvatarCache、后台增量更新。
- 增加 SubjectId + RequestId 过期结果保护。
- `CastParserVersion=2 / FullCastParserVersion=2` 定向失效旧演员缓存。

## BuildFix11 R2 — legacy 自检与头像兼容

- Windows R1 已成功真实构建。
- legacy comprehensive 的两项 TTL 警告被确认是测试夹具未设置新 ParserVersion。
- FullCast 卡片内部补 `data-background`、`::before`、`::after` 背景读取。
- 继续坚持卡片内隔离，不恢复跨卡片祖先评分算法。

## BuildFix11 R3 — FullCast 人物主页最后一级补图

- `FullCastParserVersion` 升到 3，只失效旧 FullCast。
- FullCast 缺图增加独立低优先级 Worker 补全链：同人物其他卡片 → PersonAvatarCache → 人物主页。
- 同人物多职务只访问一次人物主页，但所有角色卡片分别更新。
- `castAvatarUpdated` 增加 `castScope=home/full`。
- WorkerWebView 视口从 1×1 改为 1024×768 离屏尺寸，提高懒加载/IntersectionObserver 兼容性。

**用户实测：**BuildFix11 主线进入稳定基线；普通评价和首页 6 位演职员确认正常。

---

# BuildFix12：删除、崩溃恢复、性能与自动同步

## BuildFix12 R1 — 删除 v2 初版

- 重新开放删除入口 `deleteEntry`，旧 `delete` WebMessage 继续封死。
- 删除改为 Worker 最高优先级独立事务。
- 删除前官方读取、官方 DOM 操作、结算、官方回读、最终本地 tombstone 分离。
- failed/unconfirmed 绝不清本地。
- tombstone 只清豆瓣个人评价镜像，保留影片资料、演员、头像和软件观看记录。
- 外部在豆瓣网页手动删除后，后续官方无记录回读也可同步 tombstone。

## BuildFix12 R2 — Windows 编译修复 CS0173

- 修复 `null : JsonElement` 条件表达式无法推断类型。
- 显式使用 `JsonElement?`。
- 仅修编译类型，不改变删除语义。

## BuildFix12 R3 — 详情确认与列表传播诊断

- 初期日志曾观察到详情已空而个人列表仍有旧条目，因此短暂将个人列表视为辅助证据。
- 后续人工豆瓣网页复测进一步发现：`do + 评分 + 短评` 从详情页删除会形成特殊不一致；这一结论促成 R4 改路由。

## BuildFix12 R4 — `do` 固定改走个人在看列表

```text
do      -> PersonalDoList
wish    -> SubjectDetail
collect -> SubjectDetail
```

- Worker 打开 `/people/{ProfileId}/do`。
- 按 `/subject/{SubjectId}/` 精确定位目标影片。
- 删除控件只能来自目标影片自己的卡片。
- `do` 不允许找不到后回退详情页删除。
- 成功要求列表与详情两侧官方证据一致。

## BuildFix12 R5 — Windows 编译修复 CS0136

- 修复 `DeleteV2.cs` 同一方法作用域 `listWarning` 局部变量重名。
- 仅做编译修复，不改变删除业务。

## BuildFix12 R6 — 真实鼠标输入

- 诊断确认 R5 已打开正确 `/people/{ProfileId}/do` 且找到唯一删除按钮，但 JS `node.click()` 与人工鼠标点击不等价。
- 第一页使用无参数精确地址 `/people/{ProfileId}/do`。
- `do` 删除改用 Chromium `Input.dispatchMouseEvent` 的真实鼠标输入。
- 删除前进行中心点/遮挡检查并记录坐标诊断。

## BuildFix12 R7 — 豆瓣两阶段 do 删除

- 用户诊断确认：`do + 评分 + 短评` 第一次列表删除可能只清掉短评/部分个人数据，SubjectId 仍留在 `/do`。
- 一次用户点击内部允许自动第二次真实鼠标删除。
- 只有 fresh `/do` 连续确认同一 SubjectId 仍存在才触发第二击。
- 最多两击，绝不无限循环。

**用户实测：**R7 两阶段删除正常。

## BuildFix12 R8 — BrowserProcessExited 真恢复 + 删除提速

- 日志确认偶发“豆瓣状态异常、重开后恢复”根因是共享浏览器进程 `BrowserProcessExited`。
- 不再对死亡 CoreWebView2 原地 `EnsureCoreWebView2Async`。
- 宿主统一暂停 Worker、熔断旧导航、销毁并重新创建 Detail/Worker 控件、复用原 DoubanProfile、恢复当前影片。
- 浏览器崩溃不再误报“未登录”。
- 相同 HistoryRead 合并。
- do 删除每击后的旧约 2 秒被动等待改为短 grace period + fresh reload。
- 第二击复用当前 fresh `/do` 页面。
- 删除后取消重复 wish/do/collect 三列表扫描。
- 详情确认改成一次导航 + 两个轻量稳定快照。

## BuildFix12 R9 — Windows 编译修复 CS0157

- 修复 `WorkerJobQueue.PumpAsync` 在 `finally` 中直接 `return`。
- 改为 `finally` 内只设置 `stopPumpAfterJob`，离开 finally 后再退出。
- 保留 R8 的恢复/熔断逻辑。

**用户实测：**删除速度达到满意水平。

## BuildFix12 R10 — 恢复自动历史同步

- 恢复 BuildFix6 为性能临时关闭的自动同步。
- 启动：缓存先显示，然后自动同步当前豆瓣标签。
- 切换 `collect / wish / do` 自动同步，无需每次点按钮。
- 每标签 5 分钟节流。
- 前端 in-flight 去重 + Worker HistoryRead 后端去重。
- HistoryRead 保持低优先级并可被评价读写/删除抢占。
- 登录恢复和 BrowserProcessExited 重建成功后自动恢复当前标签同步。
- “立即同步豆瓣”保留为强制刷新。

## BuildFix12 R11 — 发布前收尾 / 文档冻结

- **不改变业务逻辑。**
- 将 README、STATUS、CHANGELOG、BuildFix12 实施报告、验收说明、包清单和 AI_HANDOFF 统一到当前 R10 业务基线。
- 新增本文作为 BuildFix5～BuildFix12 的统一更新总表。
- 新增 `docs/CURRENT_ARCHITECTURE.md` 和 `review/RELEASE_CANDIDATE_SUMMARY.md`。
- Windows 构建脚本更新为 R11 标识，并把当前 Markdown 交接/状态文档复制进发布目录。
- 增加 R11 文档一致性门禁，防止关键交接文档继续停留在 R4/R8 等旧状态。

---

## 当前最终不变量

1. 豆瓣网页是在线权威；本地只做缓存。
2. 普通保存仍为 ReviewWrite v2 Keep/Set/Clear，不因删除功能而重写。
3. DetailWebView 不为后台任务跳走；导航型任务归 WorkerWebView。
4. Worker 保存/删除最高优先级；HistoryRead、FullCast、头像必须让路。
5. `do` 删除必须走 PersonalDoList；不得退回详情页删除。
6. 删除最多两次真实鼠标点击，且必须由 fresh 官方证据决定是否第二击。
7. failed/unconfirmed 不清本地。
8. `BrowserProcessExited` 要真正重建两个豆瓣 WebView2，而不是复用死亡控制器。
9. 自动同步缓存先显示、后台刷新；不能让同步阻塞评价保存/删除。
10. `stable-v0.8.9` 继续不可覆盖，除非以后单独执行正式稳定版登记。
