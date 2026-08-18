# 当前问题分析与修改要求

## 基线

- 产品：观影助手
- 当前源码基线：`0.9.0-preview.1`
- 稳定基线：`stable-v0.8.9`
- 本交接包性质：源码修改输入包，不是正式 `v0.9.0` 发布包
- 当前交接包不包含用户豆瓣 Profile、Cookie 或个人数据

## 已知真实现象

1. 状态、评分、短评写入通常可以完成。
2. 官方状态回读偶发不正常。
3. 点击电视剧时，详情页有时没有“在看”状态按钮。
4. 当前版本已有结果面板，但必须继续保证错误能被用户和开发者区分。

## 根因假设（需要后续 AI 用真实日志确认）

### 假设一：前端和官方表单使用不同的能力判断

详情页使用 `detail.statusOptions` 控制按钮。详情元数据脚本的选择器覆盖不完整，电视剧页面的 `do` 可能没有被提取。

与此同时，统一写入后端会继续尝试通用编辑入口，并在官方编辑表单中读取 `interestOptions`。这意味着：

```text
前端认为 do 不存在
后端官方表单可能实际支持 do
```

需要用同一个官方表单能力结果驱动界面和后端流程。

### 假设二：提交后的稳定等待不足

`WaitForSubmitSettlementAsync()` 的循环结束不代表已确认稳定。当前调用方没有使用显式稳定标志。

需要让等待函数明确区分：

- 已稳定返回影片页；
- 已进入验证码或登录失效；
- 超时仍未稳定。

超时必须进入 `unconfirmed`，不能继续进行看似正常的回读。

### 假设三：表单值出现存在异步竞态

`WaitForOfficialFormAsync()` 目前主要判断表单是否出现，可能早于状态 radio、评分隐藏值和短评值完成加载。

需要对关键字段进行连续稳定采样，而不是只判断 DOM 节点存在。

## 必须保留的行为

- 只允许官方 DOM 和官方表单。
- 统一保存只触发一次官方提交。
- 任何回读不确定都不更新本地豆瓣镜像。
- 软件真实观看记录与豆瓣状态分离。
- 删除豆瓣记录不删除真实观看记录和缓存。
- `wish` 状态不得提交评分。
- 标签写入仍暂缓。

## 建议新增诊断字段

写入阶段建议记录以下对象：

```json
{
  "phase": "snapshot|formProbe|domFill|submit|readback|confirmed|unconfirmed|blocked|failed",
  "subjectId": "数字 Subject ID",
  "requested": {
    "status": "wish|do|collect",
    "rating": 1,
    "commentLength": 0
  },
  "snapshot": {
    "availableStatuses": [],
    "editControlFound": false
  },
  "form": {
    "interestOptions": [],
    "selectedInterest": "",
    "ratingControlCount": 0,
    "commentControlCount": 0
  },
  "official": {
    "status": "",
    "rating": null,
    "comment": ""
  },
  "settled": false,
  "localUpdated": false,
  "error": ""
}
```

实际实现可以采用等价结构，但必须能回答：

1. 豆瓣页面是否提供 `do`？
2. 官方编辑表单是否提供 `do`？
3. 提交后页面是否稳定？
4. 回读到的状态、评分、短评是什么？
5. 本地是否更新？

## 真实测试建议

至少准备：

- 一部普通电影 Subject；
- 一部缺少“在看”的电视剧 Subject；
- 一部实际支持“在看”的电视剧 Subject（如可找到）。

每个 Subject 执行：

1. 打开详情页，记录状态按钮。
2. 保存 `collect + rating + comment`。
3. 保存 `do`。
4. 保存 `wish`，确认评分不提交。
5. 重新打开详情页，检查 UI 与官方回读是否一致。
6. 删除豆瓣记录，确认三个列表均不存在。

## 不应接受的“修复”

- 直接把所有影片都显示“在看”，但不检查官方表单。
- 只根据本地请求值显示成功，不读取官方回读。
- 只增加固定 `Delay(3000)`，不判断实际 DOM 状态。
- 把 `unconfirmed` 改成 `confirmed` 以隐藏错误。
- 回读失败后直接更新本地状态。
- 用 HTTP API、Cookie 或私有接口绕过官方表单。

