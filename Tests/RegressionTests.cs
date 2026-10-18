using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;

namespace QuickLook.Plugin.FolderViewer.Tests
{
    internal static partial class Program
    {
        private static void TestPathPolicy()
        {
            Assert(!PathPolicy.IsNetwork(@"\\?\C:\Windows"), "Extended local paths must remain local.");
            Assert(PathPolicy.IsNetwork(@"\\?\UNC\server\share\folder"), "Extended UNC must remain remote.");
            Assert(PathPolicy.IsNetwork(@"\\server\share\folder"), "UNC must remain remote.");
            AssertEqual(@"C:\Windows", PathPolicy.ForQuery(@"\\?\C:\Windows"));
            AssertEqual(@"\\server\share", PathPolicy.ForQuery(@"\\?\UNC\server\share"));
            Assert(PathPolicy.CanScanDirectory(0x10, 0), "Normal directories should be scanned.");
            Assert(PathPolicy.CanScanDirectory(0x410, 0x9000301a), "Hydrated cloud directories should be scanned.");
            Assert(!PathPolicy.CanScanDirectory(0x1410, 0x9000301a), "Offline cloud folders must not hydrate.");
            Assert(!PathPolicy.CanScanDirectory(0x410, 0xa000000c), "Symbolic links must remain boundaries.");
            Assert(!PathPolicy.CanScanDirectory(0x410, 0xa0000003), "Junctions must remain boundaries.");
        }

        private static void TestScanResume()
        {
            WithTemporaryDirectory(path =>
            {
                for (var i = 0; i < 7; i++)
                {
                    var folder = Directory.CreateDirectory(Path.Combine(path, "dir" + i));
                    for (var j = 0; j < 11; j++) File.WriteAllBytes(Path.Combine(folder.FullName, "file" + j), new byte[3]);
                }
                var session = new DirectoryStatisticsScanner.Session(path);
                DirectoryStatistics result;
                var slices = 0;
                do
                {
                    result = session.Step(CancellationToken.None, TimeSpan.FromSeconds(1), 5);
                    Assert(++slices < 100, "A resumed scan must make progress.");
                } while (!result.IsComplete);
                Assert(slices > 1, "The budget must pause the scan.");
                AssertEqual(7L, result.DirectoryCount);
                AssertEqual(77L, result.FileCount);
                AssertEqual(231L, result.TotalSize);
            });
        }

        private static void TestStatisticsEntries()
        {
            WithTemporaryDirectory(path =>
            {
                File.WriteAllBytes(Path.Combine(path, "file"), new byte[5]);
                Directory.CreateDirectory(Path.Combine(path, "folder"));
                DirectoryEnumerator.Enumerate(path, CancellationToken.None, 10, item =>
                {
                    Assert(item.ModifiedDate == DateTime.MinValue, "Statistics must not convert timestamps.");
                    Assert(item.IsDirectory ? item.FullPath != null : item.FullPath == null,
                        "Only directory traversal needs full paths.");
                    return true;
                }, statisticsOnly: true);
            });
        }

        private static void TestEndpoints()
        {
            var defaults = EverythingEndpoint.Discover(null, 7);
            AssertEqual("Everything IPC Session 7", defaults[0].Pipe);
            AssertEqual("", defaults[0].Instance);
            Assert(defaults.Any(e => e.Pipe == "Everything IPC Session 7 (1.5a)"), "Alpha sessions must be supported.");
            var custom = EverythingEndpoint.Discover(@"index:one\%", 12);
            AssertEqual(2, custom.Length);
            AssertEqual("Everything IPC Session 12 (index%3aone%5c%25)", custom[0].Pipe);
            AssertEqual(@"index:one\%", custom[0].Instance);
        }

        private static void TestQueuedQueries()
        {
            using (var server = new SizeServer(10))
            using (var provider = new EverythingFolderSizeProvider(server.Name))
            {
                var tasks = Enumerable.Range(0, 100)
                    .Select(i => provider.QueryAsync(@"C:\mock-" + i, CancellationToken.None)).ToArray();
                Assert(Task.WaitAll(tasks, 10000), "Queries must eventually drain.");
                AssertEqual(100, tasks.Count(t => t.Result.IsSuccess && t.Result.Size == 1234));
                AssertEqual(1, server.Connections);
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    AssertThrows<OperationCanceledException>(() => provider.QueryAsync(@"C:\cancel", cancellation.Token).GetAwaiter().GetResult());
                }
            }
        }

        private static void WithDispatcher(Action action)
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private static void PumpUntil(Func<bool> done, int milliseconds = 10000)
        {
            var elapsed = Stopwatch.StartNew();
            while (!done())
            {
                Assert(elapsed.ElapsedMilliseconds < milliseconds, "Dispatcher operation timed out.");
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(1);
            }
        }

