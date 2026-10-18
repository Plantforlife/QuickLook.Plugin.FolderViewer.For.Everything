using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace QuickLook.Plugin.FolderViewer
{
    internal static class DirectoryStatisticsScanner
    {
        public static DirectoryStatistics Scan(string rootPath, CancellationToken cancellationToken,
            Action<DirectoryStatistics> progress = null)
        {
            return new Session(rootPath).Step(cancellationToken, TimeSpan.MaxValue, long.MaxValue, progress);
        }

        public static bool IsNetworkPath(string path) => PathPolicy.IsNetwork(path);

        // A paused directory resumes by skipping previously seen entries. No find handle
        // is retained while the user decides whether to continue.
        internal sealed class Session
        {
            private readonly Stack<Cursor> _pending = new Stack<Cursor>();
            private long _directories, _files, _size;
            private int _inaccessible, _skipped;
            private bool _rootChecked;

            public Session(string path) { _pending.Push(new Cursor(path)); }

            public DirectoryStatistics Step(CancellationToken token, TimeSpan budget, long itemBudget,
                Action<DirectoryStatistics> progress = null)
            {
                var elapsed = Stopwatch.StartNew();
                var lastProgress = TimeSpan.Zero;
                long processed = 0;
                token.ThrowIfCancellationRequested();
                if (!_rootChecked && _pending.Count != 0)
                {
                    _rootChecked = true;
                    if (!DirectoryEnumerator.CanScanRoot(_pending.Peek().Path))
                    {
                        _pending.Clear();
                        _skipped++;
                    }
                }
                while (_pending.Count != 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (processed > 0 && (elapsed.Elapsed >= budget || processed >= itemBudget)) break;
                    var cursor = _pending.Pop();
                    var visited = 0;
                    var outcome = DirectoryEnumerator.Enumerate(cursor.Path, token, int.MaxValue, item =>
                    {
                        if (visited++ < cursor.Offset) return true;
                        if (processed > 0 && (elapsed.Elapsed >= budget || processed >= itemBudget)) return false;
                        processed++;
                        if (item.IsDirectory)
                        {
                            _directories++;
                            if (PathPolicy.CanScanDirectory(item.Attributes, item.ReparseTag))
                                _pending.Push(new Cursor(item.FullPath));
                            else _skipped++;
                        }
                        else
                        {
                            _files++;
                            if (item.Size > 0) _size = _size > long.MaxValue - item.Size ? long.MaxValue : _size + item.Size;
                        }
                        if ((processed & 255) == 0 && elapsed.Elapsed - lastProgress >= TimeSpan.FromMilliseconds(300))
                        {
                            progress?.Invoke(Snapshot(false));
                            lastProgress = elapsed.Elapsed;
                        }
                        return true;
                    }, statisticsOnly: true);
                    if (outcome.WasTruncated)
                    {
                        cursor.Offset = visited - 1;
                        _pending.Push(cursor);
                        break;
                    }
                    if (outcome.ErrorCode != 0) _inaccessible++;
                }
                var result = Snapshot(_pending.Count == 0);
                progress?.Invoke(result);
                return result;
            }

            private DirectoryStatistics Snapshot(bool complete) =>
                new DirectoryStatistics(_directories, _files, _size, _inaccessible, complete, _skipped);

            private sealed class Cursor
            {
                public Cursor(string path) { Path = path; }
                public string Path { get; }
                public int Offset;
            }
        }
    }

    internal readonly struct DirectoryStatistics
    {
        public DirectoryStatistics(long directoryCount, long fileCount, long totalSize,
            int inaccessibleDirectoryCount, bool isComplete, int skippedDirectoryCount = 0)
        {
            DirectoryCount = directoryCount;
            FileCount = fileCount;
            TotalSize = totalSize;
            InaccessibleDirectoryCount = inaccessibleDirectoryCount;
            IsComplete = isComplete;
            SkippedDirectoryCount = skippedDirectoryCount;
        }
        public long DirectoryCount { get; }
        public long FileCount { get; }
        public long TotalSize { get; }
        public int InaccessibleDirectoryCount { get; }
        public int SkippedDirectoryCount { get; }
        public bool IsComplete { get; }
    }
}
