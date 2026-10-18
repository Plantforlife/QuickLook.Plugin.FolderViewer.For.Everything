# 性能、兼容性与交互验证

[简体中文](#简体中文) | [English](#english)

## 简体中文

更新日期：2026-10-19。当前验证版本：2.1.1，基于 2.1.0（`d9d459d`）。运行目标保持 .NET Framework 4.6.2，语言版本保持 C# 7.3。

### 2.1.1 交互验证

Release x86 与 Release x64 各 **32/32 项本机回归检查通过**。构建使用 Visual Studio MSBuild 18 和固定的 `Microsoft.NETFramework.ReferenceAssemblies.net462 1.0.3` 引用程序集。

新增三组检查覆盖：双击展开/收起、Ctrl＋双击打开、嵌套文件夹不误触父项、文件双击保持原行为，以及已释放预览不能继续打开路径。

显示与滚动检查确认选中背景为 4px 圆角、关闭延迟滚动、视口可以立即移动，横向偏移同步到表头；25,000 行时实际生成的容器仍少于 200 个。

菜单检查确认深色前景/背景可以传递、两项菜单的测量高度小于 150 DIP、宽度不超过 360 DIP，重新加载与复制路径各转发一次；释放后恢复宿主原菜单及原有项目。

AnyCPU 安装包已生成，且四个安装文件与包内内容的 SHA-256 一致。本机 QuickLook 4.5.0 已重启，Windows Restart Manager 确认进程加载新版 DLL，检查时异常日志未增加。旧版 2.1.0 已备份到插件搜索目录之外。

完整视觉与键鼠验收尚未完成。交互逻辑与布局的自动检查通过，不代表所有主题、DPI、输入设备和宿主版本均已实机验收。菜单兼容层在无法识别宿主控件时保留原菜单。

本轮原始日志位于本地忽略目录 `artifacts/`：`interaction-2.1.1-tests-x86.txt`、`interaction-2.1.1-tests-x64.txt` 和 `install-2.1.1.json`。GitHub CI 的实际状态以对应提交的工作流记录为准。

### 保留的性能与兼容性改进

**大小查询：** 单查询队列优先处理可见行，最多保留 128 个优先项和 32 个惰性批次；500 ms 通信预算在取得连接锁后开始，关闭预览后取消查询。

**IPC：** 复用连接，缓存成功端点和最多 2,048 个结果，离线时退避。支持当前会话与自定义实例，同一实例提供大小和数量；缺失管道立即跳过，忙管道有限重试。

**排序：** 后台按稳定键快照排序，只更新受影响的展开层级，合并大小通知，丢弃过时结果，并保留选择、展开和滚动锚点。

**统计：** 专用线程使用 Windows 后台资源优先级，每轮达到 2 秒或 50,000 项后暂停。仅用于统计的文件条目不构造完整路径、不转换日期；网络路径不自动启动递归扫描。

**路径：** 区分扩展本地路径、UNC 和映射网络盘，保留已有索引数量，原生长路径处理覆盖插件入口。递归统计跳过链接、未知标签和离线/需召回云目录，并标明部分结果。

**图标：** Shell 查询在后台 COM 线程运行，按类型和 DPI 缓存，最多 512 个缓存项、128 个排队项；回填前检查控件是否已复用。

**树缓存：** 75,000 项预算触发折叠分支淘汰，保护展开分支及选择路径。重新展开时重新读取，同时及时清理已折叠分支的查询快照。

**慢文件系统：** 首批最多 128 项提前显示；读取 10 秒超时后可重试。进程内最多同时使用 2 个枚举、1 个扫描、2 个 IPC 工作线程，线程按需创建。

每层最多显示 25,000 项。后台行大小填充在活动后运行最多 5 秒，新行可见或目录展开时再次唤醒；未知大小保持空白。

### 2.1.0 历史验证

2026-10-18 的 2.1.0 在 x86、x64 中各通过 29 项回归检查，覆盖队列、连接复用、取消/超时、扫描续跑、扩展与长路径、云标签、会话/实例、排序状态、缓存淘汰、过时回调和 DPI 图标。本机 Everything 1.5 的实际大小/数量查询通过；没有 Everything 时仅跳过可选实机集成项，模拟协议测试仍运行。

20 种语言的键、占位符和文化回退通过检查，中文与德文完成 800×400 离屏布局渲染。插件包检查确认只含 DLL、元数据、翻译和第三方声明，不包含 QuickLook.Common 或测试程序。

### 2.1.0 历史性能测量

以下数据来自 **x86** 测试进程，其 `C:\Windows\System32` 访问被重定向至 SysWOW64，共 3,026 项。x64 读取到 5,055 项，不能直接比较。测量并非磁盘冷缓存测试，且未针对 2.1.1 重新测量。

| 场景 | 初次审查 | 2.1.0 |
| --- | --- | --- |
| 首层读取与排序，预热后五次 | 5.33–6.44 ms | 4.87–5.91 ms |
| Everything 汇总，进程内首次查询 | 268.75 ms | 159.53 ms |
| Everything 汇总，随后两次 | 15.21 / 13.48 ms | 14.58 / 14.91 ms |
| 服务每次延迟 20 ms，同时提交 100 项 | 16 成功、84 不可用；528 ms 提前结束 | 100 成功、1 条连接；3,148.63 ms 全部完成 |
| 25,000 行排序，完整完成与布局 | 41–49 ms；丢失选择 | 76.90–78.84 ms；保留选择和锚点 |
| 排序入口同步调用 | — | 首次 3.34 ms，随后 0.76 / 0.35 ms |
| 实际生成的容器数 | 16 | 17 |

队列测试衡量可靠性与连接开销。528 ms 对应大量请求失败后的提前结束，不能当成全部查询完成时间。后台排序改善响应和状态保留，但完整刷新耗时增加；入口调用时间也不等于 UI 线程累计工作时间，结果回填和布局仍在 UI 线程执行。

本地历史记录位于忽略目录 `artifacts/`：`build-x86.txt`、`build-x64.txt`、`tests-x86.txt`、`tests-x64.txt`、`benchmark-x86.txt`、`preview-zh.png` 和 `preview-de.png`。构建与基准命令见 [README](README.md)，离屏布局可另行生成：

```powershell
./Tests/bin/Release/QuickLook.Plugin.FolderViewer.Tests.exe --render ./artifacts/preview-zh.png zh-CN
```

### 验证边界

x86/x64 结果来自测试进程，不能等同于两种 QuickLook 宿主都已实测。真实多用户会话、自定义 Everything 实例、OneDrive 召回、故障 NAS、拔盘、映射盘变化、exFAT、ARM64 和多显示器 DPI 切换仍需要对应设备验收。

Windows I/O 取消属于尽力而为，无法强制终止所有驱动调用。线程与队列有上限，系统调用不返回时仍可能暂时耗尽工作线程。QuickLook 的同步 `CanHandle` 接口仍需进行一次目录属性探测。

缓存预算不会限制整个进程内存，保护展开分支和选择路径时可能超过 75,000 项。扫描不是文件系统快照；暂停时关闭句柄，续跑时跳过此前位置，目录显著变化后应重新打开预览。

Everything 索引可能滞后。文件树来自文件系统，汇总统计仍取决于索引覆盖和更新情况；云属性策略的模拟测试也不能替代真实云服务提供商的实测。

### 接口依据

相关实现参考 [Everything SDK3](https://github.com/voidtools/everything_sdk3/blob/master/src/Everything3.c)、[会话管道说明](https://www.voidtools.com/forum/viewtopic.php?p=72889)、[Windows 后台线程模式](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setthreadpriority)、[Shell 图标线程要求](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow) 和 [WaitNamedPipe 行为](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-waitnamedpipew)。

---

## English

*Performance, compatibility, and interaction validation*

Updated: 2026-10-19. Current validation version: 2.1.1, based on 2.1.0 (`d9d459d`). The runtime target remains .NET Framework 4.6.2, with C# 7.3.

### 2.1.1 interaction validation

All **32/32 local regression checks passed** in both Release x86 and Release x64. Builds used Visual Studio MSBuild 18 and pinned `Microsoft.NETFramework.ReferenceAssemblies.net462 1.0.3` reference assemblies.

Three new groups cover double-click expansion/collapse, Ctrl + double-click opening, nested folders leaving their parents unchanged, unchanged file opening, and prevention of path activation after disposal.

Display and scrolling checks confirm 4px selection corners, disabled deferred scrolling, immediate viewport movement, and horizontal header synchronization. With 25,000 rows, fewer than 200 item containers are realized.

Menu checks confirm dark foreground/background propagation, a two-action menu measuring under 150 DIPs high and no more than 360 DIPs wide, and one forwarded invocation for each action. Disposal restores the original host menu and its items.

The AnyCPU package was generated, and SHA-256 checks matched all four installed files to the archive. Local QuickLook 4.5.0 was restarted; Windows Restart Manager confirmed the new DLL was loaded, and the exception log had not grown at the time of inspection. Version 2.1.0 was backed up outside the plugin search directory.

Full visual and keyboard/mouse acceptance remains incomplete. Passing automated interaction and layout checks does not establish device-level acceptance for every theme, DPI, input device, or host version. If the menu compatibility layer cannot recognize the host controls, it leaves the original menu in place.

Raw local records are kept in the ignored `artifacts/` directory: `interaction-2.1.1-tests-x86.txt`, `interaction-2.1.1-tests-x64.txt`, and `install-2.1.1.json`. GitHub CI status is determined by the workflow run for the corresponding commit.

### Retained performance and compatibility improvements

**Size queries:** One query queue prioritizes visible rows and retains at most 128 priority entries and 32 lazy batches. The 500 ms communication budget starts after acquiring the connection lock; closing the preview cancels its queries.

**IPC:** Connections are reused, successful endpoints and up to 2,048 results are cached, and unavailable services trigger backoff. Current-session and named instances supply both sizes and counts. Missing pipes are skipped immediately, and busy pipes receive bounded retries.

**Sorting:** Background sorting uses stable key snapshots, updates only affected expanded levels, coalesces size notifications, discards stale results, and preserves selection, expansion, and scroll anchors.

**Statistics:** Dedicated threads use Windows background resource priority and pause after 2 seconds or 50,000 entries per slice. Statistics-only file entries avoid full-path construction and date conversion. Network paths do not trigger automatic recursive scans.

**Paths:** Extended local paths, UNC paths, and mapped network drives are distinguished. Existing indexed counts are preserved, and native long-path handling covers the plugin entry point. Recursive statistics skip links, unknown tags, and offline/recall cloud folders, marking partial results.

**Icons:** Shell queries run on background COM workers, with at most 512 cached entries and 128 queued entries keyed by type and DPI. Results are applied only if the control still represents the same entry.

**Tree cache:** A 75,000-entry budget triggers eviction of folded branches while protecting expanded branches and the selection path. Reopening an evicted branch reloads it, and obsolete query snapshots are pruned.

**Slow filesystems:** An initial batch of up to 128 entries can appear early, and reads can be retried after a 10-second timeout. The process uses at most 2 enumeration, 1 scan, and 2 IPC workers, created on demand.

Each level displays up to 25,000 entries. Background row-size filling runs for up to 5 seconds after activity and resumes when more rows become visible or a folder expands. Unknown sizes remain blank.

### Historical 2.1.0 validation

On 2026-10-18, version 2.1.0 passed 29 checks in each of x86 and x64, covering queues, connection reuse, cancellation/timeouts, scan resumption, extended/long paths, cloud tags, sessions/instances, sorting state, cache eviction, stale callbacks, and DPI icons. Real Everything 1.5 size/count queries passed locally. Without Everything, only the optional live integration check is skipped; simulated protocol tests still run.

Keys, placeholders, and culture fallback passed checks for all 20 languages. Chinese and German layouts were rendered offscreen at 800×400. Packaging checks confirmed only the DLL, metadata, translations, and third-party notices were included, excluding QuickLook.Common and test programs.

### Historical 2.1.0 performance measurements

The following data comes from **x86** test processes, whose `C:\Windows\System32` access was redirected to SysWOW64, containing 3,026 entries. The x64 process read 5,055 entries and is not directly comparable. These were not cold-disk-cache measurements and were not repeated for 2.1.1.

| Scenario | Initial review | 2.1.0 |
| --- | --- | --- |
| First-level enumeration and sort, five warmed runs | 5.33–6.44 ms | 4.87–5.91 ms |
| Everything aggregate, first query in process | 268.75 ms | 159.53 ms |
| Everything aggregate, next two queries | 15.21 / 13.48 ms | 14.58 / 14.91 ms |
| 100 simultaneous requests, 20 ms server delay each | 16 successes, 84 unavailable; ended early at 528 ms | 100 successes, 1 connection; all complete in 3,148.63 ms |
| 25,000-row sort, full completion and layout | 41–49 ms; selection lost | 76.90–78.84 ms; selection and anchor retained |
| Synchronous sort entry call | — | First 3.34 ms, then 0.76 / 0.35 ms |
| Realized item containers | 16 | 17 |

The queue test measures reliability and connection overhead. The 528 ms result ended early after many failures and is not the completion time for all requests. Background sorting improves responsiveness and state preservation but increases full refresh time. Entry-call latency is not total UI-thread work: applying results and layout still run on that thread.

Historical local records are in the ignored `artifacts/` directory: `build-x86.txt`, `build-x64.txt`, `tests-x86.txt`, `tests-x64.txt`, `benchmark-x86.txt`, `preview-zh.png`, and `preview-de.png`. Build and benchmark commands are in the [README](README.md#english). Generate an offscreen layout with:

```powershell
./Tests/bin/Release/QuickLook.Plugin.FolderViewer.Tests.exe --render ./artifacts/preview-zh.png zh-CN
```

### Validation boundaries

The x86/x64 results come from test processes and do not establish live testing of both QuickLook host architectures. Real multi-user sessions, named Everything instances, OneDrive recall, failing NAS devices, drive removal, mapped-drive changes, exFAT, ARM64, and multi-monitor DPI changes still need appropriate device-level acceptance.

Windows I/O cancellation is best effort and cannot forcibly end every driver call. Threads and queues are bounded, but unreturned system calls can temporarily exhaust workers. QuickLook's synchronous `CanHandle` interface still requires one directory-attribute probe.

The cache budget does not cap process memory, and protected expanded branches or selection paths can exceed 75,000 entries. Scanning is not a filesystem snapshot: pausing closes handles and resuming skips the previously visited prefix. Reopen the preview after significant directory changes.

The Everything index may lag behind filesystem changes. The tree reads the filesystem, while aggregate statistics depend on index coverage and freshness. Simulated cloud-attribute tests also do not replace testing with a real cloud provider.

### Interface references

Implementation references include the [Everything SDK3](https://github.com/voidtools/everything_sdk3/blob/master/src/Everything3.c), [session-pipe description](https://www.voidtools.com/forum/viewtopic.php?p=72889), [Windows background thread mode](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setthreadpriority), [Shell icon threading requirements](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow), and [WaitNamedPipe behavior](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-waitnamedpipew).
