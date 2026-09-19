# Codex 模型切换器

**面向中国用户的 ChatGPT 桌面版实用工具，可为 Codex 一键切换 ChatGPT、DeepSeek、Kimi 三种模型服务。**

蓝黑白界面，中文操作说明，解压即可使用。当前版本：**2.7.1**。

## 下载与安装

### [⬇ 下载 Windows 版 2.7.1](https://github.com/007zsr/codex-model-switcher/releases/download/v2.7.1/CodexModelSwitcher-2.7.1-windows.zip)

[中文下载说明](DOWNLOAD.md) · [全部发行版本](https://github.com/007zsr/codex-model-switcher/releases) · [更新记录](CHANGELOG.md)

1. 下载上面的 Windows 压缩包，解压整个文件夹到有写入权限的位置。
2. 双击 **CodexModelSwitcher.exe**。请保留旁边的 `assets` 和模型目录文件。
3. 选择服务及模型。DeepSeek、Kimi 需要你自己的对应平台 API Key。
4. 点击“只保存配置”，或“切换并重启 ChatGPT”。重启前会提示，正在运行的任务会被中断。

普通用户下载 Windows 压缩包即可，不需要编译源码。GitHub 自动提供的 `Source code` 是源码包。

## 支持的服务

| 服务 | 使用条件 |
| --- | --- |
| ChatGPT | 已安装并登录 ChatGPT / Codex；可用模型取决于账号权限 |
| DeepSeek | 需要 DeepSeek API；支持选择模型和思考档位 |
| Kimi | 需要 Kimi API；开放平台与 Code 会员密钥不通用 |

切换器不提供账号、API Key、会员或 API 余额。

## 系统要求与常见问题

适用于 Windows 桌面环境，依赖 .NET Framework 4.x 和 Windows 凭据管理器。需要自行安装 ChatGPT / Codex；调用在线模型需要网络。

**为什么模型调用失败？** 检查密钥所属平台、账号权限、余额和网络。在“诊断与日志”中查看具体错误。

**切换后旧任务没有变化？** 配置需要应用重新读取；保存后重启 ChatGPT / Codex，并在新任务中验证。

**想报告问题？** 请在 [问题反馈](https://github.com/007zsr/codex-model-switcher/issues) 中描述版本和错误编号。

## 作者说明

本软件由「请教我周帅帅」制作，免费分享，仅供个人方便使用；使用本软件（包括切换模型提供商、修改配置）所产生的任何后果由使用者自行承担。
