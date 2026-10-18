using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuickLook.Common.Plugin;

namespace QuickLook.Plugin.FolderViewer.Tests
{
    internal static partial class Program
    {
        private static int _failed;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 0 && args[0] == "--benchmark")
                return RunBenchmarks(args.Length > 1 ? args[1] : Environment.SystemDirectory);
            if (args.Length > 1 && args[0] == "--render")
            {
                if (args.Length > 2) Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(args[2]);
                WithDispatcher(() => RenderPreview(args[1]));
                return 0;
            }

            Run("byte size formatting", TestByteSizeFormatting);
            Run("entry ordering and load gate", TestEntryOrderingAndLoadGate);
            Run("preview enumeration and limit", TestPreviewEnumerationAndLimit);
            Run("volume root normalization", TestVolumeRootNormalization);
            Run("recursive statistics", TestRecursiveStatistics);
            Run("pre-cancelled scan", TestCancelledScan);
            Run("Everything pipe protocol", TestEverythingPipeProtocol);
            Run("Everything pipe malformed response", TestEverythingPipeMalformedResponse);
            Run("Everything pipe truncated response", TestEverythingPipeTruncatedResponse);
            Run("Everything pipe cancellation", TestEverythingPipeCancellation);
            Run("Everything query deadline", TestEverythingQueryDeadline);
            Run("plugin lifecycle", TestPluginLifecycle);
            Run("Everything IPC (optional)", TestEverythingIpc);
            Run("extended and UNC path policy", TestPathPolicy);
            Run("scan budget and resume", TestScanResume);
            Run("lightweight statistics entries", TestStatisticsEntries);
            Run("Everything sessions and instances", TestEndpoints);
            Run("queued queries and reusable connection", TestQueuedQueries);
            Run("bounded size queue and priority", () => WithDispatcher(TestSizeQueue));
            Run("sort selection and collapsed branches", () => WithDispatcher(TestAsyncSorting));
            Run("tree cache budget and reload", () => WithDispatcher(TestTreeCache));
            Run("network partial statistics", TestPartialNetworkStatistics);
            Run("bounded I/O and timeout", TestIoIsolation);
            Run("asynchronous DPI icons", TestAsyncIcons);
            Run("long paths under legacy CLR switches", TestLongPaths);
            Run("first preview batch", TestFirstBatch);
            Run("nested selection and overlapping sorts", () => WithDispatcher(TestNestedSorting));
            Run("translations and culture fallback", TestTranslations);
            Run("stale statistics callbacks", () => WithDispatcher(TestStaleStatistics));

