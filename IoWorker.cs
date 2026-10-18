using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace QuickLook.Plugin.FolderViewer
{
    /// <summary>Bounds stuck filesystem calls across all previews, including closed ones.</summary>
    internal sealed class IoWorker
    {
        private static readonly Lazy<IoWorker> PreviewWorker = new Lazy<IoWorker>(() => new IoWorker(2, 32, "FolderViewer enumeration"));
        private static readonly Lazy<IoWorker> StatisticsWorker = new Lazy<IoWorker>(() => new IoWorker(1, 8, "FolderViewer statistics"));
        private static readonly Lazy<IoWorker> IndexWorker = new Lazy<IoWorker>(() => new IoWorker(2, 64, "FolderViewer index"));
        public static IoWorker Preview => PreviewWorker.Value;
        public static IoWorker Statistics => StatisticsWorker.Value;
        public static IoWorker Index => IndexWorker.Value;
        private readonly BlockingCollection<Action> _queue;

        internal IoWorker(int workers, int capacity, string name)
        {
            _queue = new BlockingCollection<Action>(capacity);
            for (var i = 0; i < workers; i++)
            {
                var thread = new Thread(() =>
                {
                    foreach (var action in _queue.GetConsumingEnumerable()) action();
                }) { IsBackground = true, Name = name };
                thread.Start();
            }
        }

        public async Task<T> RunAsync<T>(Func<CancellationToken, T> action,
            CancellationToken cancellation, TimeSpan timeout)
        {
            using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                lifetime.CancelAfter(timeout);
                var token = lifetime.Token;
                var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (token.Register(() => completion.TrySetCanceled()))
                {
                    var work = Enqueue(action, token);
                    _ = work.ContinueWith(t =>
                    {
                        if (t.IsCanceled) completion.TrySetCanceled();
                        else if (t.IsFaulted) completion.TrySetException(t.Exception.InnerExceptions);
                        else completion.TrySetResult(t.Result);
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    try { return await completion.Task.ConfigureAwait(false); }
                    catch (OperationCanceledException)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        throw new TimeoutException(Strings.Get("FolderTimedOut"));
                    }
                }
            }
        }

        // Unlike RunAsync, this task completes only when the worker has actually exited.
        // Persistent IPC connections must retain their lock until then, even after a timeout.
        internal Task<T> Enqueue<T>(Func<CancellationToken, T> action, CancellationToken token)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_queue.TryAdd(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    using (var thread = OpenThread(1, false, GetCurrentThreadId()))
                    using (token.Register(() => { if (!thread.IsInvalid) CancelSynchronousIo(thread); }))
                        completion.TrySetResult(action(token));
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception error) { completion.TrySetException(error); }
            })) completion.TrySetException(new InvalidOperationException(Strings.Get("BusyFolder")));
            return completion.Task;
        }

        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] private static extern SafeWaitHandle OpenThread(uint access, bool inherit, uint id);
        [DllImport("kernel32.dll")] private static extern bool CancelSynchronousIo(SafeWaitHandle thread);
    }

    internal sealed class BackgroundIoScope : IDisposable
    {
        private readonly bool _active = SetThreadPriority(GetCurrentThread(), 0x10000);
        public void Dispose() { if (_active) SetThreadPriority(GetCurrentThread(), 0x20000); }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] private static extern bool SetThreadPriority(IntPtr thread, int priority);
    }
}
