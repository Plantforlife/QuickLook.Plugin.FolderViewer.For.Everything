using System;
using System.Windows;
using System.Windows.Controls;

namespace QuickLook.Plugin.FolderViewer
{
    public sealed class FileIcon : Image
    {
        private int _generation;
        public FileIcon()
        {
            Loaded += (s, e) => LoadIcon();
            Unloaded += (s, e) => _generation++;
            DataContextChanged += (s, e) => { if (IsLoaded) LoadIcon(); };
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            if (IsLoaded) LoadIcon();
        }

        private async void LoadIcon()
        {
            var generation = ++_generation;
            if (!(DataContext is FileEntry entry) || string.IsNullOrEmpty(entry.FullPath))
            { Source = null; return; }
            Source = SystemIconProvider.Placeholder;
            var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            var image = await SystemIconProvider.GetIconAsync(entry.FullPath, entry.IsFolder, (int)Math.Ceiling(16 * scale));
            if (generation == _generation && IsLoaded) Source = image;
        }
    }
}