            Console.WriteLine();
            Console.WriteLine(_failed == 0 ? "All tests passed." : _failed + " test(s) failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static int RunBenchmarks(string path)
        {
            Console.WriteLine("Benchmark path: " + path);
            DirectoryEnumerator.ReadForPreview(path, CancellationToken.None);

            for (var run = 1; run <= 5; run++)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = DirectoryEnumerator.ReadForPreview(path, CancellationToken.None);
                stopwatch.Stop();
                Console.WriteLine(
                    "Filesystem level {0}: {1:N0} rows in {2:N2} ms",
                    run,
                    result.Entries.Count,
                    stopwatch.Elapsed.TotalMilliseconds);
            }

            using (var provider = new EverythingFolderSizeProvider())
            {
                for (var run = 1; run <= 3; run++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    var result = provider.QueryStatisticsAsync(path, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    stopwatch.Stop();
                    Console.WriteLine(
                        "Everything aggregate {0}: size={1}, dirs={2:N0}, files={3:N0}, {4:N2} ms",
                        run,
                        result.HasSize ? ByteSizeFormatter.Format(result.Size, CultureInfo.InvariantCulture) : "n/a",
                        result.DirectoryCount,
                        result.FileCount,
                        stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            RunQueueBenchmark();
            WithDispatcher(RunUiBenchmark);
            return 0;
        }

        private static void TestByteSizeFormatting()
        {
            var culture = CultureInfo.InvariantCulture;
            AssertEqual("0 B", ByteSizeFormatter.Format(0, culture));
            AssertEqual("1.00 KB", ByteSizeFormatter.Format(1024, culture));
            AssertEqual("1.50 KB", ByteSizeFormatter.Format(1536, culture));
            AssertEqual("1.00 GB", ByteSizeFormatter.Format(1024L * 1024 * 1024, culture));
        }

        private static void TestEntryOrderingAndLoadGate()
        {
            var entries = new List<FileEntry>
            {
                Entry("z.txt", false),
                Entry("folder", true),
                Entry("e\u0301.txt", false),
                Entry("é.txt", false)
            };
            entries.Sort();

            Assert(entries[0].IsFolder, "Folders must sort before files.");
            AssertEqual(4, entries.Count);
            Assert(entries[0].TryBeginLoading(), "The first load must acquire the gate.");
            Assert(!entries[0].TryBeginLoading(), "Concurrent loading must be rejected.");
            entries[0].CompleteLoading(Array.Empty<FileEntry>());
            AssertEqual(0, entries[0].Children.Count);
        }

        private static void TestPreviewEnumerationAndLimit()
        {
            WithTemporaryDirectory(path =>
            {
                Directory.CreateDirectory(Path.Combine(path, "a-folder"));
                File.WriteAllBytes(Path.Combine(path, "b.bin"), new byte[17]);
                File.WriteAllBytes(Path.Combine(path, "c.bin"), new byte[19]);
                File.WriteAllBytes(Path.Combine(path, "d.bin"), new byte[23]);

                var complete = DirectoryEnumerator.ReadForPreview(path, CancellationToken.None, 10);
                AssertEqual(4, complete.Entries.Count);
                Assert(complete.Entries[0].IsFolder, "Folder should be first.");
                AssertEqual(17L, complete.Entries.Single(x => x.Name == "b.bin").Size.Value);
                Assert(!complete.WasTruncated, "Small directory should not be truncated.");

                var limited = DirectoryEnumerator.ReadForPreview(path + Path.DirectorySeparatorChar, CancellationToken.None, 2);
                Assert(limited.WasTruncated, "The limit must be reported.");
                AssertEqual(3, limited.Entries.Count);
                AssertEqual(EntryKind.Notice, limited.Entries[2].Kind);
            });
        }

        private static void TestVolumeRootNormalization()
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            string firstPath = null;
            DirectoryEnumerator.Enumerate(
                root,
                CancellationToken.None,
                1,
                item =>
                {
                    firstPath = item.FullPath;
                    return false;
                });

            Assert(firstPath == null || firstPath.StartsWith(root, StringComparison.OrdinalIgnoreCase),
                "Volume root must not become the drive's current directory.");
        }

        private static void TestRecursiveStatistics()
        {
            WithTemporaryDirectory(path =>
            {
                var child = Directory.CreateDirectory(Path.Combine(path, "child"));
                Directory.CreateDirectory(Path.Combine(child.FullName, "grandchild"));
                File.WriteAllBytes(Path.Combine(path, "one.bin"), new byte[11]);
                File.WriteAllBytes(Path.Combine(child.FullName, "two.bin"), new byte[29]);

                var statistics = DirectoryStatisticsScanner.Scan(path, CancellationToken.None);
                AssertEqual(2L, statistics.DirectoryCount);
                AssertEqual(2L, statistics.FileCount);
                AssertEqual(40L, statistics.TotalSize);
                Assert(statistics.IsComplete, "Final statistics must be complete.");
            });
        }

        private static void TestCancelledScan()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                AssertThrows<OperationCanceledException>(() =>
                    DirectoryStatisticsScanner.Scan(Environment.SystemDirectory, cancellation.Token));
            }
        }

        private static void TestEverythingIpc()
        {
            using (var provider = new EverythingFolderSizeProvider())
            {
                var stopwatch = Stopwatch.StartNew();
                var result = provider.QueryStatisticsAsync(Environment.SystemDirectory, CancellationToken.None)
                    .GetAwaiter().GetResult();
                stopwatch.Stop();

                if (!result.HasSize)
                {
                    Console.Write(" [Everything unavailable; fallback verified]");
                    return;
                }

                Assert(result.Size > 0, "An indexed Windows directory must have a non-zero size.");
                Assert(result.HasCounts, "Everything should return descendant counts through Query2.");
                Console.Write(" [directories=" + result.DirectoryCount + ", files=" + result.FileCount + "]");
                Assert(result.DirectoryCount > 0, "The Windows directory should contain subdirectories.");
                Assert(result.FileCount > 0, "The Windows directory should contain files.");
                Console.Write(" [" + result.Source + ", " + stopwatch.Elapsed.TotalMilliseconds.ToString("N1") + " ms]");
            }
        }

        private static void TestEverythingPipeProtocol()
        {
            var pipeName = "FolderViewer.Tests." + Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            {
                var serverTask = Task.Run(() =>
                {
                    server.WaitForConnection();
                    var header = ReadExactly(server, 8);
                    AssertEqual(18U, ReadUInt32(header, 0));
                    var pathLength = checked((int)ReadUInt32(header, 4));
                    var path = Encoding.UTF8.GetString(ReadExactly(server, pathLength));
                    AssertEqual(@"C:\indexed", path);

                    WriteUInt32(header, 0, 200);
                    WriteUInt32(header, 4, 8);
                    server.Write(header, 0, header.Length);
                    var payload = BitConverter.GetBytes(123456789L);
                    server.Write(payload, 0, payload.Length);
                    server.Flush();
                });

                var success = EverythingFolderSizeProvider.TryQuerySdk3Pipe(
                    pipeName,
                    @"C:\indexed",
                    CancellationToken.None,
                    out var size);
                serverTask.GetAwaiter().GetResult();
                Assert(success, "A valid SDK3 response must succeed.");
                AssertEqual(123456789L, size);
            }
        }

        private static void TestEverythingPipeCancellation()
        {
            var pipeName = "FolderViewer.Tests." + Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            using (var cancellation = new CancellationTokenSource(50))
            {
                var serverTask = Task.Run(() =>
                {
                    server.WaitForConnection();
                    var header = ReadExactly(server, 8);
                    var pathLength = checked((int)ReadUInt32(header, 4));
                    ReadExactly(server, pathLength);
                    Thread.Sleep(150);
                });

                var stopwatch = Stopwatch.StartNew();
                AssertThrows<OperationCanceledException>(() =>
                    EverythingFolderSizeProvider.TryQuerySdk3Pipe(
                        pipeName,
                        @"C:\cancelled",
                        cancellation.Token,
                        out _));
                stopwatch.Stop();
                serverTask.GetAwaiter().GetResult();
                Assert(stopwatch.ElapsedMilliseconds < 450, "Cancellation must beat the pipe deadline.");
            }
        }

        private static void TestEverythingPipeMalformedResponse()
        {
            var pipeName = "FolderViewer.Tests." + Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            {
                var serverTask = Task.Run(() =>
                {
                    server.WaitForConnection();
                    var header = ReadExactly(server, 8);
                    var pathLength = checked((int)ReadUInt32(header, 4));
                    ReadExactly(server, pathLength);
                    WriteUInt32(header, 0, 999);
                    WriteUInt32(header, 4, uint.MaxValue);
                    server.Write(header, 0, header.Length);
                    server.Flush();
                    Thread.Sleep(650);
                });

                var stopwatch = Stopwatch.StartNew();
                var success = EverythingFolderSizeProvider.TryQuerySdk3Pipe(
                    pipeName,
                    @"C:\malformed",
                    CancellationToken.None,
                    out _);
                stopwatch.Stop();
                Assert(!success, "An invalid SDK3 response code must fail.");
                Assert(stopwatch.ElapsedMilliseconds < 400,
                    "A malformed response must not make the client drain an untrusted payload length.");
                serverTask.GetAwaiter().GetResult();
            }
        }

        private static void TestEverythingPipeTruncatedResponse()
        {
            var pipeName = "FolderViewer.Tests." + Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            {
                var serverTask = Task.Run(() =>
                {
                    server.WaitForConnection();
                    var header = ReadExactly(server, 8);
                    var pathLength = checked((int)ReadUInt32(header, 4));
                    ReadExactly(server, pathLength);
                    WriteUInt32(header, 0, 200);
                    WriteUInt32(header, 4, 8);
                    server.Write(header, 0, header.Length);
                    server.Write(new byte[4], 0, 4);
                    server.Flush();
                });

                var success = EverythingFolderSizeProvider.TryQuerySdk3Pipe(
                    pipeName,
                    @"C:\truncated",
                    CancellationToken.None,
                    out _);
                serverTask.GetAwaiter().GetResult();
                Assert(!success, "A truncated SDK3 payload must fail.");
            }
        }

        private static void TestEverythingQueryDeadline()
        {
            var pipeName = "FolderViewer.Tests." + Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            using (var provider = new EverythingFolderSizeProvider(
                pipeName,
                pipeName + ".unused"))
            {
                var serverTask = Task.Run(() =>
                {
                    server.WaitForConnection();
                    var header = ReadExactly(server, 8);
                    var pathLength = checked((int)ReadUInt32(header, 4));
                    ReadExactly(server, pathLength);
                    Thread.Sleep(800);
                });

                var stopwatch = Stopwatch.StartNew();
                var result = provider.QueryAsync(@"C:\deadline", CancellationToken.None)
                    .GetAwaiter().GetResult();
                stopwatch.Stop();
                Assert(!result.IsSuccess, "A timed-out Everything query must be unavailable.");
                Assert(stopwatch.ElapsedMilliseconds < 700,
                    "All SDK3 pipe attempts must share one 500 ms query budget.");
                serverTask.GetAwaiter().GetResult();
            }
        }

        private static void TestPluginLifecycle()
        {
            WithTemporaryDirectory(path =>
            {
                var plugin = new Plugin();
                plugin.Cleanup();
                Assert(plugin.CanHandle(path), "The plugin must accept a regular directory.");

                var context = new ContextObject();
                plugin.Prepare(path, context);
                AssertEqual(800d, context.PreferredSize.Width);
                AssertEqual(400d, context.PreferredSize.Height);

                plugin.View(path, context);
                Assert(context.ViewerContent is FolderInfoPanel, "View must attach the folder panel immediately.");
                Assert(!context.IsBusy, "QuickLook's outer busy state must be released immediately.");
                var firstPanel = context.ViewerContent;
                plugin.View(path, context);
                Assert(context.ViewerContent is FolderInfoPanel &&
                       !ReferenceEquals(firstPanel, context.ViewerContent),
                    "A repeated View call must replace and dispose the previous panel.");
                plugin.Cleanup();
                plugin.Cleanup();
            });
        }

        private static FileEntry Entry(string name, bool folder)
        {
            return new FileEntry(name, Path.Combine(Path.GetTempPath(), name), folder, false, folder ? (long?)null : 0, DateTime.Now);
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException();
                offset += read;
            }

            return buffer;
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return BitConverter.ToUInt32(buffer, offset);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
        }

        private static void WithTemporaryDirectory(Action<string> action)
        {
            var path = Path.Combine(Path.GetTempPath(), "FolderViewer.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                action(path);
            }
            finally
            {
                Directory.Delete(path, true);
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception exception)
            {
                _failed++;
                Console.WriteLine("FAIL  " + name + ": " + exception.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void AssertEqual<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }

        private static void AssertThrows<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
        }
    }
}
