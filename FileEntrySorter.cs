using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickLook.Plugin.FolderViewer
{
    public enum SortColumn
    {
        Name,
        Size,
        Modified
    }

    /// <summary>
    /// Explorer-like ordering: folders before files, then by the chosen column. Notices and
    /// placeholders always stay at the end, and ties fall back to the name.
    /// </summary>
    internal sealed class FileEntrySorter : IComparer<FileEntry>
    {
        public static readonly FileEntrySorter Default = new FileEntrySorter(SortColumn.Name, false);

        public FileEntrySorter(SortColumn column, bool descending)
        {
            Column = column;
            Descending = descending;
            _names = StringComparer.CurrentCultureIgnoreCase;
        }
        private readonly StringComparer _names;

        public SortColumn Column { get; }

        public bool Descending { get; }

        public int Compare(FileEntry x, FileEntry y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return 1;
            if (y == null)
                return -1;

            var group = Rank(x).CompareTo(Rank(y));
            if (group != 0)
                return group;

            int result;
            switch (Column)
            {
                case SortColumn.Size:
                    // Folders whose size is not known yet sort as the smallest.
                    result = (x.Size ?? -1).CompareTo(y.Size ?? -1);
                    break;
                case SortColumn.Modified:
                    result = x.ModifiedDate.CompareTo(y.ModifiedDate);
                    break;
                default:
                    result = 0;
                    break;
            }

            if (result == 0)
                result = CompareNames(x, y);

            return Descending ? -result : result;
        }

        public IReadOnlyList<FileEntry> Sort(IEnumerable<FileEntry> entries)
        {
            if (entries == null) return Array.Empty<FileEntry>();
            // Sizes can arrive while a background sort runs. Snapshot keys so comparisons
            // remain transitive; a later size notification schedules the next version.
            var keys = entries.Select((e, i) => new SortKey(e, i)).ToArray();
            Array.Sort(keys, (a, b) =>
            {
                var rank = Rank(a.Entry).CompareTo(Rank(b.Entry));
                if (rank != 0) return rank;
                var result = Column == SortColumn.Size ? a.Size.CompareTo(b.Size) :
                    Column == SortColumn.Modified ? a.Entry.ModifiedDate.CompareTo(b.Entry.ModifiedDate) : 0;
                if (result == 0) result = CompareNames(a.Entry, b.Entry);
                if (Descending) result = -result;
                return result == 0 ? a.Index.CompareTo(b.Index) : result;
            });
            return Array.ConvertAll(keys, k => k.Entry);
        }

        private readonly struct SortKey
        {
            public SortKey(FileEntry entry, int index) { Entry = entry; Index = index; Size = entry.Size ?? -1; }
            public FileEntry Entry { get; }
            public int Index { get; }
            public long Size { get; }
        }

        private static int Rank(FileEntry entry)
        {
            switch (entry.Kind)
            {
                case EntryKind.Folder:
                    return 0;
                case EntryKind.File:
                    return 1;
                default:
                    return 2;
            }
        }

        private int CompareNames(FileEntry x, FileEntry y)
        {
            var result = _names.Compare(x.Name, y.Name);
            return result != 0 ? result : StringComparer.Ordinal.Compare(x.Name, y.Name);
        }
    }
}
