// Copyright © 2020 Paddy Xu, Frank Becker
// This file remains available under the GNU General Public License v3 or later.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuickLook.Plugin.FolderViewer
{
    public partial class FileListView : UserControl, IDisposable
    {
        private static FileEntrySorter _sorter = FileEntrySorter.Default;
        private CancellationToken _cancellationToken;
        private bool _disposed, _sortRunning, _rootDirty;
        private Func<FileEntry, CancellationToken, Task<IReadOnlyList<FileEntry>>> _loadChildren;
        private IReadOnlyList<FileEntry> _items = Array.Empty<FileEntry>();
        private readonly HashSet<FileEntry> _dirty = new HashSet<FileEntry>();
        private readonly DispatcherTimer _resortTimer;
        private FileEntry _selected;
        private bool _restoring;
        internal const int RetainedEntryBudget = 75000;

        public FileListView()
        {
            InitializeComponent();
            UpdateSortArrows();
            _resortTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                { Interval = TimeSpan.FromMilliseconds(200) };
            _resortTimer.Tick += (s, e) => { _resortTimer.Stop(); StartSorting(); };
            treeView.SelectedItemChanged += (s, e) => { if (!_restoring) _selected = e.NewValue as FileEntry; };
        }

        internal FileEntrySorter CurrentSorter => _sorter;
        internal Task SortCompletion { get; private set; } = Task.CompletedTask;
        internal event Action<FileEntry> EntryVisible;
        internal event Action<FileEntry> BranchLoaded;
        internal event Action TreeCompacted;

        public void Configure(Func<FileEntry, CancellationToken, Task<IReadOnlyList<FileEntry>>> loadChildren,
            CancellationToken cancellationToken)
        {
            _loadChildren = loadChildren ?? throw new ArgumentNullException(nameof(loadChildren));
            _cancellationToken = cancellationToken;
        }

        public void SetItems(IReadOnlyList<FileEntry> entries) => SetSortedItems(entries, null);

        internal void SetSortedItems(IReadOnlyList<FileEntry> entries, FileEntrySorter sortedBy)
        {
            if (_disposed) return;
            var selected = _selected;
            _restoring = true;
            try
            {
                _items = entries ?? Array.Empty<FileEntry>();
                treeGrid.DataContext = _items;
                var root = selected;
                while (root?.Parent != null) root = root.Parent;
                if (root != null && _items.Contains(root)) RestoreSelection(selected);
                else _selected = null;
            }
            finally { _restoring = false; }
            if (!ReferenceEquals(sortedBy, _sorter)) { _rootDirty = true; StartSorting(); }
        }

        internal void NotifySizeChanged(FileEntry entry)
        {
            if (_disposed || _sorter.Column != SortColumn.Size) return;
            if (entry.Parent == null) _rootDirty = true;
            else if (entry.Parent.IsExpanded) _dirty.Add(entry.Parent);
            else entry.Parent.AppliedSort = null;
            // A fixed throttle (not an endlessly restarted debounce) also handles long streams.
            if (!_resortTimer.IsEnabled) _resortTimer.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _resortTimer.Stop();
            _loadChildren = null;
            EntryVisible = null;
            BranchLoaded = null;
            TreeCompacted = null;
            _dirty.Clear();
            _items = Array.Empty<FileEntry>();
            treeGrid.DataContext = null;
        }

        private async void OnItemExpanded(object sender, RoutedEventArgs args)
        {
            if (_disposed || _loadChildren == null || !(sender is TreeViewItem item) ||
                !ReferenceEquals(args.OriginalSource, item) || !(item.DataContext is FileEntry entry)) return;
            EntryVisible?.Invoke(entry);
            if (!entry.TryBeginLoading())
            {
                if (entry.IsLoaded && !ReferenceEquals(entry.AppliedSort, _sorter))
                { _dirty.Add(entry); StartSorting(); }
                BranchLoaded?.Invoke(entry);
                return;
            }
            var sorter = _sorter;
            try
            {
                var children = await _loadChildren(entry, _cancellationToken);
                if (_disposed || _cancellationToken.IsCancellationRequested) return;
                entry.CompleteLoading(children);
                entry.AppliedSort = sorter;
                if (!ReferenceEquals(sorter, _sorter)) { _dirty.Add(entry); StartSorting(); }
                BranchLoaded?.Invoke(entry);
                TrimCache();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_disposed) entry.FailLoading(error.Message); }
        }

        private void OnItemCollapsed(object sender, RoutedEventArgs args)
        {
            if (!_disposed && !_restoring && ReferenceEquals(args.OriginalSource, sender)) TrimCache();
        }

        private void OnItemLoaded(object sender, RoutedEventArgs args)
        {
            if (!_disposed && sender is TreeViewItem item && item.DataContext is FileEntry entry)
            {
                item.DataContextChanged -= OnRecycledItem;
                item.DataContextChanged += OnRecycledItem;
                EntryVisible?.Invoke(entry);
            }
        }

        private void OnRecycledItem(object sender, DependencyPropertyChangedEventArgs args)
        { if (!_disposed && args.NewValue is FileEntry entry) EntryVisible?.Invoke(entry); }

        private void OnHeaderClick(object sender, RoutedEventArgs args)
        {
            if (_disposed || !(sender is FrameworkElement header) || !(header.Tag is SortColumn column)) return;
            ChangeSort(column, column == _sorter.Column && !_sorter.Descending);
            args.Handled = true;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            if (args.HorizontalChange != 0) headerOffset.X = -args.HorizontalOffset;
        }

        internal void ChangeSort(SortColumn column, bool descending)
        {
            _sorter = new FileEntrySorter(column, descending);
            UpdateSortArrows();
            _rootDirty = true;
            foreach (var entry in VisibleEntries(_items))
                if (entry.IsExpanded && entry.IsLoaded) _dirty.Add(entry);
            StartSorting();
        }

        private void StartSorting()
        {
            if (_disposed || _sortRunning) return;
            SortCompletion = SortAsync();
        }

        private async Task SortAsync()
        {
            _sortRunning = true;
            try
            {
                while (!_disposed && (_rootDirty || _dirty.Count != 0))
                {
                    var sorter = _sorter;
                    var levels = new List<Level>();
                    if (_rootDirty) levels.Add(new Level(null, _items));
                    _rootDirty = false;
                    foreach (var entry in _dirty)
                        if (entry.IsExpanded && entry.IsLoaded) levels.Add(new Level(entry, entry.Children));
                    _dirty.Clear();
                    await Task.Run(() =>
                    {
                        foreach (var level in levels)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            level.Sorted = sorter.Sort(level.Source);
                        }
                    }, _cancellationToken);
                    if (_disposed) return;
                    if (!ReferenceEquals(sorter, _sorter)) continue;
                    var scroll = FindVisual<ScrollViewer>(treeView);
                    var oldOffset = scroll?.VerticalOffset ?? 0;
                    var anchor = VisibleEntries(_items).Skip((int)oldOffset).FirstOrDefault();
                    var selected = _selected;
                    _restoring = true;
                    try
                    {
                        foreach (var level in levels)
                        {
                            if (level.Owner == null)
                            {
                                if (!ReferenceEquals(level.Source, _items)) continue;
                                if (level.Source.SequenceEqual(level.Sorted)) continue;
                                _items = level.Sorted;
                                treeGrid.DataContext = _items;
                            }
                            else if (level.Owner.ChildrenVersion == level.Version && level.Owner.IsExpanded)
                            {
                                if (!level.Source.SequenceEqual(level.Sorted)) level.Owner.ReplaceChildren(level.Sorted, sorter);
                                level.Owner.AppliedSort = sorter;
                            }
                        }
                        RestoreSelection(selected);
                        scroll?.ScrollToVerticalOffset(anchor == null ? oldOffset : VisibleIndex(anchor));
                        _selected = selected;
                    }
                    finally { _restoring = false; }
                }
            }
            catch (OperationCanceledException) { }
            finally { _sortRunning = false; }
        }

        private void RestoreSelection(FileEntry selected)
        {
            if (selected != null)
            {
                selected.IsSelected = true;
                var container = ContainerFor(selected, true);
                if (container != null) container.IsSelected = true;
            }
            _selected = selected;
        }

        private TreeViewItem ContainerFor(FileEntry entry, bool realize = false)
        {
            ItemsControl parent = entry.Parent == null ? (ItemsControl)treeView : ContainerFor(entry.Parent, realize);
            if (parent == null) return null;
            if (realize && parent.ItemContainerGenerator.ContainerFromItem(entry) == null)
            {
                parent.ApplyTemplate();
                var presenter = FindVisual<ItemsPresenter>(parent);
                presenter?.ApplyTemplate();
                var panel = presenter == null ? null : FindVisual<VirtualizingPanel>(presenter);
                var index = parent.Items.IndexOf(entry);
                if (index >= 0) panel?.BringIndexIntoViewPublic(index);
                parent.UpdateLayout();
            }
            return parent?.ItemContainerGenerator.ContainerFromItem(entry) as TreeViewItem;
        }

        private int VisibleIndex(FileEntry entry)
        {
            var index = 0;
            foreach (var current in VisibleEntries(_items))
            { if (ReferenceEquals(current, entry)) return index; index++; }
            return 0;
        }

        private static IEnumerable<FileEntry> VisibleEntries(IReadOnlyList<FileEntry> entries)
        {
            foreach (var entry in entries)
            {
                yield return entry;
                if (entry.IsExpanded && entry.IsLoaded)
                    foreach (var child in VisibleEntries(entry.Children)) yield return child;
            }
        }

        internal void TrimCache(int budget = RetainedEntryBudget)
        {
            var all = new List<FileEntry>();
            var pending = new Stack<FileEntry>(_items);
            while (pending.Count != 0)
            {
                var item = pending.Pop();
                all.Add(item);
                if (item.IsLoaded) foreach (var child in item.Children) pending.Push(child);
            }
            var count = all.Count;
            foreach (var candidate in all.Where(e => e.IsLoaded && !e.IsExpanded)
                .OrderBy(e => e.LastAccessUtc))
            {
                if (count <= budget) break;
                var attached = true;
                for (var parent = candidate.Parent; parent != null; parent = parent.Parent)
                    if (!parent.IsLoaded) { attached = false; break; }
                if (!attached) continue;
                if (ContainsSelection(candidate)) continue;
                var descendants = CountLoaded(candidate);
                if (candidate.EvictChildren()) count -= descendants;
            }
            TreeCompacted?.Invoke();
        }

        private bool ContainsSelection(FileEntry candidate)
        {
            for (var entry = _selected; entry != null; entry = entry.Parent)
                if (ReferenceEquals(entry, candidate)) return true;
            return false;
        }

        private static int CountLoaded(FileEntry root)
        {
            if (!root.IsLoaded) return 0;
            return root.Children.Sum(c => 1 + CountLoaded(c));
        }

        private static T FindVisual<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T found) return found;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = FindVisual<T>(VisualTreeHelper.GetChild(root, i));
                if (child != null) return child;
            }
            return null;
        }

        private void UpdateSortArrows()
        {
            var arrow = _sorter.Descending ? "▼" : "▲";
            nameArrow.Text = _sorter.Column == SortColumn.Name ? arrow : string.Empty;
            sizeArrow.Text = _sorter.Column == SortColumn.Size ? arrow : string.Empty;
            modifiedArrow.Text = _sorter.Column == SortColumn.Modified ? arrow : string.Empty;
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
        {
            if (_disposed || args.ClickCount != 2) return;
            // Handle the second press before TreeViewItem's built-in double-click toggle.
            // Resolve the nearest container so nested rows cannot activate their parent.
            var source = args.OriginalSource as DependencyObject;
            while (source != null && !(source is TreeViewItem))
            {
                if (source is ToggleButton) return; // The disclosure arrow keeps its single-click behavior.
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            if (source is TreeViewItem item)
                args.Handled = ActivateItem(item, Keyboard.Modifiers, OpenPath);
        }

        internal bool ActivateItem(TreeViewItem item, ModifierKeys modifiers, Action<string> openPath)
        {
            if (_disposed || !(item.DataContext is FileEntry entry) || string.IsNullOrEmpty(entry.FullPath))
                return false;
            item.IsSelected = true;
            if (entry.IsFolder && (modifiers & ModifierKeys.Control) == 0)
                item.IsExpanded = !item.IsExpanded;
            else
                openPath(entry.FullPath);
            return true;
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception error) when (error is Win32Exception || error is InvalidOperationException) { }
        }

        private sealed class Level
        {
            public Level(FileEntry owner, IReadOnlyList<FileEntry> source)
            { Owner = owner; Source = source; Version = owner?.ChildrenVersion ?? 0; }
            public FileEntry Owner;
            public IReadOnlyList<FileEntry> Source, Sorted;
            public int Version;
        }
    }
}
