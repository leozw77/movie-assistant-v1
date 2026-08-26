# Douban Interest API Probe

独立测试工具，不修改观影助手稳定逻辑。

## 目的

验证不打开豆瓣评价状态窗口时，能否直接通过豆瓣 HTTP 接口写入：

- 想看 (`wish`)
- 在看 (`do`)
- 看过 (`collect`)
- 1–5 星评分
- 短评 (`comment`)
- 取消标记

主写入端点：

- `POST https://movie.douban.com/j/subject/{id}/interest`
- `POST https://movie.douban.com/j/subject/{id}/removeinterest`

表单使用 `application/x-www-form-urlencoded`，CSRF 字段为当前登录会话的 `ck`。

## 使用

1. 启动程序，在左侧内嵌 WebView2 登录豆瓣。
2. 点“检查登录状态”，确认检测到 `ck`；通常登录后还会检测到 `dbcl2`。
3. 输入豆瓣 Subject ID。
4. 选择想看/在看/看过。
5. 按需勾选“提交评分”和“提交短评”。
6. 点“写入状态 / 评分 / 短评”。
7. 查看原始 HTTP 返回。
8. 点击“打开影片页确认”，并在豆瓣网页版人工确认最终结果。

## 安全/测试约束

- 启动时不会自动写入。
- 不保存账号密码。
- Cookie 只从本程序自己的 WebView2 会话读取；日志只显示 Cookie 名称，不显示值。
- HTTP 2xx 不被视为最终成功；必须人工网页确认。
- “取消标记”会二次确认。
- 不批量写入，不自动重试写请求。

## 说明

这里验证的是豆瓣网页自身使用的直接 HTTP 写接口，不是 `frodo.douban.com` 的私有写接口。它的意义是先验证我们的核心目标：是否能完全绕开慢的可见评价状态窗口，同时写入状态、评分和短评。
