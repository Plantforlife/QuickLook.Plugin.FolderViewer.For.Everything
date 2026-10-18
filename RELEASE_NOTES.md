# v2.1.0 — 性能与兼容性改进 / Performance and compatibility

本版本修复大型目录中大小查询因排队超时而缺失、排序后选择丢失，以及长路径和部分统计显示的问题，同时限制后台扫描、图标和缓存开销。

- Everything 查询使用可见行优先的有界队列，复用连接，支持当前会话管道和自定义实例。
- 排序移到后台，保留选择、展开和滚动位置；折叠分支按需重排和淘汰。
- 后台统计采用低 I/O 优先级，达到时间或项目预算后暂停，可点击继续扫描。
- 修复扩展路径、长路径和已有索引计数被覆盖的问题；跳过需召回的云目录并标明部分统计。
- 异步加载 DPI 图标；大型目录提前显示首批条目；读取超时后可重试。
- 20 种语言提示、固定 net462 构建依赖、Windows x86/x64 自动构建与测试。

验证：x86 与 x64 各 29 项回归通过。本机 QuickLook 4.5.0 已完成安装、加载与文件夹试读检查。模拟每次响应延迟 20 ms、同时提交 100 项的场景，从 16/100 成功提升到 100/100，复用一条连接。排序改善的是响应和状态保留，完整刷新耗时未降低。详细数据和设备验证边界见 [VALIDATION.md](https://github.com/Plantforlife/QuickLook.Plugin.FolderViewer.For.Everything/blob/v2.1.0/VALIDATION.md)。

安装：下载 `QuickLook.Plugin.FolderViewer.For.Everything.qlplugin`，在资源管理器选中后按空格，按提示安装并重启 QuickLook。兼容目标仍为 .NET Framework 4.6.2；Everything 为可选加速组件。

---

This release fixes queued folder-size queries timing out before they start, selection loss after sorting, long-path handling, and partial-statistics display. Background scanning, icon work, and retained tree data are now bounded.

- Prioritize visible rows, reuse Everything connections, and support session pipes and named instances.
- Sort in the background while preserving selection and expansion; evict folded branches when the cache exceeds its budget.
- Run statistics with background I/O priority and resumable time/item budgets.
- Preserve indexed counts, handle extended and long paths, and mark skipped cloud/link traversal as partial.
- Load DPI-aware icons asynchronously, show an initial preview batch, and allow retry after read timeouts.
- Include 20-language UI strings, pinned net462 reference assemblies, and Windows x86/x64 CI.

Validation: 29 regression checks pass in each of x86 and x64. A simulated 100-request queue with 20 ms server delays now completes all requests using one connection. Sorting improves responsiveness and state preservation; full refresh time has not decreased. See `VALIDATION.md` for measurements and remaining device-specific checks.

Download the `.qlplugin`, select it in File Explorer, press Space, install, and restart QuickLook. The runtime target remains .NET Framework 4.6.2; Everything is optional.