        private static void TestSizeQueue()
        {
            var entries = Enumerable.Range(0, 25000).Select(i => Entry("folder" + i, true)).ToArray();
            var queried = new List<string>();
            var active = 0;
            var maximum = 0;
            using (var queue = new FolderSizeQueue(async (path, token) =>
            {
                maximum = Math.Max(maximum, ++active);
                queried.Add(path);
                try { await Task.Delay(2, token); return new FolderSizeQueryResult(true, 1, "mock"); }
                finally { active--; }
            }, (entry, size) => entry.SetIndexedSize(size), Task.CompletedTask, CancellationToken.None))
            {
                queue.AddBatch(entries);
                AssertEqual(1, queried.Count);
                for (var i = 100; i < 1100; i++) queue.Prioritize(entries[i]);
                queue.Prioritize(entries[24999]);
                Assert(queue.PendingPriorityCount <= 128, "Visible requests must be bounded.");
                PumpUntil(() => entries[24999].Size.HasValue);
                AssertEqual(1, maximum);
                Assert(queried.Count < 200, "Visible requests must precede the background tail.");
            }
            PumpUntil(() => active == 0);
            var ready = new TaskCompletionSource<bool>();
            var owner = Entry("folded", true);
            owner.TryBeginLoading();
            owner.CompleteLoading(entries.Take(10).ToArray());
            owner.IsExpanded = true;
            var obsoleteQueries = 0;
            using (var queue = new FolderSizeQueue((path, token) =>
            { obsoleteQueries++; return Task.FromResult(FolderSizeQueryResult.Unavailable); },
                (entry, size) => { }, ready.Task, CancellationToken.None))
            {
                queue.AddBatch(owner.Children, owner);
                queue.Prioritize(owner.Children[3]);
                owner.IsExpanded = false;
                queue.Prune();
                ready.SetResult(true);
                var drained = false;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => drained = true));
                PumpUntil(() => drained);
                AssertEqual(0, obsoleteQueries);
            }
        }

        private static void Layout(FileListView view)
        {
            view.Measure(new Size(800, 400));
            view.Arrange(new Rect(0, 0, 800, 400));
            view.UpdateLayout();
        }

        private static void TestAsyncSorting()
        {
            using (var view = new FileListView())
            {
                var items = Enumerable.Range(0, 25000).Select(i => Entry("item" + i.ToString("D5"), false)).ToArray();
                view.SetSortedItems(items, view.CurrentSorter);
                Layout(view);
                var tree = (TreeView)view.FindName("treeView");
                var selected = items[3];
                ((TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(selected)).IsSelected = true;
                view.ChangeSort(SortColumn.Name, true);
                PumpUntil(() => view.SortCompletion.IsCompleted);
                view.SortCompletion.GetAwaiter().GetResult();
                Layout(view);
                Assert(selected.IsSelected, "The selected model must survive sorting.");
                Assert(ReferenceEquals(selected, tree.SelectedItem), "TreeView selection must survive sorting.");
                AssertEqual(items[24999], (FileEntry)tree.Items[0]);
                view.SetSortedItems(items, view.CurrentSorter);
                Assert(ReferenceEquals(selected, tree.SelectedItem), "Final preview replacement must retain selection.");

                var collapsed = Entry("folder", true);
                collapsed.TryBeginLoading();
                collapsed.CompleteLoading(items.Take(100).ToArray());
                var before = collapsed.Children;
                view.SetSortedItems(new[] { collapsed }, view.CurrentSorter);
                view.ChangeSort(SortColumn.Size, false);
                PumpUntil(() => view.SortCompletion.IsCompleted);
                Assert(ReferenceEquals(before, collapsed.Children), "Collapsed subtrees must not be eagerly sorted.");
            }
        }

        private static void TestTreeCache()
        {
            using (var view = new FileListView())
            {
                var roots = Enumerable.Range(0, 10).Select(i => Entry("folder" + i, true)).ToArray();
                foreach (var root in roots)
                {
                    root.TryBeginLoading();
                    root.CompleteLoading(Enumerable.Range(0, 100).Select(i => Entry("file" + i, false)).ToArray());
                }
                view.SetSortedItems(roots, view.CurrentSorter);
                view.TrimCache(250);
                Assert(roots.Sum(r => r.IsLoaded ? r.Children.Count : 0) + roots.Length <= 250, "Folded data must respect the budget.");
                var evicted = roots.First(r => !r.IsLoaded);
                Assert(evicted.TryBeginLoading(), "An evicted branch must be reloadable.");
                evicted.CompleteLoading(new[] { Entry("new", false) });
                AssertEqual("new", evicted.Children[0].Name);
            }
        }

        private static void TestPartialNetworkStatistics()
        {
            WithTemporaryDirectory(path =>
            {
                using (var panel = new FolderInfoPanel(path))
                {
                    typeof(FolderInfoPanel).GetField("_indexedCounts", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, true);
                    var folders = (TextBlock)panel.FindName("numFolders");
                    var files = (TextBlock)panel.FindName("numFiles");
                    folders.Text = "folders 7";
                    files.Text = "files 77";
                    panel.MarkNetworkStatisticsUnavailable();
                    AssertEqual("folders 7", folders.Text);
                    AssertEqual("files 77", files.Text);
                }
            });
        }

        private static void TestIoIsolation()
        {
            var worker = new IoWorker(1, 1, "FolderViewer isolation test");
            using (var release = new ManualResetEventSlim())
            using (var started = new ManualResetEventSlim())
            {
                var first = worker.RunAsync(token => { started.Set(); release.Wait(); return 1; },
                    CancellationToken.None, TimeSpan.FromMilliseconds(100));
                Assert(started.Wait(1000), "Worker should start.");
                var second = worker.RunAsync(token => 2, CancellationToken.None, TimeSpan.FromSeconds(2));
                var third = worker.RunAsync(token => 3, CancellationToken.None, TimeSpan.FromSeconds(2));
                try
                {
                    AssertThrows<InvalidOperationException>(() => third.GetAwaiter().GetResult());
                    AssertThrows<TimeoutException>(() => first.GetAwaiter().GetResult());
                    Assert(!second.IsCompleted, "A stuck call must not spawn an unbounded replacement worker.");
                }
                finally { release.Set(); }
                AssertEqual(2, second.GetAwaiter().GetResult());
            }
        }

        private static void TestAsyncIcons()
        {
            var small = SystemIconProvider.GetIconAsync("file.txt", false, 16);
            var large = SystemIconProvider.GetIconAsync("file.txt", false, 32);
            Assert(Task.WaitAll(new Task[] { small, large }, 5000), "Icon worker should complete.");
            Assert(small.Result.IsFrozen && large.Result.IsFrozen, "Images must cross threads safely.");
            Assert(!ReferenceEquals(small, large), "DPI sizes must have separate cache keys.");
            Assert(ReferenceEquals(small, SystemIconProvider.GetIconAsync("other.txt", false, 16)), "Same type/DPI must reuse the cache.");
        }

        private static void TestLongPaths()
        {
            WithTemporaryDirectory(root =>
            {
                var first = @"\\?\" + root + "\\" + new string('a', 120);
                var second = first + "\\" + new string('b', 120);
                var third = second + "\\nested";
                try
                {
                    Assert(CreateDirectory(first, IntPtr.Zero), "First directory should be created.");
                    Assert(CreateDirectory(second, IntPtr.Zero), "Long directory should be created.");
                    Assert(CreateDirectory(third, IntPtr.Zero), "Long descendant should be created.");
                    var entries = DirectoryEnumerator.ReadForPreview(second.Substring(4), CancellationToken.None).Entries;
                    AssertEqual(1, entries.Count);
                    AssertEqual("nested", entries[0].Name);
                    Assert(new Plugin().CanHandle(second.Substring(4)), "QuickLook's entry point must also accept long directories.");
                    AssertEqual(1L, DirectoryStatisticsScanner.Scan(second, CancellationToken.None).DirectoryCount);
                }
                finally { RemoveDirectory(third); RemoveDirectory(second); RemoveDirectory(first); }
            });
        }

        private static void TestFirstBatch()
        {
            WithTemporaryDirectory(path =>
            {
                for (var i = 0; i < 256; i++) File.WriteAllText(Path.Combine(path, "entry" + i), "");
                IReadOnlyList<FileEntry> batch = null;
                var callbacks = 0;
                var result = DirectoryEnumerator.ReadForPreview(path, CancellationToken.None,
                    firstBatch: entries => { callbacks++; batch = entries; });
                AssertEqual(1, callbacks);
                Assert(batch.Count > 0 && batch.Count <= 128, "The initial batch must be bounded.");
                Assert(batch.All(result.Entries.Contains), "The final list must reuse initial models and their UI state.");
                AssertEqual(256, result.Entries.Count);
            });
        }

        private static void TestNestedSorting()
        {
            using (var view = new FileListView())
            {
                var parent = Entry("parent", true);
                var children = Enumerable.Range(0, 1000).Select(i => Entry("nested" + i.ToString("D4"), false)).ToArray();
                parent.TryBeginLoading();
                parent.CompleteLoading(children);
                parent.IsExpanded = true;
                view.SetSortedItems(new[] { parent, Entry("other", true) }, view.CurrentSorter);
                Layout(view);
                var tree = (TreeView)view.FindName("treeView");
                var container = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(parent);
                ((TreeViewItem)container.ItemContainerGenerator.ContainerFromItem(children[2])).IsSelected = true;
                view.ChangeSort(SortColumn.Size, true);
                view.ChangeSort(SortColumn.Name, true);
                view.ChangeSort(SortColumn.Name, false);
                PumpUntil(() => view.SortCompletion.IsCompleted);
                view.SortCompletion.GetAwaiter().GetResult();
                Assert(parent.IsExpanded, "Expanded branches must survive sorting.");
                Assert(ReferenceEquals(children[2], tree.SelectedItem), "Nested selection must survive root and child sorts.");
                AssertEqual("nested0000", parent.Children[0].Name);
                view.ChangeSort(SortColumn.Name, true);
                view.Dispose();
                PumpUntil(() => view.SortCompletion.IsCompleted);
                view.SortCompletion.GetAwaiter().GetResult();
            }
        }

        private static void TestTranslations()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Translations.config");
            var root = XDocument.Load(path).Root;
            var keys = root.Element("en").Elements().Select(e => e.Name.LocalName).ToArray();
            AssertEqual(20, root.Elements().Count());
            foreach (var language in root.Elements())
            {
                AssertEqual(keys.Length, language.Elements().Count());
                foreach (var key in keys)
                {
                    var text = language.Element(key)?.Value;
                    Assert(!string.IsNullOrWhiteSpace(text), language.Name + ": missing " + key);
                    Assert(text.Contains("{0}") == root.Element("en").Element(key).Value.Contains("{0}"), "Placeholders must match.");
                    string.Format(CultureInfo.InvariantCulture, text, 123);
                }
            }
            AssertEqual("名称", Strings.Table.Load(path, CultureInfo.GetCultureInfo("zh-CN")).Get("Header_Name"));
            AssertEqual("名稱", Strings.Table.Load(path, CultureInfo.GetCultureInfo("zh-TW")).Get("Header_Name"));
            AssertEqual("Nom", Strings.Table.Load(path, CultureInfo.GetCultureInfo("fr-CA")).Get("Header_Name"));
            AssertEqual("Name", Strings.Table.Load(path, CultureInfo.GetCultureInfo("fi-FI")).Get("Header_Name"));
        }

        private static void TestStaleStatistics()
        {
            WithTemporaryDirectory(path =>
            {
                using (var panel = new FolderInfoPanel(path))
                {
                    var type = typeof(FolderInfoPanel);
                    var status = (TextBlock)panel.FindName("statisticsStatus");
                    type.GetField("_statisticsStarted", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, true);
                    var indexed = (Task)type.GetField("_indexedStatisticsTask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                    PumpUntil(() => indexed.IsCompleted && ((FrameworkElement)panel.FindName("rootLoading")).Visibility == Visibility.Collapsed);
                    type.GetField("_scanGeneration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, 2);
                    type.GetMethod("QueueStatisticsUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel,
                        new object[] { new DirectoryStatistics(10, 20, 30, 0, false), 1 });
                    status.Text = "paused";
                    var drained = false;
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => drained = true));
                    PumpUntil(() => drained);
                    AssertEqual("paused", status.Text);
                }
            });
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateDirectory(string path, IntPtr securityAttributes);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RemoveDirectory(string path);

        private sealed class SizeServer : IDisposable
        {
            private readonly NamedPipeServerStream _server;
            private readonly Task _worker;
            private volatile bool _disposed;
            public string Name { get; } = "FolderViewer.Regression." + Guid.NewGuid().ToString("N");
            public int Connections;
            public SizeServer(int delay)
            {
                _server = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                _worker = Task.Run(() =>
                {
                    try
                    {
                        while (!_disposed)
                        {
                            _server.WaitForConnection();
                            Interlocked.Increment(ref Connections);
                            try
                            {
                                while (!_disposed)
                                {
                                    var header = ReadExactly(_server, 8);
                                    ReadExactly(_server, BitConverter.ToInt32(header, 4));
                                    Thread.Sleep(delay);
                                    var reply = BitConverter.GetBytes((uint)200).Concat(BitConverter.GetBytes((uint)8))
                                        .Concat(BitConverter.GetBytes((long)1234)).ToArray();
                                    _server.Write(reply, 0, reply.Length);
                                    _server.Flush();
                                }
                            }
                            catch (IOException) { if (!_disposed) _server.Disconnect(); }
                        }
                    }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                });
            }
            public void Dispose() { _disposed = true; _server.Dispose(); _worker.Wait(2000); }
        }
    }
}
