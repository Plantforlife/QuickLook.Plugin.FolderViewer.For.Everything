using System;
using System.IO;
using System.Runtime.InteropServices;

namespace QuickLook.Plugin.FolderViewer
{
    internal static class PathPolicy
    {
        internal const uint CloudTag = 0x9000001a;
        internal const uint RecallOrOffline = 0x1000 | 0x40000 | 0x400000;

        public static string ForQuery(string path)
        {
            if (path == null) return null;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length > 5 && path[5] == ':')
                return path.Substring(4);
            return path;
        }

        public static bool IsNetwork(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = ForQuery(path);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return false; // local volume GUID
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            var root = path.Length >= 3 && path[1] == ':' ? path.Substring(0, 3) : Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && GetDriveType(root) == 4;
        }

        public static bool IsRoot(string path)
        {
            path = ForQuery(path)?.TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(path)) return true;
            if (path.Length == 2 && path[1] == ':') return true;
            if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            var serverEnd = path.IndexOf('\\', 2);
            return serverEnd < 0 || path.IndexOf('\\', serverEnd + 1) < 0;
        }

        public static bool CanScanDirectory(uint attributes, uint reparseTag)
        {
            if ((attributes & RecallOrOffline) != 0) return false;
            if ((attributes & (uint)FileAttributes.ReparsePoint) == 0) return true;
            // Cloud tags differ in their provider-specific high nibble. Traverse only hydrated
            // cloud directories; links and unknown providers remain boundaries, avoiding loops.
            return (reparseTag & 0xffff0fff) == CloudTag;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetDriveType(string root);
    }
}
