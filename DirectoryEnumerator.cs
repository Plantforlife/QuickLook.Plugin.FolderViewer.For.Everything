using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace QuickLook.Plugin.FolderViewer
{
    internal static class DirectoryEnumerator
    {
        internal const int DefaultPreviewLimit = 25000;

        private const int ErrorFileNotFound = 2;
        private const int ErrorNoMoreFiles = 18;
        private const int ErrorInvalidParameter = 87;
        private const int FindFirstExLargeFetch = 2;

        public static DirectoryReadResult ReadForPreview(
            string path,
            CancellationToken cancellationToken,
            int limit = DefaultPreviewLimit,
            FileEntrySorter sorter = null,
            Action<IReadOnlyList<FileEntry>> firstBatch = null)
        {
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
            var entries = new List<FileEntry>(Math.Min(limit, 1024));
            var started = System.Diagnostics.Stopwatch.StartNew();
            var published = false;
            var outcome = Enumerate(path, cancellationToken, limit == int.MaxValue ? limit : limit + 1, item =>
            {
                if (entries.Count >= limit)
                    return false;

                entries.Add(new FileEntry(
                    item.Name,
                    item.FullPath,
                    item.IsDirectory,
                    item.IsReparsePoint,
                    item.IsDirectory ? (long?)null : item.Size,
                    item.ModifiedDate));
                if (!published && firstBatch != null && (entries.Count >= 128 || started.ElapsedMilliseconds >= 150))
                {
                    published = true;
                    firstBatch((sorter ?? FileEntrySorter.Default).Sort(entries));
                }
                return true;
            });

            cancellationToken.ThrowIfCancellationRequested();

            if (outcome.WasTruncated)
            {
                entries.Add(FileEntry.CreateNotice(
                    Strings.Format("TruncatedNotice", limit.ToString("N0"))));
            }

            return new DirectoryReadResult((sorter ?? FileEntrySorter.Default).Sort(entries), outcome.ErrorCode, outcome.WasTruncated);
        }

        public static DirectoryEnumerationOutcome Enumerate(
            string path,
            CancellationToken cancellationToken,
            int maximumItems,
            Func<DirectoryItem, bool> visitor,
            bool statisticsOnly = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A directory path is required.", nameof(path));
            if (visitor == null)
                throw new ArgumentNullException(nameof(visitor));
            if (maximumItems < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumItems));

            cancellationToken.ThrowIfCancellationRequested();

            var normalizedPath = NormalizeDirectoryPath(path);
            var searchPath = CombineSearchPattern(ToExtendedPath(normalizedPath));
            WIN32_FIND_DATA findData;
            var handle = FindFirstFileEx(
                searchPath,
                FINDEX_INFO_LEVELS.FindExInfoBasic,
                out findData,
                FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                FindFirstExLargeFetch);

            if (handle.IsInvalid && Marshal.GetLastWin32Error() == ErrorInvalidParameter)
            {
                handle.Dispose();
                handle = FindFirstFileEx(
                    searchPath,
                    FINDEX_INFO_LEVELS.FindExInfoBasic,
                    out findData,
                    FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                    IntPtr.Zero,
                    0);
            }

            using (handle)
            {
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    return error == ErrorFileNotFound
                        ? DirectoryEnumerationOutcome.Success
                        : new DirectoryEnumerationOutcome(error, false);
                }

                var visited = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!IsDotEntry(findData.cFileName))
                    {
                        if (visited >= maximumItems || !visitor(ToDirectoryItem(normalizedPath, findData, statisticsOnly)))
                            return new DirectoryEnumerationOutcome(0, true);

                        visited++;
                    }

                    if (!FindNextFile(handle, out findData))
                    {
                        var error = Marshal.GetLastWin32Error();
                        return error == ErrorNoMoreFiles
                            ? DirectoryEnumerationOutcome.Success
                            : new DirectoryEnumerationOutcome(error, false);
                    }
                }
            }
        }

        public static string GetErrorMessage(int errorCode)
        {
            return errorCode == 0 ? string.Empty : new Win32Exception(errorCode).Message;
        }

        private static string CombineSearchPattern(string path)
        {
            return path.EndsWith("\\", StringComparison.Ordinal) ? path + "*" : path + "\\*";
        }

        private static bool IsDotEntry(string name)
        {
            return name == "." || name == "..";
        }

        private static string NormalizeDirectoryPath(string path)
        {
            // QuickLook controls the CLR path switches. Native normalization also works
            // when the host retains .NET Framework's legacy MAX_PATH behavior.
            var buffer = new StringBuilder(512);
            var length = GetFullPathName(path, buffer.Capacity, buffer, IntPtr.Zero);
            if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length >= buffer.Capacity)
            {
                buffer = new StringBuilder(checked((int)length + 1));
                length = GetFullPathName(path, buffer.Capacity, buffer, IntPtr.Zero);
                if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            var fullPath = buffer.ToString();
            var trimmed = fullPath.TrimEnd('\\', '/');
            return trimmed.EndsWith(":", StringComparison.Ordinal) ? trimmed + "\\" : trimmed;
        }

        internal static uint ReadAttributes(string path) => GetFileAttributes(ToExtendedPath(NormalizeDirectoryPath(path)));

        internal static bool CanScanRoot(string path)
        {
            var normalized = ToExtendedPath(NormalizeDirectoryPath(path));
            var attributes = GetFileAttributes(normalized);
            if (attributes == uint.MaxValue) return true; // enumeration reports the access error
            if ((attributes & 0x400) == 0) return PathPolicy.CanScanDirectory(attributes, 0);
            using (var handle = FindFirstFileEx(normalized.TrimEnd('\\'), FINDEX_INFO_LEVELS.FindExInfoBasic,
                out var data, FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, 0))
                return !handle.IsInvalid && PathPolicy.CanScanDirectory(data.dwFileAttributes, data.dwReserved0);
        }

        private static DirectoryItem ToDirectoryItem(string parentPath, WIN32_FIND_DATA data, bool statisticsOnly)
        {
            var attributes = (FileAttributes)data.dwFileAttributes;
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            var size = isDirectory
                ? 0L
                : unchecked((long)(((ulong)data.nFileSizeHigh << 32) | data.nFileSizeLow));
            var fileTime = unchecked(((long)data.ftLastWriteTimeHigh << 32) | data.ftLastWriteTimeLow);
            DateTime modifiedDate;

            try
            {
                modifiedDate = statisticsOnly ? DateTime.MinValue : DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                modifiedDate = DateTime.MinValue;
            }

            return new DirectoryItem(
                data.cFileName,
                statisticsOnly && !isDirectory ? null : parentPath.TrimEnd('\\') + "\\" + data.cFileName,
                isDirectory,
                (attributes & FileAttributes.ReparsePoint) != 0,
                size,
                modifiedDate, data.dwFileAttributes, data.dwReserved0);
        }

        private static string ToExtendedPath(string path)
        {
            if (path.StartsWith("\\\\?\\", StringComparison.Ordinal))
                return path;
            if (path.StartsWith("\\\\", StringComparison.Ordinal))
                return "\\\\?\\UNC\\" + path.Substring(2);

            return "\\\\?\\" + path;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFullPathName(string path, int size, StringBuilder buffer, IntPtr filePart);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributes(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFindHandle FindFirstFileEx(
            string lpFileName,
            FINDEX_INFO_LEVELS fInfoLevelId,
            out WIN32_FIND_DATA lpFindFileData,
            FINDEX_SEARCH_OPS fSearchOp,
            IntPtr lpSearchFilter,
            int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextFile(SafeFindHandle hFindFile, out WIN32_FIND_DATA lpFindFileData);

        private enum FINDEX_INFO_LEVELS
        {
            FindExInfoStandard,
            FindExInfoBasic
        }

        private enum FINDEX_SEARCH_OPS
        {
            FindExSearchNameMatch
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public uint ftCreationTimeLow;
            public uint ftCreationTimeHigh;
            public uint ftLastAccessTimeLow;
            public uint ftLastAccessTimeHigh;
            public uint ftLastWriteTimeLow;
            public uint ftLastWriteTimeHigh;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeFindHandle()
                : base(true)
            {
            }

            protected override bool ReleaseHandle()
            {
                return FindClose(handle);
            }

            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool FindClose(IntPtr handle);
        }
    }

    internal readonly struct DirectoryItem
    {
        public DirectoryItem(
            string name,
            string fullPath,
            bool isDirectory,
            bool isReparsePoint,
            long size,
            DateTime modifiedDate, uint attributes = 0, uint reparseTag = 0)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            IsReparsePoint = isReparsePoint;
            Size = size;
            ModifiedDate = modifiedDate;
            Attributes = attributes;
            ReparseTag = reparseTag;
        }

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public bool IsReparsePoint { get; }
        public long Size { get; }
        public DateTime ModifiedDate { get; }
        public uint Attributes { get; }
        public uint ReparseTag { get; }
    }

    internal readonly struct DirectoryEnumerationOutcome
    {
        public static DirectoryEnumerationOutcome Success => new DirectoryEnumerationOutcome(0, false);

        public DirectoryEnumerationOutcome(int errorCode, bool wasTruncated)
        {
            ErrorCode = errorCode;
            WasTruncated = wasTruncated;
        }

        public int ErrorCode { get; }
        public bool WasTruncated { get; }
    }

    internal sealed class DirectoryReadResult
    {
        public DirectoryReadResult(IReadOnlyList<FileEntry> entries, int errorCode, bool wasTruncated)
        {
            Entries = entries;
            ErrorCode = errorCode;
            WasTruncated = wasTruncated;
        }

        public IReadOnlyList<FileEntry> Entries { get; }
        public int ErrorCode { get; }
        public bool WasTruncated { get; }
    }
}
