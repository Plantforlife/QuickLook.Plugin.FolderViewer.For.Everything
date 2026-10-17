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
        }

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
            // OrderBy is stable, so equal items keep their current relative order.
            return entries == null ? Array.Empty<FileEntry>() : entries.OrderBy(e => e, this).ToArray();
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

        private static int CompareNames(FileEntry x, FileEntry y)
        {
            var result = StringComparer.CurrentCultureIgnoreCase.Compare(x.Name, y.Name);
            return result != 0 ? result : StringComparer.Ordinal.Compare(x.Name, y.Name);
        }
    }
}
