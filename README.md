# Edge 扩展提醒延期 · Edge Snooze

将 Microsoft Edge 的“关闭开发人员模式下的扩展”提醒，自定义延后 **1～1000 周**。

适合需要使用解压扩展、又不想每两周处理一次提醒的用户。保留开发者模式和扩展设置，提供自动查找配置、自定义路径、备份及撤销。

**[下载 Windows 单文件 EXE](https://github.com/mofan6/edge-extension-snooze/releases/latest/download/EdgeReminder.exe)** · **[所有版本](https://github.com/mofan6/edge-extension-snooze/releases)** · **[逻辑与实现原理](docs/how-it-works.zh-CN.md)**

## 特性

- **1～1000 周**自定义，提供 2、52、999、1000 周快捷按钮。
- 根据周数显示预计下次提醒日期。
- 自动识别当前用户的稳定版、Beta、Dev、Canary 常见配置目录。
- 支持多个个人资料，以及策略或运行参数指定的数据目录。
- 可手动选择 `User Data`、个人资料目录或 `Preferences` 文件。
- 写入前自动备份，写入后读回验证。
- 撤销只恢复提醒时间，保留后来修改的其他设置。
- 提供 Edge 进程检测、关闭窗口及确认结束剩余进程的操作。
- 橘影橙配色、圆角按钮；软件本身不联网，不常驻后台。

## 使用方法

1. 下载并运行 `EdgeReminder.exe`。
2. 勾选要修改的 Edge 配置。
3. 输入周数，保存网页内容并完全退出 Edge。
4. 点击“应用提醒时间”，随后重新打开 Edge。

**没有找到配置？** 在 Edge 地址栏输入 `edge://version`，查看“个人资料路径”，用软件的“选择目录”或“选择文件”添加。

Edge 装在其他盘符通常不影响使用，因为软件处理的是个人资料目录，不依赖 `msedge.exe` 的固定安装路径。手动添加的路径在当前软件会话内有效。

## 下载与系统要求

| 项目 | 说明 |
| --- | --- |
| 系统 | Windows 10 / 11，常见 x86 / x64 环境 |
| 运行环境 | 使用 Windows 已有的 .NET Framework 4.x |
| 分发形式 | 单 EXE，免安装，不需要附带应用 DLL 或 Python |
| v1.0.0 体积 | 55,296 字节，约 54 KiB |
| 权限 | 正常使用不要求管理员权限 |

运行后会在当前用户的本地应用数据目录生成备份，这些数据文件不用随 EXE 分发。

## 它是如何工作的？

软件修改每个 Edge 个人资料 `Preferences` 文件中的：

```text
extensions.ui.dev_mode_warning_snooze_end_time
```

把它设置为“当前 UTC 时间 + 所选周数”。这个值是从 1601-01-01 UTC 起累计的微秒数，以字符串形式保存。

对 **Edge 153.0.4234.32** 的相关代码核验表明，提醒判断会读取这个时间，并在截止时间尚未到达时跳过提醒；检查到的判断路径没有将超过两周的时间缩回两周。软件不修改 Edge DLL 或弹窗菜单。

这使用了 Edge 的内部配置，**不同版本或未来更新可能改变、忽略或重置该设置，不承诺永久关闭**。Dev / Canary 等通道也可能本来就不显示相同提醒。完整分析、实现流程与适用边界见[原理文档](docs/how-it-works.zh-CN.md)。

## 备份与恢复

备份位于：

```text
%LOCALAPPDATA%\EdgeReminder\Backups
```

“撤销上次修改”只恢复本工具最近一次修改的提醒时间。如果该时间后来已经被其他程序改动，会停止覆盖。备份含浏览器配置，请不要把备份上传到 Issue 或公开仓库。

## 从源码构建

需要 Windows 和系统 .NET Framework C# 编译器。无需 NuGet 包或 .NET SDK。

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

输出：`dist\EdgeReminder.exe`。

运行测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

测试使用独立的临时配置，覆盖时间范围、精确 JSON 修改、中文路径、多配置发现、文件冲突及备份撤销等 17 组场景，不修改真实 Edge 配置。GitHub Actions 也会执行构建与这些测试。

## 项目结构

```text
src/          WinForms 界面、路径发现、配置修改、JSON 位置解析器
tests/        临时配置上的自动化测试
scripts/      Windows 构建与测试脚本
docs/         时间机制和实现说明
```

## 反馈问题

提交 Issue 时请附上 Windows 版本、Edge 版本、操作步骤和软件报错。不要上传完整 `Preferences`、`Secure Preferences`、备份文件或账号凭据。

## English

**Edge Snooze** is a tiny Windows utility that postpones Microsoft Edge's developer-mode extension reminder by **1–1000 weeks**. It discovers browser profiles, supports custom paths, backs up preferences and provides a scoped undo operation. Developer mode and unpacked extensions are preserved.

Download the single EXE from [Releases](https://github.com/mofan6/edge-extension-snooze/releases), choose a profile and a number of weeks, fully exit Edge, and apply. It uses Windows' existing .NET Framework. This relies on an internal Edge preference; future browser versions may change its behavior.

---

独立工具，与 Microsoft 无隶属关系。源码在此公开供查阅；本仓库暂未指定开源许可证。
