// Copyright © 2020 Paddy Xu, Frank Becker
// This file remains available under the GNU General Public License v3 or later.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace QuickLook.Plugin.FolderViewer
{
    public partial class FolderInfoPanel : UserControl, IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;
        private readonly EverythingFolderSizeProvider _folderSizeProvider = new EverythingFolderSizeProvider();
        private readonly Task<FolderAggregateQueryResult> _indexedStatisticsTask;
        private readonly string _path;
        private readonly FolderSizeQueue _sizeQueue;
        private DirectoryStatisticsScanner.Session _scan;
        private bool _scanBusy, _statisticsStarted;
        private int _rootGeneration, _scanGeneration;
        private bool _disposed;
        private bool _indexedCounts;
        private long? _indexedTotalSize;
        private FolderMenu _folderMenu;

        public FolderInfoPanel(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _cancellationToken = _cancellation.Token;
            InitializeComponent();

            var calculating = Strings.Get("Stat_Calculating");
            totalSize.Text = Strings.Format("Stat_TotalSize", calculating);
            numFolders.Text = Strings.Format("Stat_Folders", calculating);
            numFiles.Text = Strings.Format("Stat_Files", calculating);

            Resources.MergedDictionaries.Clear();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            fileListView.Configure(LoadChildrenAsync, _cancellationToken);

            _indexedStatisticsTask = LoadIndexedStatisticsAsync();
            _sizeQueue = new FolderSizeQueue(_folderSizeProvider.QueryAsync, (entry, size) =>
            {
                entry.SetIndexedSize(size);
                fileListView.NotifySizeChanged(entry);
            }, _indexedStatisticsTask, _cancellationToken);
            fileListView.EntryVisible += entry => _sizeQueue.Prioritize(entry);
            fileListView.BranchLoaded += entry => _sizeQueue.AddBatch(entry.Children, entry);
            fileListView.TreeCompacted += _sizeQueue.Prune;
            LoadRootAsync();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
            _folderMenu?.Dispose();
            _folderMenu = null;
            _cancellation.Cancel();
            _sizeQueue.Dispose();
            fileListView.Dispose();
            _folderSizeProvider.Dispose();
            _cancellation.Dispose();
            GC.SuppressFinalize(this);
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            if (_disposed) return;
            _folderMenu?.Dispose();
            _folderMenu = FolderMenu.Attach(this);
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            _folderMenu?.Dispose();
            _folderMenu = null;
        }

        private async void LoadRootAsync()
        {
            var generation = ++_rootGeneration;
            var completed = false;
            var sorter = fileListView.CurrentSorter;
            rootError.Visibility = retryRead.Visibility = Visibility.Collapsed;
            rootLoading.Visibility = Visibility.Visible;
            try
            {
                var result = await IoWorker.Preview.RunAsync(token => DirectoryEnumerator.ReadForPreview(
                    _path, token, sorter: sorter, firstBatch: batch => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_disposed || completed || generation != _rootGeneration) return;
                        fileListView.SetSortedItems(batch, sorter);
                        rootLoading.Visibility = Visibility.Collapsed;
                        statisticsStatus.Text = Strings.Get("ReadingFolder");
                    }))), _cancellationToken, TimeSpan.FromSeconds(10));
                if (_disposed)
                    return;

                if (result.ErrorCode != 0)
                    throw new Win32Exception(result.ErrorCode);

                await RunOnUiThreadAsync(() =>
                {
                    fileListView.SetSortedItems(result.Entries, sorter);
                    _sizeQueue.AddBatch(result.Entries);
                    rootLoading.Visibility = Visibility.Collapsed;
                    statisticsStatus.Text = result.WasTruncated
                        ? Strings.Get("Status_PreviewTruncated")
                        : Strings.Get("Status_Ready");
                    LoadStatisticsAsync();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                await RunOnUiThreadAsync(() =>
                {
                    rootLoading.Visibility = Visibility.Collapsed;
                    rootError.Text = Strings.Get("CannotReadFolder") + "\n" + exception.Message;
                    rootError.Visibility = Visibility.Visible;
                    retryRead.Visibility = Visibility.Visible;
                    statisticsProgress.Visibility = Visibility.Collapsed;
                    statisticsStatus.Text = Strings.Get("Status_Unavailable");
                });
            }
            finally { completed = true; }
        }

        private void OnRetryRead(object sender, RoutedEventArgs args) { if (!_disposed) LoadRootAsync(); }

        private async Task<IReadOnlyList<FileEntry>> LoadChildrenAsync(
            FileEntry entry,
            CancellationToken cancellationToken)
        {
            _sizeQueue.Prioritize(entry);
            var sorter = fileListView.CurrentSorter;
            var result = await IoWorker.Preview.RunAsync(
                token => DirectoryEnumerator.ReadForPreview(entry.FullPath, token, sorter: sorter),
                cancellationToken, TimeSpan.FromSeconds(10));
            if (result.ErrorCode != 0)
                throw new Win32Exception(result.ErrorCode);

            return result.Entries;
        }

        private async Task<FolderAggregateQueryResult> LoadIndexedStatisticsAsync()
        {
            try
            {
                var result = await _folderSizeProvider.QueryStatisticsAsync(_path, _cancellationToken);
                if (_disposed)
                    return default(FolderAggregateQueryResult);

                await RunOnUiThreadAsync(() =>
                {
                    if (result.HasSize)
                    {
                        _indexedTotalSize = result.Size;
                        totalSize.Text = Strings.Format("Stat_TotalSize", ByteSizeFormatter.Format(result.Size));
                        totalSize.ToolTip = result.Source;
                    }

                    if (result.HasCounts)
                    {
                        _indexedCounts = true;
                        numFolders.Text = Strings.Format("Stat_Folders", result.DirectoryCount.ToString("N0"));
                        numFiles.Text = Strings.Format("Stat_Files", result.FileCount.ToString("N0"));
                    }

                    if (result.HasSize || result.HasCounts)
                        statisticsStatus.Text = Strings.Get("Status_EverythingIndex");
                });

                return result;
            }
            catch (OperationCanceledException)
            {
                return default(FolderAggregateQueryResult);
            }
            catch (ObjectDisposedException)
            {
                return default(FolderAggregateQueryResult);
            }
            catch
            {
                return default(FolderAggregateQueryResult);
            }
        }

        private async void LoadStatisticsAsync()
        {
            if (_statisticsStarted) return;
            _statisticsStarted = true;
            try
            {
                var indexed = await _indexedStatisticsTask;
                if (_disposed)
                    return;
                if (indexed.HasSize && indexed.HasCounts)
                {
                    await RunOnUiThreadAsync(() =>
                    {
                        statisticsProgress.Visibility = Visibility.Collapsed;
                        statisticsStatus.Text = Strings.Get("Status_EverythingIndex");
                    });
                    return;
                }

                var scanPath = await IoWorker.Preview.RunAsync(token =>
                    EverythingFolderSizeProvider.ResolveAggregateQueryPath(_path), _cancellationToken, TimeSpan.FromSeconds(2));
                var network = await IoWorker.Preview.RunAsync(token => PathPolicy.IsNetwork(scanPath),
                    _cancellationToken, TimeSpan.FromSeconds(2));
                if (network)
                {
                    await RunOnUiThreadAsync(MarkNetworkStatisticsUnavailable);
                    return;
                }

                await Task.Delay(350, _cancellationToken);
                _scan = new DirectoryStatisticsScanner.Session(scanPath);
                await RunScanSliceAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                await RunOnUiThreadAsync(() =>
                {
                    statisticsProgress.Visibility = Visibility.Collapsed;
                    statisticsStatus.Text = Strings.Get("Status_StatsUnavailable");
                    statisticsStatus.ToolTip = exception.Message;
                });
            }
        }

        internal void MarkNetworkStatisticsUnavailable()
        {
            var notScanned = Strings.Get("Stat_NotScanned");
            if (!_indexedCounts)
            {
                numFolders.Text = Strings.Format("Stat_Folders", notScanned);
                numFiles.Text = Strings.Format("Stat_Files", notScanned);
            }
            if (!_indexedTotalSize.HasValue) totalSize.Text = Strings.Format("Stat_TotalSize", notScanned);
            statisticsProgress.Visibility = Visibility.Collapsed;
            statisticsStatus.Text = _indexedTotalSize.HasValue || _indexedCounts
                ? Strings.Get("Status_EverythingIndex") : Strings.Get("Status_NetworkFolder");
        }

        private async void OnContinueStatistics(object sender, RoutedEventArgs args)
        {
            if (_disposed || _scanBusy || _scan == null) return;
            await RunScanSliceAsync();
        }

        private async Task RunScanSliceAsync()
        {
            var generation = ++_scanGeneration;
            var scan = _scan;
            _scanBusy = true;
            continueScan.Visibility = Visibility.Collapsed;
            statisticsProgress.Visibility = Visibility.Visible;
            try
            {
                var result = await IoWorker.Statistics.RunAsync(token =>
                {
                    using (new BackgroundIoScope())
                        return scan.Step(token, TimeSpan.FromSeconds(2), 50000,
                            statistics => QueueStatisticsUpdate(statistics, generation));
                }, _cancellationToken, TimeSpan.FromSeconds(10));
                if (_disposed) return;
                ApplyStatistics(result);
                if (!result.IsComplete)
                {
                    statisticsProgress.Visibility = Visibility.Collapsed;
                    statisticsStatus.Text = Strings.Get("Status_Paused");
                    continueScan.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!_disposed)
                {
                    statisticsProgress.Visibility = Visibility.Collapsed;
                    statisticsStatus.Text = Strings.Get("Status_StatsUnavailable");
                    statisticsStatus.ToolTip = error.Message;
                    // A timed-out native call may still be unwinding: do not reuse its session.
                    _scan = null;
                }
            }
            finally { _scanBusy = false; ++_scanGeneration; }
        }

        private void QueueStatisticsUpdate(DirectoryStatistics statistics, int generation)
        {
            if (_disposed)
                return;

            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (generation == _scanGeneration) ApplyStatistics(statistics);
                }));
            }
            catch (Exception exception) when (
                exception is TaskCanceledException ||
                exception is InvalidOperationException)
            {
            }
        }

        private void ApplyStatistics(DirectoryStatistics statistics)
        {
            if (_disposed)
                return;

            if (!_indexedCounts)
            {
                numFolders.Text = Strings.Format("Stat_Folders", statistics.DirectoryCount.ToString("N0"));
                numFiles.Text = Strings.Format("Stat_Files", statistics.FileCount.ToString("N0"));
            }
            if (!_indexedTotalSize.HasValue)
                totalSize.Text = Strings.Format("Stat_TotalSize", ByteSizeFormatter.Format(statistics.TotalSize));

            if (statistics.IsComplete)
            {
                statisticsProgress.Visibility = Visibility.Collapsed;
                var skipped = statistics.InaccessibleDirectoryCount + statistics.SkippedDirectoryCount;
                statisticsStatus.Text = skipped == 0
                    ? (_indexedTotalSize.HasValue ? Strings.Get("Status_IndexedSize") : Strings.Get("Status_Done"))
                    : Strings.Format("Status_Partial", skipped.ToString("N0"));
            }
            else
            {
                statisticsStatus.Text = Strings.Get("Status_Counting");
            }
        }

        private async Task RunOnUiThreadAsync(Action action)
        {
            if (_disposed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            try
            {
                if (Dispatcher.CheckAccess())
                {
                    if (!_disposed)
                        action();
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (!_disposed)
                        action();
                }).Task.ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is OperationCanceledException ||
                exception is InvalidOperationException)
            {
            }
        }
    }
}
