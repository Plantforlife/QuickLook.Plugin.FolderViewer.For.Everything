// The Everything 1.5 SDK3 wire protocol used here is documented by voidtools.
// The SDK reference implementation is Copyright (C) 2024 David Carpenter and MIT licensed.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace QuickLook.Plugin.FolderViewer
{
    internal sealed class EverythingFolderSizeProvider : IDisposable
    {
        private const int QueryBudgetMilliseconds = 500;
        private readonly Dictionary<string, FolderSizeQueryResult> _cache =
            new Dictionary<string, FolderSizeQueryResult>(StringComparer.OrdinalIgnoreCase);
        private readonly EverythingEndpoint[] _endpoints;
        private readonly SemaphoreSlim _queryLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly CancellationToken _lifetimeToken;
        private Sdk3Client _client;
        private EverythingEndpoint _preferred;
        private DateTime _retryAfter;
        private readonly bool _allowLegacy;
        private volatile bool _disposed;

        public EverythingFolderSizeProvider()
        {
            _lifetimeToken = _lifetime.Token;
            _endpoints = EverythingEndpoint.Defaults();
            _allowLegacy = true;
        }

        internal EverythingFolderSizeProvider(params string[] pipeNames)
        {
            _lifetimeToken = _lifetime.Token;
            if (pipeNames == null || pipeNames.Length == 0)
                throw new ArgumentException("At least one pipe name is required.", nameof(pipeNames));

            _endpoints = Array.ConvertAll(pipeNames, p => new EverythingEndpoint(p, null));
        }

        public async Task<FolderSizeQueryResult> QueryAsync(string path, CancellationToken cancellationToken)
        {
            if (_disposed) return FolderSizeQueryResult.Unavailable;
            if (string.IsNullOrWhiteSpace(path))
                return FolderSizeQueryResult.Unavailable;

            path = PathPolicy.ForQuery(path);
            var queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
            var lockTaken = false;
            var disposeCancellation = true;
            try
            {
                try
                {
                    await _queryLock.WaitAsync(queryCancellation.Token).ConfigureAwait(false);
                    lockTaken = true;
                    // Waiting for another row is not time spent communicating with Everything.
                    queryCancellation.CancelAfter(QueryBudgetMilliseconds);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return FolderSizeQueryResult.Unavailable;
                }

                if (_disposed)
                    return FolderSizeQueryResult.Unavailable;
                if (_cache.TryGetValue(path, out var cached))
                    return cached;
                if (DateTime.UtcNow < _retryAfter) return FolderSizeQueryResult.Unavailable;

                var queryTask = IoWorker.Index.Enqueue(token => QueryCore(path, token), queryCancellation.Token);
                var completedTask = await Task.WhenAny(
                    queryTask,
                    Task.Delay(Timeout.Infinite, queryCancellation.Token)).ConfigureAwait(false);
                if (completedTask != queryTask)
                {
                    disposeCancellation = false;
                    ObserveAndDispose(queryTask, queryCancellation);
                    // A timed-out OS call may still be unwinding. Keep ownership of the
                    // persistent connection until that worker actually exits.
                    lockTaken = false;
                    _ = queryTask.ContinueWith(t => _queryLock.Release(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    cancellationToken.ThrowIfCancellationRequested();
                    return FolderSizeQueryResult.Unavailable;
                }

                FolderSizeQueryResult result;
                try
                {
                    result = await queryTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return FolderSizeQueryResult.Unavailable;
                }
                catch (InvalidOperationException) { return FolderSizeQueryResult.Unavailable; }

                cancellationToken.ThrowIfCancellationRequested();
                if (result.IsSuccess)
                {
                    if (_cache.Count >= 2048) _cache.Clear();
                    _cache[path] = result;
                }
                return result;
            }
            finally
            {
                if (lockTaken)
                    _queryLock.Release();
                if (disposeCancellation)
                    queryCancellation.Dispose();
            }
        }

        public async Task<FolderAggregateQueryResult> QueryStatisticsAsync(
            string path,
            CancellationToken cancellationToken)
        {
            if (_disposed) return default(FolderAggregateQueryResult);
            var queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
            queryCancellation.CancelAfter(QueryBudgetMilliseconds);
            var disposeCancellation = true;
            var queryPath = path;
            var sizeResult = FolderSizeQueryResult.Unavailable;
            var counts = CountQueryResult.Unavailable;
            try
            {
                var resolveTask = IoWorker.Preview.RunAsync(
                    token => ResolveAggregateQueryPath(path), queryCancellation.Token, TimeSpan.FromMilliseconds(500));
                var resolvedTask = await Task.WhenAny(
                    resolveTask,
                    Task.Delay(Timeout.Infinite, queryCancellation.Token)).ConfigureAwait(false);
                if (resolvedTask != resolveTask)
                {
                    disposeCancellation = false;
                    ObserveAndDispose(resolveTask, queryCancellation);
                    cancellationToken.ThrowIfCancellationRequested();
                    return default(FolderAggregateQueryResult);
                }

                queryPath = await resolveTask.ConfigureAwait(false);
                sizeResult = await QueryAsync(queryPath, queryCancellation.Token).ConfigureAwait(false);
                queryCancellation.Token.ThrowIfCancellationRequested();

                var instance = _preferred?.Instance ?? Environment.GetEnvironmentVariable("FOLDERVIEWER_EVERYTHING_INSTANCE");
                var countTask = IoWorker.Index.Enqueue(token =>
                {
                    var success = EverythingLegacyIpc.TryGetDescendantCounts(
                        queryPath,
                        token,
                        out var directoryCount,
                        out var fileCount, instance);
                    return new CountQueryResult(success, directoryCount, fileCount);
                }, queryCancellation.Token);
                var completedTask = await Task.WhenAny(
                    countTask,
                    Task.Delay(Timeout.Infinite, queryCancellation.Token)).ConfigureAwait(false);
                if (completedTask != countTask)
                {
                    disposeCancellation = false;
                    ObserveAndDispose(countTask, queryCancellation);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else
                {
                    counts = await countTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (TimeoutException) { }
            catch (InvalidOperationException) { }
            finally
            {
                if (disposeCancellation)
                    queryCancellation.Dispose();
            }

            return new FolderAggregateQueryResult(
                sizeResult.IsSuccess,
                sizeResult.Size,
                counts.IsSuccess,
                counts.DirectoryCount,
                counts.FileCount,
                sizeResult.Source);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _client?.Dispose();
            _lifetime.Dispose();
        }

        private FolderSizeQueryResult QueryCore(string path, CancellationToken cancellationToken)
        {
            var queryPath = path;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var retryResolvedPath = false;
                var responsive = false;
                var endpoints = new List<EverythingEndpoint>();
                if (_preferred != null) endpoints.Add(_preferred);
                foreach (var endpoint in _endpoints)
                    if (!ReferenceEquals(endpoint, _preferred)) endpoints.Add(endpoint);
                foreach (var endpoint in endpoints)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_client == null || !ReferenceEquals(endpoint, _preferred))
                    {
                        _client?.Dispose();
                        _client = new Sdk3Client(endpoint.Pipe);
                    }
                    var success = _client.TryQuery(queryPath, cancellationToken, out var size);
                    if (!success && !_client.Responded)
                    {
                        _client.Dispose();
                        _client = new Sdk3Client(endpoint.Pipe);
                        success = _client.TryQuery(queryPath, cancellationToken, out size);
                    }
                    if (_client.Responded)
                    {
                        responsive = true;
                        _preferred = endpoint;
                    }
                    if (!success)
                    {
                        if (responsive) break; // an unindexed path is not an endpoint failure
                        _client.Dispose();
                        _client = null;
                        continue;
                    }
                    if (size != 0 || !IsReparsePoint(queryPath))
                        return new FolderSizeQueryResult(true, size, "Everything 1.5 index");
                    if (attempt == 0 && TryResolveLocalPath(queryPath, out var resolvedPath))
                    {
                        queryPath = resolvedPath;
                        retryResolvedPath = true;
                        break;
                    }

                    return FolderSizeQueryResult.Unavailable;
                }

                if (retryResolvedPath)
                    continue;

                cancellationToken.ThrowIfCancellationRequested();
                if (_allowLegacy && EverythingLegacyIpc.TryGetFolderSize(
                    queryPath,
                    cancellationToken,
                    out var legacySize, _preferred?.Instance ?? Environment.GetEnvironmentVariable("FOLDERVIEWER_EVERYTHING_INSTANCE")))
                {
                    if (legacySize != 0 || !IsReparsePoint(queryPath))
                        return new FolderSizeQueryResult(true, legacySize, "Everything index");
                    if (attempt == 0 && TryResolveLocalPath(queryPath, out var resolvedLegacyPath))
                    {
                        queryPath = resolvedLegacyPath;
                        continue;
                    }
                }

                if (!responsive) _retryAfter = DateTime.UtcNow.AddSeconds(2);
                return FolderSizeQueryResult.Unavailable;
            }

            return FolderSizeQueryResult.Unavailable;
        }

        internal static string ResolveAggregateQueryPath(string path)
        {
            path = PathPolicy.ForQuery(path);
            if (DirectoryStatisticsScanner.IsNetworkPath(path) || !IsReparsePoint(path))
                return path;

            return TryResolveLocalPath(path, out var resolvedPath) ? resolvedPath : path;
        }

        internal static bool TryQuerySdk3Pipe(string pipeName, string path,
            CancellationToken cancellationToken, out long size)
        {
            using (var client = new Sdk3Client(pipeName))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(QueryBudgetMilliseconds);
                try { return client.TryQuery(path, timeout.Token, out size); }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    size = 0;
                    return false;
                }
            }
        }
        private static bool IsReparsePoint(string path)
        {
            try
            {
                var attributes = DirectoryEnumerator.ReadAttributes(path);
                return attributes != uint.MaxValue && (attributes & 0x400) != 0;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is System.ComponentModel.Win32Exception ||
                exception is System.Security.SecurityException)
            {
                return false;
            }
        }

        private static bool TryResolveLocalPath(string path, out string resolvedPath)
        {
            resolvedPath = null;
            if (DirectoryStatisticsScanner.IsNetworkPath(path))
                return false;
            var attributes = DirectoryEnumerator.ReadAttributes(path);
            if (attributes != uint.MaxValue && (attributes & PathPolicy.RecallOrOffline) != 0)
                return false;

            using (var handle = CreateFile(
                path,
                0,
                1 | 2 | 4,
                IntPtr.Zero,
                3,
                0x02000000,
                IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    return false;

                var buffer = new StringBuilder(512);
                var length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
                if (length == 0)
                    return false;
                if (length >= buffer.Capacity)
                {
                    buffer = new StringBuilder(checked((int)length + 1));
                    length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
                    if (length == 0 || length >= buffer.Capacity)
                        return false;
                }

                var value = PathPolicy.ForQuery(buffer.ToString());

                if (string.Equals(value, path, StringComparison.OrdinalIgnoreCase))
                    return false;

                resolvedPath = value;
                return true;
            }
        }

        private static void ObserveAndDispose(Task task, CancellationTokenSource cancellation)
        {
            task.ContinueWith(
                completedTask =>
                {
                    var ignored = completedTask.Exception;
                    cancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            StringBuilder filePath,
            int filePathSize,
            uint flags);

        private readonly struct CountQueryResult
        {
            public static CountQueryResult Unavailable => new CountQueryResult(false, 0, 0);

            public CountQueryResult(bool isSuccess, long directoryCount, long fileCount)
            {
                IsSuccess = isSuccess;
                DirectoryCount = directoryCount;
                FileCount = fileCount;
            }

            public bool IsSuccess { get; }
            public long DirectoryCount { get; }
            public long FileCount { get; }
        }
    }

    internal readonly struct FolderSizeQueryResult
    {
        public static FolderSizeQueryResult Unavailable => new FolderSizeQueryResult(false, 0, null);

        public FolderSizeQueryResult(bool isSuccess, long size, string source)
        {
            IsSuccess = isSuccess;
            Size = size;
            Source = source;
        }

        public bool IsSuccess { get; }
        public long Size { get; }
        public string Source { get; }
    }

    internal readonly struct FolderAggregateQueryResult
    {
        public FolderAggregateQueryResult(
            bool hasSize,
            long size,
            bool hasCounts,
            long directoryCount,
            long fileCount,
            string source)
        {
            HasSize = hasSize;
            Size = size;
            HasCounts = hasCounts;
            DirectoryCount = directoryCount;
            FileCount = fileCount;
            Source = source;
        }

        public bool HasSize { get; }
        public long Size { get; }
        public bool HasCounts { get; }
        public long DirectoryCount { get; }
        public long FileCount { get; }
        public string Source { get; }
    }
}
