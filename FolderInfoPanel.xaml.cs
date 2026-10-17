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
        private bool _disposed;
        private bool _indexedCounts;
        private long? _indexedTotalSize;

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
            fileListView.Configure(LoadChildrenAsync, _cancellationToken);

            _indexedStatisticsTask = LoadIndexedStatisticsAsync();
            LoadRootAsync();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellation.Cancel();
            fileListView.Dispose();
            _folderSizeProvider.Dispose();
            _cancellation.Dispose();
            GC.SuppressFinalize(this);
        }

        private async void LoadRootAsync()
        {
            try
            {
                var result = await Task.Run(
                    () => DirectoryEnumerator.ReadForPreview(_path, _cancellationToken),
                    _cancellationToken);
                if (_disposed)
                    return;

                if (result.ErrorCode != 0)
                    throw new Win32Exception(result.ErrorCode);

                await RunOnUiThreadAsync(() =>
                {
                    fileListView.SetItems(result.Entries);
                    QueueFolderSizeLookups(result.Entries);
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
                    statisticsProgress.Visibility = Visibility.Collapsed;
                    statisticsStatus.Text = Strings.Get("Status_Unavailable");
                });
            }
        }

        private async Task<IReadOnlyList<FileEntry>> LoadChildrenAsync(
            FileEntry entry,
            CancellationToken cancellationToken)
        {
            LoadIndexedEntrySizeAsync(entry);
            var result = await Task.Run(
                () => DirectoryEnumerator.ReadForPreview(entry.FullPath, cancellationToken),
                cancellationToken);
            if (result.ErrorCode != 0)
                throw new Win32Exception(result.ErrorCode);

            await RunOnUiThreadAsync(() => QueueFolderSizeLookups(result.Entries));
            return result.Entries;
        }

        private void QueueFolderSizeLookups(IReadOnlyList<FileEntry> entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry.IsFolder)
                    LoadIndexedEntrySizeAsync(entry);
            }
        }

        private async void LoadIndexedEntrySizeAsync(FileEntry entry)
        {
            try
            {
                var result = await _folderSizeProvider.QueryAsync(entry.FullPath, _cancellationToken);
                if (!_disposed && result.IsSuccess)
                    await RunOnUiThreadAsync(() =>
                    {
                        entry.SetIndexedSize(result.Size);
                        fileListView.NotifySizeChanged();
                    });
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch
            {
                // Everything is an optional accelerator; an IPC failure must not affect browsing.
            }
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

                if (DirectoryStatisticsScanner.IsNetworkPath(_path))
                {
                    await RunOnUiThreadAsync(() =>
                    {
                        var notScanned = Strings.Get("Stat_NotScanned");
                        numFolders.Text = Strings.Format("Stat_Folders", notScanned);
                        numFiles.Text = Strings.Format("Stat_Files", notScanned);
                        if (!_indexedTotalSize.HasValue)
                            totalSize.Text = Strings.Format("Stat_TotalSize", notScanned);
                        statisticsProgress.Visibility = Visibility.Collapsed;
                        statisticsStatus.Text = _indexedTotalSize.HasValue || _indexedCounts
                            ? Strings.Get("Status_EverythingIndex")
                            : Strings.Get("Status_NetworkFolder");
                    });
                    return;
                }

                await Task.Delay(350, _cancellationToken);
                var statistics = await Task.Factory.StartNew(
                    () =>
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                        return DirectoryStatisticsScanner.Scan(
                            _path,
                            _cancellationToken,
                            QueueStatisticsUpdate);
                    },
                    _cancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                await RunOnUiThreadAsync(() => ApplyStatistics(statistics));
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

        private void QueueStatisticsUpdate(DirectoryStatistics statistics)
        {
            if (_disposed)
                return;

            try
            {
                Dispatcher.BeginInvoke(new Action(() => ApplyStatistics(statistics)));
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
                statisticsStatus.Text = statistics.InaccessibleDirectoryCount == 0
                    ? (_indexedTotalSize.HasValue ? Strings.Get("Status_IndexedSize") : Strings.Get("Status_Done"))
                    : Strings.Format("Status_Skipped", statistics.InaccessibleDirectoryCount.ToString("N0"));
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
