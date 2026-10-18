using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickLook.Plugin.FolderViewer
{
    internal sealed class EverythingEndpoint
    {
        public EverythingEndpoint(string pipe, string instance) { Pipe = pipe; Instance = instance; }
        public string Pipe { get; }
        public string Instance { get; }

        internal static EverythingEndpoint[] Discover(string instance, int session)
        {
            var result = new List<EverythingEndpoint>();
            foreach (var name in instance == null ? new[] { "", "1.5a" } : new[] { instance })
            {
                var escaped = name.Replace("%", "%25").Replace(":", "%3a").Replace("\\", "%5c");
                var suffix = name.Length == 0 ? "" : " (" + escaped + ")";
                result.Add(new EverythingEndpoint("Everything IPC Session " + session + suffix, name));
                result.Add(new EverythingEndpoint("Everything IPC" + suffix, name));
            }
            return result.ToArray();
        }

        public static EverythingEndpoint[] Defaults()
        {
            ProcessIdToSessionId(GetCurrentProcessId(), out var session);
            return Discover(Environment.GetEnvironmentVariable("FOLDERVIEWER_EVERYTHING_INSTANCE"), (int)session);
        }

        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint process, out uint session);
    }

    // Serialized by the provider. A timeout closes the pipe to interrupt synchronous reads.
    internal sealed class Sdk3Client : IDisposable
    {
        private NamedPipeClientStream _pipe;
        private readonly string _name;
        public bool Responded { get; private set; }
        public Sdk3Client(string name) { _name = name; }

        public bool TryQuery(string path, CancellationToken cancellation, out long size)
        {
            size = 0;
            Responded = false;
            try
            {
                if (_pipe == null)
                {
                    // Missing names return immediately; only an existing busy endpoint
                    // spends the short wait budget. New session probes must not penalize
                    // older Everything versions with a timeout for every absent name.
                    if (!WaitNamedPipe(@"\\.\pipe\" + _name, 20))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        return false;
                    }
                    _pipe = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
                    // Briefly wait for a busy server, without spending the entire query budget
                    // on a missing endpoint. The caller's token bounds all attempts together.
                    using (cancellation.Register(Dispose)) _pipe.Connect(0);
                }
                using (cancellation.Register(Dispose))
                {
                    var bytes = Encoding.UTF8.GetBytes(path);
                    var header = new byte[8];
                    Buffer.BlockCopy(BitConverter.GetBytes((uint)18), 0, header, 0, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes((uint)bytes.Length), 0, header, 4, 4);
                    _pipe.Write(header, 0, 8);
                    _pipe.Write(bytes, 0, bytes.Length);
                    _pipe.Flush();
                    ReadExactly(_pipe, header);
                    var code = BitConverter.ToUInt32(header, 0);
                    if ((code != 100 && code != 200) || BitConverter.ToUInt32(header, 4) != 8)
                    {
                        Dispose();
                        return false;
                    }
                    ReadExactly(_pipe, header);
                    Responded = true;
                    var value = BitConverter.ToUInt64(header, 0);
                    if (value == ulong.MaxValue) return false;
                    size = value > long.MaxValue ? long.MaxValue : (long)value;
                    return true;
                }
            }
            catch (Exception error) when (error is IOException || error is TimeoutException ||
                error is UnauthorizedAccessException || error is InvalidOperationException || error is ObjectDisposedException)
            {
                Dispose();
                cancellation.ThrowIfCancellationRequested();
                return false;
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        public void Dispose()
        {
            // Do not null the field here: cancellation can race with a read. A disposed
            // stream throws the expected ObjectDisposedException instead of a null reference.
            _pipe?.Dispose();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WaitNamedPipe(string name, uint milliseconds);
    }
}
