# AI 二次开发声明

[简体中文](#简体中文) | [English](#english)

## 简体中文

**本项目是原版 `QuickLook.Plugin.FolderViewer` 的 AI 二次开发版本。本 Fork 新增、重构和优化的代码由 OpenAI 模型通过 Codex 生成并迭代完善，初版使用 GPT-5.6 Sol。**

本项目基于 [adyanth/QuickLook.Plugin.FolderViewer](https://github.com/adyanth/QuickLook.Plugin.FolderViewer)，目标是缩短文件夹预览的等待时间，通过 [Everything](https://www.voidtools.com/) 索引提供大小和数量统计，并控制后台工作开销。

人工维护者提出需求、决定功能方向并决定最终发布内容。AI 声明仅适用于本 Fork 新增及修改的部分；原项目代码的版权仍归原作者和贡献者所有。实际完成的测试和验收范围以 [验证记录](VALIDATION.md) 为准。

### 相比原版的主要改动

- **目录加载：** 从初次预览时递归构建目录树，改为先读取当前层级、展开时再加载子目录。原生 `FindFirstFileEx` 在一次枚举中获取名称、属性、大小和修改时间。
- **大小与统计：** 增加 Everything 1.5 SDK3 命名管道和 1.4/1.5 `WM_COPYDATA` 后备协议，异步填充文件夹大小与递归数量。查询采用有界队列、连接复用、超时、取消及离线退避。
- **本地回退：** 统计与目录列表分离，使用低 I/O 优先级并分片暂停；网络路径不自动递归扫描。跳过链接、未知重解析点和需召回的云目录，允许遍历已下载的云目录，并标明部分统计。
- **界面与交互：** 使用 WPF 回收虚拟化、后台排序和按需淘汰的折叠分支缓存，保留选择与展开状态。提供 20 种语言翻译，并增加圆角选中行、双击展开、Ctrl＋双击打开、实时滚动及紧凑文件夹菜单。
- **生命周期与兼容性：** 补充取消、重复 `View()`、幂等清理和 Dispatcher 更新边界，处理访问拒绝、路径删除、长路径、IPC 断开和关闭竞争。Shell 图标异步加载并按类型和 DPI 缓存。
- **工程与验证：** 固定 net462 构建引用程序集，增加 x86/x64 回归测试和 Windows CI，并检查安装包只包含四个插件运行文件。性能结论区分响应速度、完整耗时和统计可靠性。

### 性能说明

测量结果仅反映记录中的设备、进程架构和缓存条件。2.1.0 的模拟队列测试从 16/100 成功提升至 100/100；后台排序改善了交互响应和状态保留，但完整排序刷新耗时增加。不得据此宣称所有场景都更快或资源开销始终更低。

Everything 为可选组件。文件树始终读取文件系统，索引不可用时仍可显示目录；不会为填充每一行文件夹大小而启动独立递归扫描。配置方法见 [README](README.md)。

### 版权与许可

本 Fork 保留原项目各文件的版权、许可证和贡献者署名。Everything SDK 协议相关归属见 [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt)，项目许可见 [LICENSE.txt](LICENSE.txt)。

AI 辅助开发不会取代或免除原项目及第三方组件的版权、许可和署名义务。

---

## English

**This is an AI-modified fork of the original `QuickLook.Plugin.FolderViewer`. Added, refactored, and optimized code in this fork was generated and iteratively refined by OpenAI models through Codex, initially using GPT-5.6 Sol.**

This project is based on [adyanth/QuickLook.Plugin.FolderViewer](https://github.com/adyanth/QuickLook.Plugin.FolderViewer). It aims to reduce folder-preview latency, use the [Everything](https://www.voidtools.com/) index for sizes and counts, and control background resource use.

The human maintainer supplies requirements, selects the intended behavior, and makes publishing decisions. This disclosure applies only to additions and modifications in this fork; original code remains the work of its respective authors and contributors. See the [validation record](VALIDATION.md#english) for completed tests and acceptance coverage.

### Main changes from the original plugin

- **Directory loading:** Instead of recursively building the directory tree during the initial preview, the fork reads the current level first and loads subfolders on expansion. Native `FindFirstFileEx` obtains names, attributes, sizes, and modification times in one enumeration.
- **Sizes and statistics:** Everything 1.5 SDK3 named pipes and a 1.4/1.5 `WM_COPYDATA` fallback asynchronously fill folder sizes and recursive counts. Queries use bounded queues, connection reuse, timeouts, cancellation, and offline backoff.
- **Local fallback:** Statistics run separately from the directory listing, with low I/O priority and resumable slices. Network paths do not trigger automatic recursive scans. Links, unknown reparse points, and cloud directories requiring recall are skipped; hydrated cloud folders can be traversed, and partial statistics are identified.
- **Interface and interaction:** WPF recycling virtualization, background sorting, and eviction of folded branches limit retained work while preserving selection and expansion. The interface includes translations for 20 languages, rounded selection, double-click expansion, Ctrl + double-click opening, live scrolling, and a compact folder menu.
- **Lifecycle and compatibility:** The fork adds cancellation, repeated `View()` safety, idempotent cleanup, and Dispatcher update boundaries. It handles access denial, deleted paths, long paths, IPC disconnects, and shutdown races. Shell icons load asynchronously and are cached by type and DPI.
- **Build and validation:** Pinned net462 reference assemblies, x86/x64 regression checks, and Windows CI support the build. Packaging checks restrict the archive to four plugin runtime files. Performance conclusions distinguish responsiveness, full completion time, and statistics reliability.

### Performance notes

Measurements apply only to the documented device, process architecture, and cache conditions. The 2.1.0 simulated queue test improved from 16/100 successes to 100/100. Background sorting improved responsiveness and state preservation, but full sort/refresh time increased. These results do not establish faster execution or lower resource use in every scenario.

Everything is optional. The tree always reads the filesystem and remains available without the index. The plugin does not launch a separate recursive scan for every folder row just to populate its size. See the [README](README.md#english) for setup.

### Copyright and licensing

This fork retains the original copyright notices, licenses, and contributor attribution in individual files. See [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt) for Everything SDK protocol attribution and [LICENSE.txt](LICENSE.txt) for the project license.

AI assistance does not replace or remove copyright, licensing, or attribution obligations for the original project or third-party components.
