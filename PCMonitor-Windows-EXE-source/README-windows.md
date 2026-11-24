# PC Monitor — Windows 采集端（原版）

这是独立源码工程，不是已编译的 PCMonitor.exe。只需构建当前这一端。各版本使用同一局域网协议。

## GitHub 云端编译（推荐）

1. 解压 ZIP。将解压后内部文件与文件夹上传到 GitHub 仓库根目录，不要额外套一层文件夹。可为本包创建独立私人仓库，也可把三个包合并到原 PCMonitor 仓库；它们的源码目录、工作流和说明文件名均独立。
2. 检查 `.github/workflows/build-windows.yml` 是否存在。如果网页上传遗漏 `.github`，点击 Add file → Create new file，输入上述完整路径，然后复制 `workflow-windows.txt` 的全部内容并提交到默认分支。
3. 打开 Actions，选择 `PC Monitor - Windows EXE`，点击 Run workflow → Run workflow（推送相关源码也会触发）。如使用原仓库且不再需要旧的 `build.yml`，可先在 Actions 中禁用旧工作流，避免重复构建。
4. 最新任务成功后，打开 Summary，在 Artifacts 中点击 `PCMonitor-Windows`。下载 ZIP 并解压，即可找到 `PCMonitor.exe`。

## 安装与使用

如果原 EXE 已经正常工作，继续使用即可，无需重新编译。解压全部构建产物并保留依赖文件，以管理员权限运行 EXE。仅在可信网络设置为专用网络，并通过程序放行专用网络防火墙。

## 本地编译（可选）

需要 Windows 与 .NET 8 SDK。 安装好环境后双击 `build-windows.cmd`。

## 注意事项

- 未在当前环境实际编译或实机测试；如有错误请保留 Actions 日志。
- 仅用于自己控制的可信局域网，原版 HTTP 通信未加密。不需要关闭整个 Windows 防火墙。
- 三个包彼此独立，无需引用其他源码工程；正常使用仍需 Windows 端与至少一个手机端。
