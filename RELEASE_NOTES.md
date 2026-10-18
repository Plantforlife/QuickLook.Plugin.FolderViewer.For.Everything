# v2.1.1 — 文件夹交互与显示改进

[简体中文](#简体中文) | [English](#english)

## 简体中文

本次更新调整文件夹双击方式、选中背景、滚动同步和“更多”菜单。保留 2.1.0 的按需读取、Everything 加速和后台资源限制。

### 更新内容

**选中背景：** 列表选中行增加 4px 小圆角，保留系统高亮颜色。

**双击方式：** 双击文件夹展开或收起；Ctrl＋双击才在资源管理器中打开。单击箭头仍可展开，文件的双击打开方式保持不变。

**滚动同步：** 关闭延迟滚动，拖动滚动条时实时更新列表；横向滚动时表头同步移动，继续保留列表虚拟化。

**更多菜单：** 在识别到 QuickLook 宿主菜单时，使用紧凑布局和匹配主题的文字/背景，保留“重新加载”和“复制为路径”。文件夹菜单不显示音视频专用的“显示媒体信息”；离开文件夹预览后恢复宿主原菜单。

### 验证

Release x86 和 Release x64 各 32 项本机回归检查全部通过。新增检查覆盖文件夹激活手势、嵌套目录、圆角背景、实时滚动、25,000 行虚拟化、横向表头同步，以及菜单命令转发和宿主菜单恢复。

AnyCPU 插件包已构建并安装到本机 QuickLook 4.5.0，四个安装文件的 SHA-256 与打包内容一致，宿主已加载新版 DLL。完整视觉与键鼠验收尚未完成。详细范围和限制见 [验证记录](VALIDATION.md)。

### 安装

下载 `.qlplugin` 安装包，在资源管理器中选中后按空格，按照提示安装并重启 QuickLook。运行目标仍为 .NET Framework 4.6.2，Everything 为可选组件。

### 历史版本

[2.1.0](https://github.com/Plantforlife/QuickLook.Plugin.FolderViewer.For.Everything/releases/tag/v2.1.0) 引入有界 Everything 查询队列、后台排序、可暂停统计、长路径处理、DPI 图标和 20 种语言翻译。性能测量保留在验证记录中，均注明测量条件。

---

## English

*Folder interaction and display improvements*

This update changes folder double-click behavior, selection styling, scroll synchronization, and the More menu. It retains the on-demand loading, Everything acceleration, and background resource limits from 2.1.0.

### Changes

**Selection styling:** Selected rows now have 4px rounded corners while retaining the system highlight color.

**Double-click behavior:** Double-click a folder to expand or collapse it. Ctrl + double-click opens it in File Explorer. Disclosure arrows still work with one click, and double-clicking a file keeps its usual behavior.

**Scroll synchronization:** Deferred scrolling is disabled, so dragging a scrollbar updates the list immediately. Column headers follow horizontal scrolling, and list virtualization remains enabled.

**More menu:** When the QuickLook host menu is recognized, a compact layout uses theme-matched text and background colors and offers Reload and Copy as path. The folder menu omits the audio/video-specific Show media info action. Leaving the folder preview restores the original host menu.

### Validation

All 32 local regression checks passed in both Release x86 and Release x64. New checks cover folder activation gestures, nested folders, rounded selection, live scrolling, 25,000-row virtualization, horizontal header synchronization, menu action forwarding, and host-menu restoration.

The AnyCPU package was built and installed in local QuickLook 4.5.0. SHA-256 checks matched all four installed files to the package, and the host loaded the new DLL. Full visual and keyboard/mouse acceptance remains incomplete. See the [validation record](VALIDATION.md#english) for coverage and limitations.

### Installation

Download the `.qlplugin` package, select it in File Explorer, press Space, and follow the installation prompt. Restart QuickLook afterward. The runtime target remains .NET Framework 4.6.2, and Everything is optional.

### Previous release

[Version 2.1.0](https://github.com/Plantforlife/QuickLook.Plugin.FolderViewer.For.Everything/releases/tag/v2.1.0) introduced a bounded Everything query queue, background sorting, resumable statistics, long-path handling, DPI-aware icons, and translations for 20 languages. Performance measurements remain in the validation record with their test conditions.
