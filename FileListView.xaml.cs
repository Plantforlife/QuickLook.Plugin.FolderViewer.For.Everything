// Copyright © 2020 Paddy Xu, Frank Becker
// This file remains available under the GNU General Public License v3 or later.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace QuickLook.Plugin.FolderViewer
{
    public partial class FileListView : UserControl, IDisposable
    {
        // Shared across previews so the chosen order sticks until QuickLook restarts.
        private static FileEntrySorter _sorter = FileEntrySorter.Default;

        private CancellationToken _cancellationToken;
        private bool _disposed;
        private Func<FileEntry, CancellationToken, Task<IReadOnlyList<FileEntry>>> _loadChildren;
        private IReadOnlyList<FileEntry> _items = Array.Empty<FileEntry>();
        private DispatcherTimer _resortTimer;

        public FileListView()
        {
            InitializeComponent();
            UpdateSortArrows();
        }

        public void Configure(
            Func<FileEntry, CancellationToken, Task<IReadOnlyList<FileEntry>>> loadChildren,
            CancellationToken cancellationToken)
        {
            _loadChildren = loadChildren ?? throw new ArgumentNullException(nameof(loadChildren));
            _cancellationToken = cancellationToken;
        }

        public void SetItems(IReadOnlyList<FileEntry> entries)
        {
            _items = _sorter.Sort(entries);
            treeGrid.DataContext = _items;
        }

        /// <summary>
        /// Folder sizes arrive from Everything after the list is shown. When sorting by size,
        /// re-sort once they settle (debounced, so rows don't jump on every folder).
        /// </summary>
        public void NotifySizeChanged()
        {
            if (_disposed || _sorter.Column != SortColumn.Size)
                return;

            if (_resortTimer == null)
            {
                _resortTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _resortTimer.Tick += (sender, args) =>
                {
                    _resortTimer.Stop();
                    if (!_disposed && _sorter.Column == SortColumn.Size)
                        Resort();
                };
            }

            _resortTimer.Stop();
            _resortTimer.Start();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _resortTimer?.Stop();
            _loadChildren = null;
            _items = Array.Empty<FileEntry>();
            treeGrid.DataContext = null;
            GC.SuppressFinalize(this);
        }

        private async void OnItemExpanded(object sender, System.Windows.RoutedEventArgs args)
        {
            if (_disposed || _loadChildren == null || !(sender is TreeViewItem item) ||
                !(item.DataContext is FileEntry entry) || !entry.TryBeginLoading())
            {
                return;
            }

            try
            {
                var children = await _loadChildren(entry, _cancellationToken);
                if (!_disposed && !_cancellationToken.IsCancellationRequested)
                    entry.CompleteLoading(_sorter.Sort(children));
            }
            catch (OperationCanceledException)
            {
                if (!_disposed)
                    entry.FailLoading(Strings.Get("LoadingCanceled"));
            }
            catch (Exception exception)
            {
                if (!_disposed)
                    entry.FailLoading(exception.Message);
            }
        }

        private void OnHeaderClick(object sender, MouseButtonEventArgs args)
        {
            if (_disposed || !(sender is FrameworkElement header) || !(header.Tag is SortColumn column))
                return;

            // Same column toggles direction; a new column starts ascending.
            _sorter = new FileEntrySorter(column, column == _sorter.Column && !_sorter.Descending);
            UpdateSortArrows();
            Resort();
            args.Handled = true;
        }

        private void Resort()
        {
            _items = _sorter.Sort(_items);
            foreach (var entry in _items)
                entry.ApplySort(_sorter);
            treeGrid.DataContext = _items;
        }

        private void UpdateSortArrows()
        {
            var arrow = _sorter.Descending ? "▼" : "▲";
            nameArrow.Text = _sorter.Column == SortColumn.Name ? arrow : string.Empty;
            sizeArrow.Text = _sorter.Column == SortColumn.Size ? arrow : string.Empty;
            modifiedArrow.Text = _sorter.Column == SortColumn.Modified ? arrow : string.Empty;
        }

        private void OnItemMouseDoubleClick(object sender, MouseButtonEventArgs args)
        {
            if (_disposed || !(sender is TreeViewItem item) || !item.IsSelected ||
                !(item.DataContext is FileEntry entry) ||
                entry.IsPlaceholder || string.IsNullOrEmpty(entry.FullPath))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
                args.Handled = true;
            }
            catch (Exception exception) when (
                exception is Win32Exception ||
                exception is InvalidOperationException)
            {
                // The file may have disappeared after the preview was populated.
            }
        }
    }
}
