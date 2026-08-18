# v0.9.0 BuildFix12 R11 验证结果

R11 稳定版发布副本保持 R10 业务代码基线，仅提升版本元数据、修正项目文件名编码并更新发布文档。

## 静态与协议门禁

- R11 文档一致性门禁：22/22 PASS。
- R10 自动同步专项：14/14 PASS。
- R9 编译风险专项：4/4 PASS。
- R8 BrowserProcessExited 恢复/删除性能专项：18/18 PASS。
- BuildFix12 删除专项：51/51 PASS。
- BuildFix11 双 WebView2/演职员回归：34/34 PASS。
- 综合 review/source 门禁：98/98 PASS。
- 嵌入豆瓣 JavaScript：全部 PASS。
- `app.js` Node 语法检查：PASS。
- 评价协议：6/6 PASS。

## Windows 构建证据

- .NET SDK：8.0.423。
- Release `dotnet build`：0 warnings / 0 errors。
- win-x64 framework-dependent single-file publish：成功，`SelfContained=false`。
- 评价 review self-test：18/18 PASS。
- legacy comprehensive self-test：86/86 PASS。
- EXE：`观影助手.exe`，SHA-256 记录在 `BUILD_INFO.txt`。
- ZIP：`观影助手-v0.9.0-win-x64.zip`，SHA-256 由发布目录和交付记录保存。

## 尚未由本次自动流程覆盖

本次没有使用真实登录豆瓣账号执行详情读取、评价保存、do 删除和历史同步的最终人工冒烟；这些必须在实际用户账号和 WebView2 Evergreen Runtime 上完成。静态门禁和本地自检不能替代该项外部页面回归。
