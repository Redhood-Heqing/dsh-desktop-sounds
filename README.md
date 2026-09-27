# DSH 桌面提示音

在原桌面客户端中正常使用 **Codex / GPT Work**，任务完成、等待必要确认或最终失败时，自动播放对应的塔科夫提示音。安装后在托盘后台运行，无需另开浏览器或播放器。

这是社区开发的 Windows 桌面辅助工具，不是 OpenAI 官方插件。

## 下载与安装

[直接下载 Windows 安装分发包](https://github.com/Redhood-Heqing/dsh-desktop-sounds/releases/download/v1.0.0/DSH-Desktop-Sounds-v1.0.0-Windows-x64.zip) · [v1.0.0 发布说明](https://github.com/Redhood-Heqing/dsh-desktop-sounds/releases/tag/v1.0.0)

请到本仓库的 [Releases](https://github.com/Redhood-Heqing/dsh-desktop-sounds/releases) 下载 `DSH-Desktop-Sounds-Setup.exe`，或下载安装分发 ZIP 并解压（ZIP 内保留中文安装文件名）；GitHub 的 `Source code (zip)` 是源码，不能直接安装。

1. 使用 Windows 11 x64，先安装并登录具有 Work / Codex 入口的兼容 GPT 桌面客户端。
2. 双击安装 EXE，点击“一键安装 / 修复”。安装时会自动打开通知设置，请暂勿切换窗口。
3. 之后直接使用原客户端。托盘图标提供音量、暂停和声音设置。

默认目录为 `%LOCALAPPDATA%\Programs\DSHDesktopSounds`，已有安装优先原位置修复。不要求 D 盘、Python、Node、浏览器扩展或管理员权限。三种音频已内置，运行不从 GitHub 下载声音。个人项目安装包未作商业代码签名，Windows 可能显示未知发布者；校验值见 Release 的 SHA256.txt，不需要导入专用证书。

卸载使用 Windows“设置 > 应用”中的 DSH 桌面提示音，或开始菜单的“卸载并恢复通知”。保留的 data 是个人设置和通知恢复资料，不要上传到公开仓库。

## 功能与边界

| 功能 | 行为 |
| --- | --- |
| 完成音 | 当前轮次成功结束后自动播放 |
| 确认音 | 等待必要选择或权限审批时播放，普通文字问句不触发 |
| 错误音 | 经核实的最终失败触发；取消和可恢复工具错误不当作最终失败 |
| 去重 | 同一事件共享去重和播放器，避免重复播放 |
| 安静使用 | 全屏、无边框占满屏幕、锁屏或暂停期间丢弃提示，不补播 |
| 默认音处理 | 安装时设置原客户端的通知音与通知时机，卸载时按恢复记录处理 |

当前兼容 OpenAI.Codex 桌面包 **26.924.1866.0 / 26.924.2738.0**，按资源哈希检查。官方升级到未知版本后暂停接入，需要重新适配。只有普通 Chat 的客户端不支持；网页端也不在范围内。

当前安装包已在我的 Windows 11 x64 本机完成真实安装、修复、卸载恢复、特殊字符路径和新版双入口自动完成音测试；尚未完成另一台设备实测。真实审批/最终失败与两小时资源测量属于此前二进制和旧宿主的历史验证。严格 AI 响应对照和英文界面尚未独立验收；不承诺零资源开销或绝对无缺陷。见 [验证摘要](docs/VALIDATION.md)。

## 从源码构建

在 Windows 11 x64 上解压源码，使用 Windows PowerShell 5.1，在仓库根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\client\test.ps1 -EvidenceDirectory .\build-results\components
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\test-config.ps1
```

输出为 `dist/快速安装包/DSH桌面提示音_一键安装.exe`。只构建不会安装或更改客户端设置。组件测试输出目录必须是新目录；重复测试可换一个目录名。测试包含合成状态和隔离文件夹夹具，不能代替真实客户端验收。

使用 Windows 自带 .NET Framework 编译器，无需 Visual Studio；M4A 与已转换的 WAV 均包含在源码中。可选的 `client/DecodeAudio.cpp` 仅用于重新转换音频，需要另行准备 C++/Windows SDK，不属于正常构建前置。

源码目录：`client/` 为运行时与组件测试，`client/Core/` 为事件分类/去重/静音，`installer/` 为安装、卸载与配置测试。原始聊天、账户信息、私有测试会话、开发者配置、临时目录和历史证据压缩包不在公开导出范围。

## 来源、许可与反馈

本项目新增代码采用 [MIT 许可](LICENSE)。声音来自 [dsh-theme-tarkov](https://github.com/ZHIGENGNIAO258/dsh-theme-tarkov)，保留上游 MIT 许可和署名。详见 [第三方说明](THIRD_PARTY_NOTICES.md) 与 [新增代码许可状态](SOURCE_LICENSE_STATUS.md)。

反馈时提供系统版本、GPT 桌面包版本、使用入口、复现步骤及是否前台/全屏即可。不要粘贴账户凭据、完整聊天或整个 data 文件夹。维护适配前可先暂停工具。
