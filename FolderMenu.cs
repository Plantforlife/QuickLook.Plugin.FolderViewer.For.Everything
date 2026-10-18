using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickLook.Plugin.FolderViewer
{
    // QuickLook 4.5 exposes its menu on the titlebar button. Scope the compatibility
    // menu to this preview and restore the host menu before another viewer takes over.
    internal sealed class FolderMenu : IDisposable
    {
        private readonly Button _button;
        private readonly ContextMenu _original, _menu;

        private FolderMenu(Button button, Window window, MenuItem reload, MenuItem copy)
        {
            _button = button;
            _original = button.ContextMenu;
            var resources = new ResourceDictionary
            {
                Source = new Uri("/QuickLook.Plugin.FolderViewer.For.Everything;component/FolderMenu.xaml", UriKind.Relative)
            };
            _menu = new ContextMenu
            {
                Style = (Style)resources["FolderMenu"],
                FontFamily = window.FontFamily,
                FontSize = 12,
                Background = window.TryFindResource("MainWindowBackgroundNoTransparent") as Brush ?? SystemColors.MenuBrush,
                Foreground = window.TryFindResource("WindowTextForeground") as Brush ?? SystemColors.MenuTextBrush
            };
            _menu.Items.Add(Forward(reload, "\uE72C"));
            _menu.Items.Add(Forward(copy, "\uE8C8"));
            button.SetCurrentValue(FrameworkElement.ContextMenuProperty, _menu);
        }

        internal static FolderMenu Attach(FrameworkElement content)
        {
            var window = Window.GetWindow(content);
            var button = window?.FindName("buttonMore") as Button;
            var reload = window?.FindName("moreItemReload") as MenuItem;
            var copy = window?.FindName("moreItemCopyAsPath") as MenuItem;
            return button?.ContextMenu != null && reload != null && copy != null
                ? new FolderMenu(button, window, reload, copy) : null;
        }

        private static MenuItem Forward(MenuItem original, string glyph)
        {
            var item = new MenuItem
            {
                Header = original.Header,
                Command = original.Command,
                CommandParameter = original.CommandParameter,
                CommandTarget = original.CommandTarget,
                Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets") }
            };
            item.Click += (sender, args) => original.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, original));
            return item;
        }

        public void Dispose()
        {
            _menu.IsOpen = false;
            if (ReferenceEquals(_button.ContextMenu, _menu))
                _button.SetCurrentValue(FrameworkElement.ContextMenuProperty, _original);
        }
    }
}
