// Legacy Everything IPC constants and structures are derived from Everything_IPC.h.
// Copyright (C) 2022 David Carpenter / voidtools, used under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;

namespace QuickLook.Plugin.FolderViewer
{
    internal static class EverythingLegacyIpc
    {
        private const string WindowClass = "EVERYTHING_TASKBAR_NOTIFICATION";
        private const string WindowClass15 = "EVERYTHING_TASKBAR_NOTIFICATION_(1.5a)";
        private static readonly SemaphoreSlim ReceiverLock = new SemaphoreSlim(1, 1);
        private static ReceiverWindow _receiver;

        public static bool TryGetFolderSize(
            string path,
            CancellationToken cancellationToken,
            out long size)
        {
            size = 0;
            var everythingWindow = FindWindow(WindowClass15, null);
            if (everythingWindow == IntPtr.Zero)
                everythingWindow = FindWindow(WindowClass, null);
            if (everythingWindow == IntPtr.Zero)
                return false;

            var receiver = GetReceiver(cancellationToken);
            return receiver != null && receiver.TryQuery(everythingWindow, path, cancellationToken, out size);
        }

        public static bool TryGetDescendantCounts(
            string path,
            CancellationToken cancellationToken,
            out long directoryCount,
            out long fileCount)
        {
            directoryCount = 0;
            fileCount = 0;
            var everythingWindow = FindWindow(WindowClass15, null);
            if (everythingWindow == IntPtr.Zero)
                everythingWindow = FindWindow(WindowClass, null);
            if (everythingWindow == IntPtr.Zero)
                return false;

            var receiver = GetReceiver(cancellationToken);
            if (receiver == null)
                return false;

            var normalizedPath = path.TrimEnd('\\', '/');

            // A folder outside Everything's index (e.g. an exFAT drive that is not added as a
            // folder index, or an excluded folder) makes the descendant queries "succeed" with
            // 0 results. Only trust the counts if Everything knows the folder itself.
            if (!receiver.TryQueryCount(
                    everythingWindow,
                    "folder:wfn:\"" + normalizedPath + "\"",
                    cancellationToken,
                    out var selfCount) ||
                selfCount == 0)
            {
                return false;
            }

            var pathSearch = "path:\"" + normalizedPath + "\\\"";
            var success = receiver.TryQueryCount(
                       everythingWindow,
                       "folder: " + pathSearch,
                       cancellationToken,
                       out directoryCount) &&
                   receiver.TryQueryCount(
                       everythingWindow,
                       "file: " + pathSearch,
                       cancellationToken,
                       out fileCount);
            return success;
        }

