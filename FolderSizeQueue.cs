using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuickLook.Plugin.FolderViewer
{
    // UI-owned: one in-flight query, bounded urgent work, lazy background cursors.
    internal sealed class FolderSizeQueue : IDisposable
    {
        private readonly Func<string, CancellationToken, Task<FolderSizeQueryResult>> _query;
        private readonly Action<FileEntry, long> _apply;
        private readonly CancellationTokenSource _lifetime;
        private readonly Queue<FileEntry> _urgent = new Queue<FileEntry>();
        private readonly HashSet<FileEntry> _urgentSet = new HashSet<FileEntry>();
        private readonly Queue<Batch> _batches = new Queue<Batch>();
        private bool _running, _disposed;
        private DateTime _backgroundUntil;
        private readonly Task _ready;

        public FolderSizeQueue(Func<string, CancellationToken, Task<FolderSizeQueryResult>> query,
            Action<FileEntry, long> apply, Task ready, CancellationToken token)
        {
            _query = query;
            _apply = apply;
            _ready = ready;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        }

        internal int PendingPriorityCount => _urgent.Count;
        public void AddBatch(IReadOnlyList<FileEntry> entries, FileEntry owner = null)
        {
            if (_disposed || entries == null) return;
            Prune();
            while (_batches.Count >= 32) _batches.Dequeue().Items.Dispose();
            _batches.Enqueue(new Batch(entries.GetEnumerator(), owner));
            Wake();
        }

        // Drop enumeration snapshots immediately when a branch folds or is evicted,
        // even if background filling has already exhausted its activity window.
        public void Prune()
        {
            for (var i = _batches.Count; i > 0; i--)
            {
                var batch = _batches.Dequeue();
                if (batch.IsCurrent) _batches.Enqueue(batch);
                else batch.Items.Dispose();
            }
            for (var i = _urgent.Count; i > 0; i--)
            {
                var entry = _urgent.Dequeue();
                var visible = true;
                for (var parent = entry.Parent; parent != null; parent = parent.Parent)
                    if (!parent.IsExpanded || !parent.IsLoaded) { visible = false; break; }
                if (visible) _urgent.Enqueue(entry);
                else _urgentSet.Remove(entry);
            }
        }

        public void Prioritize(FileEntry entry)
        {
            if (_disposed || entry == null || !entry.IsFolder || entry.Size.HasValue || _urgentSet.Contains(entry)) return;
            if (_urgent.Count >= 128) _urgentSet.Remove(_urgent.Dequeue());
            _urgent.Enqueue(entry);
            _urgentSet.Add(entry);
            Wake();
        }

        private void Wake()
        {
            _backgroundUntil = DateTime.UtcNow.AddSeconds(5);
            if (!_running) PumpAsync();
        }

        private async void PumpAsync()
        {
            _running = true;
            try
            {
                await _ready;
                while (!_disposed && !_lifetime.IsCancellationRequested)
                {
                    var entry = Next();
                    if (entry == null) break;
                    if (entry.Size.HasValue || !entry.IsFolder) continue;
                    var result = await _query(entry.FullPath, _lifetime.Token);
                    if (_disposed) break;
                    if (result.IsSuccess) _apply(entry, result.Size);
                    else await Task.Delay(50, _lifetime.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
            finally { _running = false; }
        }

        private FileEntry Next()
        {
            if (_urgent.Count != 0)
            {
                var item = _urgent.Dequeue();
                _urgentSet.Remove(item);
                return item;
            }
            if (DateTime.UtcNow >= _backgroundUntil) return null;
            while (_batches.Count != 0)
            {
                var batch = _batches.Peek();
                if (batch.IsCurrent && batch.Items.MoveNext())
                    return batch.Items.Current;
                _batches.Dequeue().Items.Dispose();
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _urgent.Clear();
            _urgentSet.Clear();
            while (_batches.Count != 0) _batches.Dequeue().Items.Dispose();
            _lifetime.Dispose();
        }

        private sealed class Batch
        {
            public Batch(IEnumerator<FileEntry> items, FileEntry owner)
            { Items = items; Owner = owner; Generation = owner?.LoadGeneration ?? 0; }
            public IEnumerator<FileEntry> Items { get; }
            public FileEntry Owner { get; }
            private int Generation { get; }
            public bool IsCurrent
            {
                get
                {
                    if (Owner == null) return true;
                    if (Owner.LoadGeneration != Generation) return false;
                    for (var parent = Owner; parent != null; parent = parent.Parent)
                        if (!parent.IsExpanded) return false;
                    return true;
                }
            }
        }
    }
}
