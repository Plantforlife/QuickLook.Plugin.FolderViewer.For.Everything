# AI 二次开发声明 / AI-Assisted Modification Notice

## 中文声明

> **本项目是对原版 `QuickLook.Plugin.FolderViewer` 的 AI 二次开发版本。本分支新增、重构和优化的代码均由 OpenAI 模型通过 Codex（初版为 GPT-5.6 Sol） 生成并迭代完善。**

本项目基于 [adyanth/QuickLook.Plugin.FolderViewer](https://github.com/adyanth/QuickLook.Plugin.FolderViewer) 进行增强，主要目标是提高文件夹预览速度，并利用 [Everything](https://www.voidtools.com/) 索引快速提供文件夹大小及递归文件、文件夹数量。

人工维护者负责提出需求、确定功能方向、审查运行结果、执行验收测试并决定最终发布内容。上述 AI 声明仅适用于本 Fork 新增及修改的部分；原项目代码仍归原作者和贡献者所有。本项目保留了原文件中的版权、许可证和第三方声明。

### 相比原版新增和改进的功能

| 功能 | 原版插件 | 本增强版 |
|---|---|---|
| 首次预览 | 递归构建完整目录树后再显示结果，大型目录等待时间较长 | 只枚举当前文件夹，首层内容优先显示 |
| 子文件夹 | 首次预览时一起递归读取 | 用户展开时才按需加载 |
| 文件系统枚举 | 使用托管目录递归和文件对象 | 使用原生 `FindFirstFileEx`，一次读取名称、属性、大小和修改时间 |
| 文件夹行大小 | 文件夹行通常不显示递归大小 | 通过 Everything 异步填充首层及展开后子文件夹的大小，不阻塞预览 |
| 总大小与数量 | 依靠本地递归扫描 | 优先使用 Everything 索引查询总大小、文件数和文件夹数 |
| Everything 兼容 | 不支持 | 支持 Everything 1.5 SDK3 命名管道，并提供 1.4/1.5 `WM_COPYDATA` 后备协议 |
| Everything 异常 | 不适用 | 具备 500 ms 查询预算、取消、超时、畸形回复验证和自动回退 |
| 本地统计回退 | 与目录树构建耦合 | 独立、低优先级、可取消的后台统计，不延迟目录列表 |
| 网络文件夹 | 可能触发昂贵的递归读取 | 不自动递归扫描网络路径 |
| 重解析点 | 可能造成额外遍历或循环风险 | 递归统计跳过链接、未知标签和需召回的云目录，允许遍历已下载的云目录；索引统计一致地解析本地 junction 目标 |
| 文件图标 | 可能对每个文件触发 Shell 图标或缩略图处理 | 按扩展名和文件夹类型缓存通用系统图标 |
| 超大目录 | 没有明确的单层显示保护 | 每层最多显示 25,000 项，并使用 WPF Recycling Virtualization |
| UI 语言 | 英文界面 | 表头、加载提示、状态和底部统计均已汉化 |
| 生命周期 | 使用简单停止标记，异步清理边界较弱 | 支持取消、幂等清理、重复 `View()` 和 Dispatcher 安全更新 |
| 错误处理 | 覆盖范围有限 | 处理访问拒绝、路径删除、长路径、取消、IPC 断开和窗口关闭竞争 |
| 测试 | 缺少针对性能及 IPC 的回归测试 | 提供自动化回归测试、WPF 预览工具及真实 Everything 集成测试，最新验证见 VALIDATION.md |
| 打包 | 基础打包流程 | 确定性版本、干净的插件压缩包及 Everything 协议第三方声明 |

### 性能参考

以下为初版开发机器的历史数据，仅作为参考；当前改进的验证记录见 [VALIDATION.md](VALIDATION.md)：

- `C:\Windows\System32`：3,027 个首层项目的枚举和排序约为 **5.10 至 5.98 ms**。
- Everything 汇总统计：冷查询约 **127 ms**，热查询约 **6.8 至 7.5 ms**。
- junction 回归验证：大小 **579 B**、子文件夹 **1**、文件 **2**，大小和数量均解析到相同目标。
- 自动化测试：**13/13 通过**。
- 集成测试使用的 Everything 版本：**1.5.0.1396a**。

### Everything 使用要求

Everything 是可选加速组件。若要显示索引文件夹大小并获得最快统计结果：

1. 安装完整版 Everything，Lite 版本不提供 IPC。
2. 保持 Everything 运行并启用 IPC。
3. 打开 **工具 > 选项 > 索引 > 大小**。
4. 启用文件大小和文件夹大小索引。

Everything 不可用时，文件夹列表仍会正常显示。本插件不会为了填充每一个文件夹行的大小而启动大量递归扫描。

---

## English Notice

> **This project is an AI-modified fork of `QuickLook.Plugin.FolderViewer`. All code newly introduced, refactored, or optimized in this fork was generated and iteratively refined by OpenAI models through Codex (initially GPT-5.6 Sol).**

This fork is based on [adyanth/QuickLook.Plugin.FolderViewer](https://github.com/adyanth/QuickLook.Plugin.FolderViewer). Its main goals are to reduce folder-preview latency and to use the [Everything](https://www.voidtools.com/) index for fast folder sizes and recursive file/folder counts.

The human maintainer supplied the requirements, selected the intended behavior, reviewed the results, ran acceptance tests, and made the final publishing decisions. This AI disclosure applies only to the additions and modifications in this fork. The original project remains the work of its respective authors and contributors. Existing copyright, license, and third-party notices have been retained.

### Features Added or Improved Compared With the Original Plugin

| Area | Original plugin | This enhanced fork |
|---|---|---|
| Initial preview | Recursively built the complete directory tree before presenting the finished result | Enumerates only the selected folder and displays the first level immediately |
| Subfolders | Read recursively during the initial preview | Loaded on demand when expanded |
| Filesystem enumeration | Managed directory recursion and per-entry filesystem objects | Native `FindFirstFileEx` obtains names, attributes, sizes, and timestamps in one pass |
| Folder-row sizes | Recursive folder sizes were generally not shown in folder rows | Everything asynchronously fills sizes for first-level and expanded child folders without blocking the preview |
| Total size and counts | Required a local recursive traversal | Prefers indexed total size and recursive file/folder counts from Everything |
| Everything support | Not available | Supports the Everything 1.5 SDK3 named-pipe protocol with a 1.4/1.5 `WM_COPYDATA` fallback |
| Everything failures | Not applicable | Uses a 500 ms budget, cancellation, timeouts, malformed-response validation, and automatic fallback |
| Local statistics fallback | Coupled to directory-tree construction | Detached, cancellable, low-priority background statistics that do not delay the listing |
| Network folders | Could trigger expensive recursive work | Never starts an automatic recursive network scan |
| Reparse points | Could add traversal cost or loop risk | Statistics skip links, unknown tags and cloud directories requiring recall, while traversing hydrated cloud directories; indexed statistics resolve local junction targets consistently |
| File icons | Could invoke Shell icon or thumbnail processing for individual files | Caches generic system icons by extension and folder type |
| Very large folders | No explicit per-level display guard | Limits each displayed level to 25,000 items and enables WPF recycling virtualization |
| UI language | English UI | Headers, loading text, status messages, and footer statistics are localized in Simplified Chinese |
| Lifecycle | Simple stop flag with fragile asynchronous cleanup boundaries | Owned cancellation, idempotent cleanup, repeated `View()` safety, and dispatcher-aware updates |
| Error handling | Limited coverage | Handles inaccessible folders, deleted paths, long paths, cancellation, IPC disconnects, and shutdown races |
| Testing | No focused performance or IPC regression suite | Includes automated regression tests, a WPF preview harness, and real Everything integration checks; see VALIDATION.md for current results |
| Packaging | Basic packaging | Deterministic versioning, validated clean archives, and third-party notices for the Everything protocol |

### Reference Performance

The following historical measurements describe the initial version on its development machine. See [VALIDATION.md](VALIDATION.md) for current validation:

- `C:\Windows\System32`: 3,027 first-level entries enumerated and sorted in approximately **5.10 to 5.98 ms**.
- Everything aggregate statistics: approximately **127 ms cold** and **6.8 to 7.5 ms warm**.
- Junction regression: **579 B**, **1 descendant folder**, and **2 descendant files**, with size and counts resolving to the same target.
- Automated tests: **13 of 13 passed**.
- Everything version used for integration testing: **1.5.0.1396a**.

### Everything Requirements

Everything is an optional accelerator. To enable indexed folder-row sizes and the fastest aggregate statistics:

1. Install the full edition of Everything. The Lite edition does not provide IPC.
2. Keep Everything running with IPC enabled.
3. Open **Tools > Options > Indexes > Size**.
4. Enable both file-size and folder-size indexing.

If Everything is unavailable, the folder listing still appears normally. The plugin deliberately avoids launching a separate recursive scan for every folder row merely to populate its size.

## Attribution and Licensing

The original project and its contributors retain their original attribution. This fork preserves existing per-file notices. The managed Everything IPC implementation is based on public Everything SDK protocol definitions; related attribution is included in `THIRD_PARTY_NOTICES.txt`.

AI assistance does not replace or remove any copyright, license, or attribution obligations associated with the original project or third-party materials.
