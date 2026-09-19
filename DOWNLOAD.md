# 中文下载中心

## Windows 正式版 · 2.7.1

### [点击下载完整压缩包](https://github.com/007zsr/codex-model-switcher/releases/download/v2.7.1/CodexModelSwitcher-2.7.1-windows.zip)

[进入 2.7.1 发布页面](https://github.com/007zsr/codex-model-switcher/releases/tag/v2.7.1) · [查看全部版本](https://github.com/007zsr/codex-model-switcher/releases)

| 下载项目 | 用途 |
| --- | --- |
| `CodexModelSwitcher-2.7.1-windows.zip` | 普通用户下载，内含可运行程序、美术资源、模型目录和中文说明 |
| `SHA256SUMS.txt` | 校验压缩包完整性 |
| GitHub 的 `Source code` | 开发者使用的源码归档，不是推荐的成品下载入口 |

## 三步开始使用

1. 下载并完整解压压缩包。建议放到你的软件目录，并确保该目录可写。
2. 双击 `CodexModelSwitcher.exe`。不要只移动这一个文件，保留完整目录。
3. 选择 ChatGPT、DeepSeek 或 Kimi，设置模型后保存或切换并重启。

DeepSeek 和 Kimi 需要自行准备 API。日志、备份和报告保存在程序旁的 `data` 文件夹。重启 ChatGPT / Codex 前请处理好正在运行的任务。

## 已有旧版如何更新

关闭旧版切换器，把新版解压到一个新目录即可。首次启动会尝试读取旧版 LocalAppData 数据；若需要沿用已经保存在旧软件目录下的数据，可在关闭程序后把旧目录的 `data` 整体复制到新版目录。请保留原目录以便回退。

## 文件校验

下载后可在 PowerShell 中执行：

```powershell
Get-FileHash .\CodexModelSwitcher-2.7.1-windows.zip -Algorithm SHA256
```

将结果与发布页面附件 `SHA256SUMS.txt` 比较即可。

[返回中文首页](README.md)
