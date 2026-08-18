# Computer Use 启动诊断（2026-08-09）

## 结论

- 观影助手开发版 EXE 可以启动。
- 默认启动模式是托盘程序，没有普通主窗口；因此仅调用窗口枚举时看不到它，不等于启动失败。
- `--history-live-preview` 可打开可见的“影视库”窗口，已验证窗口句柄和进程正常存在。
- 本次未修改正式 v0.9 发布目录，也未修改 Windows ACL、安全设置或观影助手源码。

## Computer Use 控制器原因

首次加载 `@oai/sky` 时，Node REPL 在访问：

`C:\Users\leozw77\AppData\Local\OpenAI\Codex`

报错：

`EPERM: operation not permitted, lstat ...`

系统级只读检查确认该目录存在、ACL 正常，且官方运行时与 `@oai/sky` 均已安装。根因是当前 REPL 沙箱无法读取 Computer Use 的安装路径，而不是安装文件缺失。

已在 `D:\chatgpt\.codex-cua-node-mirror` 建立本机官方依赖的隔离镜像，并补齐 `@statsig/js-client`。镜像加载后 `sky` 可成功初始化。

## 当前剩余限制

`C:\Users\leozw77\.codex\computer-use\config.toml` 当前为：

```toml
[apps]
allowed = []
```

所以 `sky.list_apps()` 和 `sky.list_windows()` 返回空数组，即使观影助手的预览窗口实际已经运行。要继续远程点击/截图，需要在 Computer Use 的应用授权界面允许目标应用；本次没有自动放宽该白名单。

## 验证记录

- 开发版路径：`D:\chatgpt\观影助手-0.9-recognition-fix\build-verify-ai10\观影助手.exe`
- 默认托盘进程：已观察到运行。
- 可见预览窗口：`影视库`，进程响应正常。
- Computer Use `@oai/sky`：隔离镜像加载成功。
- 正式发布包：未修改。
