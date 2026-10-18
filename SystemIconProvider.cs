using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickLook.Plugin.FolderViewer
{
    internal static class SystemIconProvider
    {
        private const int MaximumCachedIcons = 512;
        private const int MaximumExtensionLength = 24;
        private const uint FileAttributeDirectory = 0x10;
        private const uint FileAttributeNormal = 0x80;
        private const uint ShgfiIcon = 0x000000100;
        private const uint ShgfiSmallIcon = 0x000000001;
        private const uint ShgfiUseFileAttributes = 0x000000010;

        private static readonly Dictionary<string, Task<ImageSource>> Cache =
            new Dictionary<string, Task<ImageSource>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();
        private static readonly BlockingCollection<Action> Work = new BlockingCollection<Action>(128);
        public static readonly ImageSource Placeholder = CreatePlaceholder();

        static SystemIconProvider()
        {
            var thread = new Thread(() =>
            {
                var initialized = CoInitializeEx(IntPtr.Zero, 2) >= 0;
                try { foreach (var work in Work.GetConsumingEnumerable()) work(); }
                finally { if (initialized) CoUninitialize(); }
            }) { IsBackground = true, Name = "FolderViewer icons" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public static ImageSource GetIcon(string fullPath, bool isFolder)
        {
            var task = GetIconAsync(fullPath, isFolder, 16);
            return task.Status == TaskStatus.RanToCompletion ? task.Result : Placeholder;
        }

        public static Task<ImageSource> GetIconAsync(string fullPath, bool isFolder, int pixels)
        {
            pixels = pixels <= 16 ? 16 : pixels <= 24 ? 24 : pixels <= 32 ? 32 : pixels <= 48 ? 48 : 64;
            var key = isFolder ? "<folder>" : GetExtensionKey(fullPath);
            var cacheKey = key + ":" + pixels;
            lock (CacheLock)
            {
                if (Cache.TryGetValue(cacheKey, out var cached))
                    return cached;
                if (Cache.Count >= MaximumCachedIcons)
                    Cache.Clear();
                var completion = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!Work.TryAdd(() =>
                {
                    try { completion.TrySetResult(LoadIcon(key, isFolder, pixels) ?? Placeholder); }
                    catch { completion.TrySetResult(Placeholder); }
                })) return Task.FromResult(Placeholder);
                Cache[cacheKey] = completion.Task;
                return completion.Task;
            }
        }

        private static ImageSource CreatePlaceholder()
        {
            var drawing = new GeometryDrawing(Brushes.LightGray, new Pen(Brushes.Gray, 1),
                new RectangleGeometry(new Rect(2, 1, 11, 14)));
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }

        private static string GetExtensionKey(string path)
        {
            try
            {
                var extension = Path.GetExtension(path);
                return string.IsNullOrEmpty(extension) || extension.Length > MaximumExtensionLength
                    ? "<file>"
                    : extension;
            }
            catch (ArgumentException)
            {
                return "<file>";
            }
        }

        private static ImageSource LoadIcon(string key, bool isFolder, int pixels)
        {
            var attributes = isFolder ? FileAttributeDirectory : FileAttributeNormal;
            var lookupName = isFolder ? "folder" : "file" + (key.StartsWith("<", StringComparison.Ordinal) ? string.Empty : key);
            SHFILEINFO info;
            var result = SHGetFileInfo(
                lookupName,
                attributes,
                out info,
                (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                ShgfiIcon | (pixels <= 16 ? ShgfiSmallIcon : 0) | ShgfiUseFileAttributes);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(pixels, pixels));
                source.Freeze();
                return source;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is ExternalException)
            {
                return null;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            out SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }
    }
}
