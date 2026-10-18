# QuickLook.Plugin.FolderViewer.For.Everything

[简体中文](#简体中文) | [English](#english)

## 简体中文

为 [QuickLook](https://github.com/QL-Win/QuickLook) 提供快速文件夹预览，支持按需展开目录，并通过 [Everything](https://www.voidtools.com/) 索引加速大小和数量统计。

> [!IMPORTANT]
> 本项目是原版 FolderViewer 的 AI 二次开发版本，新增及修改的代码由 OpenAI 模型通过 Codex 生成并迭代完善。原作者的版权、许可证和第三方声明予以保留。

完整开发声明与原版对比见 [AI 二次开发声明](AI_MODIFICATION_NOTICE.md)。本次更新内容见 [版本说明](RELEASE_NOTES.md)。

### 安装

从 [GitHub Releases](https://github.com/Plantforlife/QuickLook.Plugin.FolderViewer.For.Everything/releases) 下载 `QuickLook.Plugin.FolderViewer.For.Everything.qlplugin`，在资源管理器中选中安装包，按空格打开，按照提示安装并重启 QuickLook。

插件运行目标为 .NET Framework 4.6.2，需要 Windows 和 QuickLook。Everything 是可选加速组件；未安装 Everything 时仍可预览文件夹。

### 使用方法

- **展开与打开：** 双击文件夹可展开或收起；按住 **Ctrl 再双击**，才会在资源管理器中打开文件夹。左侧箭头仍可单击展开，文件的双击打开方式保持不变。
- **选择与滚动：** 选中行使用小圆角背景。拖动滚动条时列表实时更新，横向滚动时表头跟随移动。
- **排序：** 点击名称、大小或修改时间表头进行排序，再次点击可切换顺序。排序在后台计算，并保留选中项、展开状态和滚动位置。
- **更多菜单：** 在支持的 QuickLook 宿主中，文件夹预览的“三个点”使用紧凑菜单，提供“重新加载”和“复制为路径”。“显示媒体信息”用于查看音视频编码、时长、码率等，不适用于文件夹，因此不在这个菜单中显示。

### 速度与资源开销

- **按需读取：** 先读取当前文件夹，子目录在展开时加载；首批最多 128 项可提前显示。每层最多显示 25,000 项，配合 WPF 虚拟化减少界面容器开销。
- **索引加速：** Everything 异步提供文件夹行大小、总大小及递归文件/文件夹数量。查询优先处理可见行，复用连接并限制队列和缓存；目录列表始终来自文件系统。
- **后台统计：** 本地递归统计使用后台 I/O 优先级，每轮达到 2 秒或 50,000 项后暂停，可点击“继续扫描”。网络路径不自动启动递归统计；未知大小会保持空白，部分统计会注明状态。
- **图标与缓存：** 图标在有界后台工作线程上加载，并按类型和 DPI 缓存。折叠分支超过 75,000 项缓存预算时按需淘汰；展开分支及选中项的祖先路径受到保护，因此该预算不是进程内存上限。
- **路径与兼容性：** 支持长路径、扩展路径和 UNC 路径；统计跳过链接、未知重解析点和需要召回的云目录，避免意外下载。界面提供 20 种语言翻译。

### Everything 设置

1. 安装完整版 [Everything](https://www.voidtools.com/downloads/)，Lite 版本不提供 IPC。
2. 保持 Everything 运行并启用 IPC。
3. 打开“工具 → 选项 → 索引 → 大小”，启用文件大小和文件夹大小索引。

Everything 1.5.0.1384a 或更新版本可使用 SDK3 命名管道查询，另有 Everything 1.4/1.5 的 `WM_COPYDATA` 后备协议。索引不可用时，本地统计可回退到可取消的后台扫描，不阻塞目录列表。

如需指定 Everything 实例，在 QuickLook 的环境中设置 `FOLDERVIEWER_EVERYTHING_INSTANCE`，然后重启 QuickLook。例如，值 `Work` 对应通过 `Everything.exe -instance Work` 启动的实例。

### 构建与测试

需要 Windows、带有 .NET 桌面构建工具的 Visual Studio / Build Tools，以及首次还原 NuGet 依赖时的网络访问。先初始化 Git 子模块，再执行以下命令：

```powershell
git submodule update --init --recursive
$env:FOLDERVIEWER_VERSION = "2.1.1"
msbuild QuickLook.Plugin.FolderViewer.For.Everything.sln -restore /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU"
./Tests/bin/Release/QuickLook.Plugin.FolderViewer.Tests.exe
./Scripts/pack-zip.ps1
Remove-Item Env:FOLDERVIEWER_VERSION
```

构建使用固定版本的 net462 引用程序集，源代码语言版本为 C# 7.3。`QuickLook.Common` 由宿主提供，不应包含在安装包中；插件包只包含插件 DLL、元数据、翻译和第三方声明。

2.1.1 已在本机通过 x86、x64 各 32 项回归检查，并已安装到 QuickLook 4.5.0。完整视觉和键鼠验收尚未完成；测试范围、历史性能数据和设备验证边界见 [验证记录](VALIDATION.md)。

GitHub Actions 工作流会分别运行 x86、x64 测试，再生成 AnyCPU 安装包。性能测试可在本机执行：

```powershell
./Tests/bin/Release/QuickLook.Plugin.FolderViewer.Tests.exe --benchmark C:\Windows\System32
```

### 许可与致谢

本项目基于 [adyanth/QuickLook.Plugin.FolderViewer](https://github.com/adyanth/QuickLook.Plugin.FolderViewer)。许可详见 [LICENSE.txt](LICENSE.txt) 和各源文件中的原始声明；Everything SDK 协议相关说明见 [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt)。

---

## English

A fast folder preview plugin for [QuickLook](https://github.com/QL-Win/QuickLook), with on-demand folder expansion and optional [Everything](https://www.voidtools.com/) acceleration for sizes and counts.

> [!IMPORTANT]
> This is an AI-modified fork of the original FolderViewer. Added and modified code was generated and iteratively refined by OpenAI models through Codex. Original copyright, license, and third-party notices are retained.

See the [AI modification notice](AI_MODIFICATION_NOTICE.md#english) for the full disclosure and comparison with the original plugin, and the [release notes](RELEASE_NOTES.md#english) for this update.

### Installation

Download `QuickLook.Plugin.FolderViewer.For.Everything.qlplugin` from [GitHub Releases](https://github.com/Plantforlife/QuickLook.Plugin.FolderViewer.For.Everything/releases), select it in File Explorer, press Space, and follow the installation prompt. Restart QuickLook afterward.

The plugin targets .NET Framework 4.6.2 and requires Windows and QuickLook. Everything is optional; folder previews also work without it.

### Usage

- **Expand and open:** Double-click a folder to expand or collapse it. Hold **Ctrl while double-clicking** to open it in File Explorer. The disclosure arrow still toggles with one click, and double-clicking a file keeps its usual open behavior.
- **Selection and scrolling:** Selected rows have rounded corners. Dragging a scrollbar updates the list immediately, and column headers move with horizontal scrolling.
- **Sorting:** Click the Name, Size, or Modified header to sort; click again to reverse the order. Sorting runs in the background and preserves selection, expansion, and scroll position.
- **More menu:** On supported QuickLook hosts, the folder preview uses a compact menu with Reload and Copy as path. Show media info displays details such as audio/video codecs, duration, and bitrate; it does not apply to folders and is omitted from this menu.

### Performance and resource use

- **On-demand loading:** The current folder is read first; subfolders load when expanded. An initial batch of up to 128 entries can appear early. Each level displays up to 25,000 entries, with WPF virtualization limiting UI container overhead.
- **Indexed statistics:** Everything asynchronously supplies folder-row sizes, total size, and recursive file/folder counts. Queries prioritize visible rows, reuse connections, and bound queues and caches. The directory listing always comes from the filesystem.
- **Background statistics:** Local recursive statistics use background I/O priority and pause after 2 seconds or 50,000 entries per slice. Continue scan starts another slice. Network paths do not trigger automatic recursive scans; unknown sizes remain blank and partial statistics are identified.
- **Icons and caches:** Icons load on bounded background workers and are cached by type and DPI. Folded branches are evicted when retained entries exceed the 75,000-entry budget. Expanded branches and the selected item's ancestors are protected, so this budget is not a process memory limit.
- **Paths and compatibility:** Long, extended, and UNC paths are supported. Statistics skip links, unknown reparse points, and cloud directories requiring recall to avoid unintended downloads. The interface includes translations for 20 languages.

### Everything setup

1. Install the full edition of [Everything](https://www.voidtools.com/downloads/). The Lite edition does not provide IPC.
2. Keep Everything running with IPC enabled.
3. Open Tools → Options → Indexes → Size, then enable file-size and folder-size indexing.

Everything 1.5.0.1384a or later can use SDK3 named-pipe queries, with a `WM_COPYDATA` fallback for Everything 1.4/1.5. If the index is unavailable, local statistics can fall back to a cancellable background scan without blocking the listing.

To select a named Everything instance, set `FOLDERVIEWER_EVERYTHING_INSTANCE` in QuickLook's environment and restart QuickLook. For example, `Work` selects the instance started with `Everything.exe -instance Work`.

### Build and test

Use Windows with Visual Studio / Build Tools and the .NET desktop build tools. Network access is required for the initial NuGet restore. Initialize the Git submodules, then run:

```powershell
git submodule update --init --recursive
$env:FOLDERVIEWER_VERSION = "2.1.1"
msbuild QuickLook.Plugin.FolderViewer.For.Everything.sln -restore /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU"
./Tests/bin/Release/QuickLook.Plugin.FolderViewer.Tests.exe
./Scripts/pack-zip.ps1
Remove-Item Env:FOLDERVIEWER_VERSION
```

The build uses pinned net462 reference assemblies and C# 7.3. The host supplies `QuickLook.Common`, which must be excluded from the package. The plugin package contains only the plugin DLL, metadata, translations, and third-party notices.

Version 2.1.1 passed 32 regression checks in each of x86 and x64 locally and was installed in QuickLook 4.5.0. Full visual and keyboard/mouse acceptance remains incomplete. See the [validation record](VALIDATION.md#english) for coverage, historical performance data, and device-specific limits.

The GitHub Actions workflow runs tests in x86 and x64 processes before creating an AnyCPU package. Run benchmarks locally with:

```powershell
./Tests/bin/Release/QuickLook.Plugin.FolderViewer.Tests.exe --benchmark C:\Windows\System32
```

### License and acknowledgments

This project is based on [adyanth/QuickLook.Plugin.FolderViewer](https://github.com/adyanth/QuickLook.Plugin.FolderViewer). See [LICENSE.txt](LICENSE.txt) and the original notices in individual source files. Everything SDK protocol attribution is included in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
