using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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
            int limit = DefaultPreviewLimit)
        {
            var entries = new List<FileEntry>(Math.Min(limit, 1024));
            var outcome = Enumerate(path, cancellationToken, limit + 1, item =>
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
                return true;
            });

            entries.Sort();

            if (outcome.WasTruncated)
            {
                entries.Add(FileEntry.CreateNotice(
                    Strings.Format("TruncatedNotice", limit.ToString("N0"))));
            }

            return new DirectoryReadResult(entries, outcome.ErrorCode, outcome.WasTruncated);
        }

        public static DirectoryEnumerationOutcome Enumerate(
            string path,
            CancellationToken cancellationToken,
            int maximumItems,
            Func<DirectoryItem, bool> visitor)
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
                        if (visited >= maximumItems || !visitor(ToDirectoryItem(normalizedPath, findData)))
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
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                return fullPath;

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static DirectoryItem ToDirectoryItem(string parentPath, WIN32_FIND_DATA data)
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
                modifiedDate = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                modifiedDate = DateTime.MinValue;
            }

            return new DirectoryItem(
                data.cFileName,
                Path.Combine(parentPath, data.cFileName),
                isDirectory,
                (attributes & FileAttributes.ReparsePoint) != 0,
                size,
                modifiedDate);
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
            DateTime modifiedDate)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            IsReparsePoint = isReparsePoint;
            Size = size;
            ModifiedDate = modifiedDate;
        }

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public bool IsReparsePoint { get; }
        public long Size { get; }
        public DateTime ModifiedDate { get; }
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
