// Copyright © 2020 Paddy Xu, Frank Becker
// This file remains available under the GNU General Public License v3 or later.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace QuickLook.Plugin.FolderViewer
{
    public sealed class FileEntry : IComparable<FileEntry>, INotifyPropertyChanged
    {
        private static readonly IReadOnlyList<FileEntry> EmptyChildren = Array.Empty<FileEntry>();
        private static readonly IReadOnlyList<FileEntry> LoadingChildren =
            new[] { new FileEntry(Strings.Get("Loading"), EntryKind.Placeholder) };

        private IReadOnlyList<FileEntry> _children;
        private bool _isExpanded;
        private int _loadState;
        private long? _size;

        public FileEntry(
            string name,
            string fullPath,
            bool isFolder,
            bool isReparsePoint,
            long? size,
            DateTime modifiedDate)
            : this(name, isFolder ? EntryKind.Folder : EntryKind.File)
        {
            FullPath = fullPath;
            IsReparsePoint = isReparsePoint;
            _size = size;
            ModifiedDate = modifiedDate;
        }

        private FileEntry(string name, EntryKind kind)
        {
            Name = name;
            Kind = kind;
            _children = kind == EntryKind.Folder ? LoadingChildren : EmptyChildren;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IReadOnlyList<FileEntry> Children => _children;

        public string FullPath { get; }

        public bool IsFolder => Kind == EntryKind.Folder;

        public bool IsPlaceholder => Kind == EntryKind.Placeholder;

        public bool IsReparsePoint { get; }

        /// <summary>
        /// Bound two-way to TreeViewItem.IsExpanded, so the expanded state survives the item
        /// containers being regenerated when the list is re-sorted.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;

                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading => Volatile.Read(ref _loadState) == 1;

        public EntryKind Kind { get; }

        public DateTime ModifiedDate { get; }

        public string Name { get; }

        public long? Size
        {
            get => _size;
            private set
            {
                if (_size == value)
                    return;

                _size = value;
                OnPropertyChanged();
            }
        }

        public static FileEntry CreateNotice(string message)
        {
            return new FileEntry(message, EntryKind.Notice);
        }

        public int CompareTo(FileEntry other)
        {
            if (ReferenceEquals(other, null))
                return -1;

            if (IsFolder != other.IsFolder)
                return IsFolder ? -1 : 1;

            var result = StringComparer.CurrentCultureIgnoreCase.Compare(Name, other.Name);
            return result != 0 ? result : StringComparer.Ordinal.Compare(Name, other.Name);
        }

        public bool TryBeginLoading()
        {
            return IsFolder && Interlocked.CompareExchange(ref _loadState, 1, 0) == 0;
        }

        public void CompleteLoading(IReadOnlyList<FileEntry> children)
        {
            _children = children ?? EmptyChildren;
            Volatile.Write(ref _loadState, 2);
            OnPropertyChanged(nameof(Children));
            OnPropertyChanged(nameof(IsLoading));
        }

        public void FailLoading(string message)
        {
            _children = new[] { CreateNotice(message) };
            Volatile.Write(ref _loadState, 2);
            OnPropertyChanged(nameof(Children));
            OnPropertyChanged(nameof(IsLoading));
        }

        /// <summary>
        /// Re-sorts already loaded children (recursively). Folders that were never expanded
        /// are sorted when they load.
        /// </summary>
        internal void ApplySort(FileEntrySorter sorter)
        {
            if (!IsFolder || Volatile.Read(ref _loadState) != 2 || _children.Count == 0)
                return;

            _children = sorter.Sort(_children);
            OnPropertyChanged(nameof(Children));

            foreach (var child in _children)
                child.ApplySort(sorter);
        }

        public void SetIndexedSize(long size)
        {
            if (IsFolder && size >= 0)
                Size = size;
        }

        public override string ToString()
        {
            return Name;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum EntryKind
    {
        File,
        Folder,
        Placeholder,
        Notice
    }
}