        private static ReceiverWindow GetReceiver(CancellationToken cancellationToken)
        {
            ReceiverLock.Wait(cancellationToken);
            try
            {
                if (_receiver == null)
                {
                    var candidate = new ReceiverWindow();
                    if (!candidate.IsReady)
                    {
                        candidate.Shutdown();
                        return null;
                    }
                    _receiver = candidate;
                }

                return _receiver;
            }
            finally
            {
                ReceiverLock.Release();
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        private sealed class ReceiverWindow
        {
            private const uint EverythingCopyDataQuery2W = 18;
            private const uint EverythingRequestSize = 0x10;
            private const uint MatchDiacritics = 0x10;
            private const uint SortNameAscending = 1;
            private const uint SmtoBlockAndAbortIfHung = 0x0001 | 0x0002;
            private const uint WmCopyData = 0x004A;
            private const int QueryTimeoutMilliseconds = 250;

            private readonly AutoResetEvent _ready = new AutoResetEvent(false);
            private readonly AutoResetEvent _reply = new AutoResetEvent(false);
            private readonly SemaphoreSlim _queryLock = new SemaphoreSlim(1, 1);
            private readonly Thread _thread;
            private volatile Dispatcher _dispatcher;
            private IntPtr _handle;
            private uint _nextRequestId;
            private volatile uint _pendingRequestId;
            private bool _replyHasSize;
            private bool _replyReceived;
            private long _replySize;
            private uint _replyTotalItems;
            private volatile bool _stopRequested;

            public ReceiverWindow()
            {
                _thread = new Thread(MessageThread)
                {
                    IsBackground = true,
                    Name = "FolderViewer Everything IPC",
                    Priority = ThreadPriority.BelowNormal
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                IsReady = _ready.WaitOne(1000) && _handle != IntPtr.Zero;
            }

            public bool IsReady { get; }

            public void Shutdown()
            {
                _stopRequested = true;
                var dispatcher = _dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                {
                    try
                    {
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                if (_thread != Thread.CurrentThread)
                    _thread.Join(500);
            }

            public bool TryQuery(
                IntPtr everythingWindow,
                string path,
                CancellationToken cancellationToken,
                out long size)
            {
                var success = TryQueryCore(
                    everythingWindow,
                    "folder:wfn:\"" + path + "\"",
                    EverythingRequestSize,
                    cancellationToken,
                    out size,
                    out _);
                return success;
            }

            public bool TryQueryCount(
                IntPtr everythingWindow,
                string search,
                CancellationToken cancellationToken,
                out long count)
            {
                count = 0;
                var success = TryQueryCore(
                    everythingWindow,
                    search,
                    0,
                    cancellationToken,
                    out _,
                    out var totalItems);
                if (success)
                    count = totalItems;
                return success;
            }

            private bool TryQueryCore(
                IntPtr everythingWindow,
                string search,
                uint requestFlags,
                CancellationToken cancellationToken,
                out long size,
                out uint totalItems)
            {
                size = 0;
                totalItems = 0;
                if (!IsReady || _handle == IntPtr.Zero)
                    return false;
                cancellationToken.ThrowIfCancellationRequested();

                _queryLock.Wait(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _replyHasSize = false;
                    _replyReceived = false;
                    _replySize = 0;
                    _replyTotalItems = 0;
                    _reply.Reset();
                    _pendingRequestId = unchecked(++_nextRequestId);
                    if (_pendingRequestId == 0)
                        _pendingRequestId = unchecked(++_nextRequestId);

                    var searchBytes = Encoding.Unicode.GetBytes(search + "\0");
                    var query = new byte[28 + searchBytes.Length];
                    WriteUInt32(query, 0, unchecked((uint)_handle.ToInt64()));
                    WriteUInt32(query, 4, _pendingRequestId);
                    WriteUInt32(query, 8, MatchDiacritics);
                    WriteUInt32(query, 12, 0);
                    WriteUInt32(query, 16, 1);
                    WriteUInt32(query, 20, requestFlags);
                    WriteUInt32(query, 24, SortNameAscending);
                    Buffer.BlockCopy(searchBytes, 0, query, 28, searchBytes.Length);

                    var pinned = GCHandle.Alloc(query, GCHandleType.Pinned);
                    try
                    {
                        var copyData = new COPYDATASTRUCT
                        {
                            dwData = new UIntPtr(EverythingCopyDataQuery2W),
                            cbData = query.Length,
                            lpData = pinned.AddrOfPinnedObject()
                        };
                        IntPtr sendResult;
                        var sent = SendMessageTimeout(
                            everythingWindow,
                            WmCopyData,
                            _handle,
                            ref copyData,
                            SmtoBlockAndAbortIfHung,
                            QueryTimeoutMilliseconds,
                            out sendResult);
                        if (sent == IntPtr.Zero)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return false;
                        }

                        var signaled = WaitHandle.WaitAny(
                            new[] { _reply, cancellationToken.WaitHandle },
                            QueryTimeoutMilliseconds);
                        if (signaled == 1)
                            cancellationToken.ThrowIfCancellationRequested();
                        if (signaled != 0 || !_replyReceived)
                            return false;
                        if (requestFlags == EverythingRequestSize && !_replyHasSize)
                            return false;

                        size = _replySize;
                        totalItems = _replyTotalItems;
                        return true;
                    }
                    finally
                    {
                        _pendingRequestId = 0;
                        pinned.Free();
                    }
                }
                finally
                {
                    _queryLock.Release();
                }
            }

            private void MessageThread()
            {
                try
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    var parameters = new HwndSourceParameters(
                        "QuickLook.Plugin.FolderViewer.EverythingReceiver." + Guid.NewGuid().ToString("N"))
                    {
                        Width = 0,
                        Height = 0,
                        WindowStyle = 0
                    };
                    using (var source = new HwndSource(parameters))
                    {
                        source.AddHook(WindowProcedure);
                        _handle = source.Handle;
                        ChangeWindowMessageFilterEx(_handle, WmCopyData, 1, IntPtr.Zero);
                        _ready.Set();
                        if (!_stopRequested)
                            Dispatcher.Run();
                    }
                }
                catch
                {
                    _handle = IntPtr.Zero;
                    _ready.Set();
                }
                finally
                {
                    _handle = IntPtr.Zero;
                    _dispatcher = null;
                }
            }

            private IntPtr WindowProcedure(
                IntPtr hwnd,
                int message,
                IntPtr wParam,
                IntPtr lParam,
                ref bool handled)
            {
                if (message != WmCopyData || lParam == IntPtr.Zero)
                    return IntPtr.Zero;

                var copyData = (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT));
                if (unchecked((uint)copyData.dwData.ToUInt64()) != _pendingRequestId ||
                    copyData.lpData == IntPtr.Zero ||
                    copyData.cbData < 20)
                    return IntPtr.Zero;

                _replyTotalItems = unchecked((uint)Marshal.ReadInt32(copyData.lpData, 0));
                var itemCount = unchecked((uint)Marshal.ReadInt32(copyData.lpData, 4));
                if (itemCount > 1 || (itemCount != 0 && copyData.cbData < 28))
                    return IntPtr.Zero;

                var returnedRequestFlags = unchecked((uint)Marshal.ReadInt32(copyData.lpData, 12));
                var dataOffset = itemCount == 0 || copyData.cbData < 28
                    ? uint.MaxValue
                    : unchecked((uint)Marshal.ReadInt32(copyData.lpData, 24));
                if (itemCount != 0 &&
                    (returnedRequestFlags & EverythingRequestSize) != 0 &&
                    dataOffset >= 28 &&
                    dataOffset <= int.MaxValue &&
                    dataOffset <= copyData.cbData - sizeof(long))
                {
                    _replySize = Marshal.ReadInt64(copyData.lpData, (int)dataOffset);
                    _replyHasSize = _replySize >= 0;
                }
                else
                {
                    _replySize = 0;
                    _replyHasSize = false;
                }

                _replyReceived = true;
                _reply.Set();
                handled = true;
                return new IntPtr(1);
            }

            private static void WriteUInt32(byte[] buffer, int offset, uint value)
            {
                buffer[offset] = (byte)value;
                buffer[offset + 1] = (byte)(value >> 8);
                buffer[offset + 2] = (byte)(value >> 16);
                buffer[offset + 3] = (byte)(value >> 24);
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr SendMessageTimeout(
                IntPtr hWnd,
                uint msg,
                IntPtr wParam,
                ref COPYDATASTRUCT lParam,
                uint flags,
                uint timeout,
                out IntPtr result);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool ChangeWindowMessageFilterEx(
                IntPtr hwnd,
                uint message,
                uint action,
                IntPtr changeFilterStruct);

            [StructLayout(LayoutKind.Sequential)]
            private struct COPYDATASTRUCT
            {
                public UIntPtr dwData;
                public int cbData;
                public IntPtr lpData;
            }
        }
    }
}
