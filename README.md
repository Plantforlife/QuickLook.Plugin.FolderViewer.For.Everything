# QuickLook.Plugin.FolderViewer.For.Everything

A fast folder preview plugin for [QuickLook](https://github.com/QL-Win/QuickLook).

> [!IMPORTANT]
> 此项目为 AI 二次开发版本，新增及修改的代码均由 OpenAI 模型通过 Codex（初版为 GPT-5.6 Sol） 生成并迭代完善。
> This is an AI-modified fork. All newly introduced and modified code was generated and iteratively refined by OpenAI models through Codex (initially GPT-5.6 Sol).
> See [AI_MODIFICATION_NOTICE.md](AI_MODIFICATION_NOTICE.md) for the full bilingual disclosure and comparison with the original plugin.

## What changed

- The preview reads only the selected folder. Subfolders are loaded when expanded.
- Native `FindFirstFileEx` enumeration gets names, attributes, sizes, and dates in one pass.
- WPF tree virtualization and a 25,000-item per-folder preview guard keep wide folders responsive.
- File icons load on a bounded background COM worker and are cached by extension and DPI.
- Recursive statistics use Windows background I/O priority and pause after 2 seconds or
  50,000 entries per slice. **Continue scan** starts another slice.
- Statistics skip links, unknown reparse points and offline/recall cloud folders. Hydrated
  cloud directories can be traversed; skipped or inaccessible folders mark totals as partial.
- Network folders never trigger an automatic recursive scan.
- Everything 1.5 indexed size is queried through the SDK3 named-pipe protocol.
- Everything 1.4/1.5 `WM_COPYDATA` IPC supplies indexed size and descendant counts as a fallback.
- Errors, cancellation, cleanup, long paths, GDI resource handling, and sorting collisions are handled.
- Sorting runs off the UI thread, touches expanded branches only, and preserves selection.
- A first batch of up to 128 entries can appear before the selected folder finishes loading.
- Folded branches are evicted above a 75,000-entry cache budget; expanded branches and the
  selected item's ancestors remain protected. Reopening an evicted branch reloads it.

The directory tree always comes from the filesystem. Everything is an optional aggregate-statistics
accelerator, so a stale or incomplete index cannot hide files from the preview.

## Everything acceleration

Everything is optional. For the fastest total size and recursive file/folder counts:

1. Install the full edition of [Everything](https://www.voidtools.com/downloads/). The Lite edition has no IPC support.
2. Keep Everything running and enable IPC.
3. In Everything, open **Tools > Options > Indexes > Size**.
4. Enable **Index file size** and **Index folder size**.

Everything `1.5.0.1384a` or newer uses the fastest named-pipe folder-size query. Older 1.4/1.5
versions use a bounded window-IPC query. If the index is unavailable, local statistics fall back to
a cancellable background scan without delaying the folder tree.

Current-session SDK3 pipes are tried before their legacy names. To select a named Everything
instance, set `FOLDERVIEWER_EVERYTHING_INSTANCE` in QuickLook's environment and restart
QuickLook. For example, the value `Work` selects the instance started with `Everything.exe
-instance Work`. When a pipe responds, the window-IPC fallback uses that same instance.

Folder-row sizes use one query at a time, prioritizing visible rows and newly expanded folders.
The 500 ms request budget starts after waiting for the connection lock. Successful queries
reuse the connection and a bounded cache; an unavailable server backs off for 2 seconds.
Background row-size filling runs for up to 5 seconds after activity and resumes when more
rows become visible or a branch is expanded. Unknown sizes remain blank until queried.

Filesystem preview calls have a 10-second timeout and a **Retry** action. Dedicated worker
pools bound outstanding native calls across closed previews. Windows I/O cancellation is
best effort: an unresponsive driver can retain a worker until its call returns. Automatic
statistics do not scan UNC paths or mapped network drives. Paused scans close enumeration
handles and resume by skipping the already visited prefix; changing folders are not an
atomic snapshot, so reopen the preview after significant filesystem changes.

## Install

Download the `.qlplugin` release, select it in File Explorer, press Space, and restart QuickLook when
prompted. The standard plugin instructions are available in the
[QuickLook wiki](https://github.com/QL-Win/QuickLook/wiki/Available-Plugins#how-to-install-or-upgrade-a-plugin).

## Build and test

Requirements:

- Windows
- Visual Studio / Build Tools with MSBuild and the .NET desktop build tools
- NuGet access on first build (the pinned .NET Framework 4.6.2 reference pack is restored)
- Git submodules initialized

```powershell
git submodule update --init --recursive
$env:FOLDERVIEWER_VERSION = "2.1.0"
msbuild QuickLook.Plugin.FolderViewer.For.Everything.sln -restore /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU"
./Tests/bin/Release/QuickLook.Plugin.FolderViewer.Tests.exe
./Scripts/pack-zip.ps1
Remove-Item Env:FOLDERVIEWER_VERSION
```

QuickLook supplies `QuickLook.Common` at runtime; it must not be included in the `.qlplugin` archive.

The Windows CI workflow runs regression tests in x64 and x86 processes, then creates an
AnyCPU plugin artifact. Run the filesystem, IPC and UI benchmarks locally with:

```powershell
./Tests/bin/Release/QuickLook.Plugin.FolderViewer.Tests.exe --benchmark C:\Windows\System32
```

The runtime target remains .NET Framework 4.6.2 / C# 7.3. See
[VALIDATION.md](VALIDATION.md) for verification results and remaining device-specific checks.

## License

See `LICENSE.txt`, the per-file notices retained from the original QuickLook sources, and
`THIRD_PARTY_NOTICES.txt` for the Everything SDK protocol definitions.
