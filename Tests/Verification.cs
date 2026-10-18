using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickLook.Plugin.FolderViewer.Tests
{
    internal static partial class Program
    {
        private static void RunQueueBenchmark()
        {
            using (var server = new SizeServer(20))
            using (var provider = new EverythingFolderSizeProvider(server.Name))
            {
                var elapsed = Stopwatch.StartNew();
                var tasks = Enumerable.Range(0, 100)
                    .Select(i => provider.QueryAsync(@"C:\benchmark-" + i, CancellationToken.None)).ToArray();
                Task.WaitAll(tasks);
                Console.WriteLine("IPC 100 requests, 20 ms server delay: {0}/100 successes, {1} connection(s), {2:F2} ms to drain",
                    tasks.Count(t => t.Result.IsSuccess), server.Connections, elapsed.Elapsed.TotalMilliseconds);
            }
        }

        private static void RunUiBenchmark()
        {
            using (var view = new FileListView())
            {
                var entries = Enumerable.Range(0, 25000).Select(i => Entry("item" + i.ToString("D5"), false)).ToArray();
                view.SetSortedItems(entries, view.CurrentSorter);
                Layout(view);
                var tree = (TreeView)view.FindName("treeView");
                ((TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(entries[3])).IsSelected = true;
                for (var i = 0; i < 3; i++)
                {
                    var elapsed = Stopwatch.StartNew();
                    view.ChangeSort(SortColumn.Name, i % 2 == 0);
                    var dispatchMs = elapsed.Elapsed.TotalMilliseconds;
                    PumpUntil(() => view.SortCompletion.IsCompleted);
                    view.SortCompletion.GetAwaiter().GetResult();
                    Layout(view);
                    Console.WriteLine("25,000-row sort {0}: caller {1:F2} ms, full completion/layout {2:F2} ms, selection {3}",
                        i + 1, dispatchMs, elapsed.Elapsed.TotalMilliseconds, ReferenceEquals(tree.SelectedItem, entries[3]));
                }
                var realized = entries.Count(e => tree.ItemContainerGenerator.ContainerFromItem(e) != null);
                Console.WriteLine("Realized containers: {0} of {1}", realized, entries.Length);
            }
        }

        private static void RenderPreview(string output)
        {
            using (var panel = new FolderInfoPanel(Environment.SystemDirectory))
            {
                var loaded = (FrameworkElement)panel.FindName("rootLoading");
                PumpUntil(() => loaded.Visibility == Visibility.Collapsed);
                var status = (TextBlock)panel.FindName("statisticsStatus");
                status.Text = Strings.Get("Status_Paused");
                ((Button)panel.FindName("continueScan")).Visibility = Visibility.Visible;
                ((FrameworkElement)panel.FindName("statisticsProgress")).Visibility = Visibility.Collapsed;
                var surface = new Border { Width = 800, Height = 400, Background = Brushes.White, Child = panel };
                surface.Measure(new Size(800, 400));
                surface.Arrange(new Rect(0, 0, 800, 400));
                surface.UpdateLayout();
                var bitmap = new RenderTargetBitmap(800, 400, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                using (var stream = File.Create(output)) encoder.Save(stream);
                Console.WriteLine("Rendered " + output);
            }
        }
    }
}
