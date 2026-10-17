// Copyright © 2020 Paddy Xu, Frank Becker
// This file remains available under the GNU General Public License v3 or later.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickLook.Plugin.FolderViewer
{
    public sealed class SizePrettyPrintConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DependencyProperty.UnsetValue)
                return string.Empty;

            // WPF passes the element's Language here (en-US unless set), not the user's
            // regional settings, so format with CurrentCulture like Explorer does.
            return value is long size && size >= 0
                ? ByteSizeFormatter.Format(size, CultureInfo.CurrentCulture)
                : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class DatePrintConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is DateTime date) || date == DateTime.MinValue)
                return string.Empty;

            // See SizePrettyPrintConverter: use the user's regional format, not WPF's en-US default.
            return date.ToString("g", CultureInfo.CurrentCulture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class FileToIconConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2 ||
                values[0] == DependencyProperty.UnsetValue ||
                values[1] == DependencyProperty.UnsetValue ||
                !(values[0] is string fullPath) ||
                !(values[1] is bool isFolder) ||
                string.IsNullOrEmpty(fullPath))
            {
                return null;
            }

            return SystemIconProvider.GetIcon(fullPath, isFolder);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    internal static class ByteSizeFormatter
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };

        public static string Format(long bytes, CultureInfo culture = null)
        {
            culture = culture ?? CultureInfo.CurrentCulture;
            if (bytes < 1024)
                return bytes.ToString("N0", culture) + " B";

            var value = (double)bytes;
            var unit = 0;
            while (value >= 1024 && unit < Units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString(value >= 100 ? "N0" : value >= 10 ? "N1" : "N2", culture) + " " + Units[unit];
        }
    }
}
