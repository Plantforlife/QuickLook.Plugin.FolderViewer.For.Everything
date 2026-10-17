using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Markup;
using System.Xml.Linq;

namespace QuickLook.Plugin.FolderViewer
{
    /// <summary>
    /// UI strings from Translations.config, picked by the Windows display language.
    /// </summary>
    /// <remarks>
    /// Walks the whole culture parent chain (QuickLook's TranslationHelper only checks one
    /// level, and on .NET Framework zh-CN's parent is zh-CHS rather than zh-Hans), then
    /// falls back to English and finally to the id itself.
    /// </remarks>
    internal static class Strings
    {
        private static readonly Lazy<Table> Translations = new Lazy<Table>(
            () => Table.Load(
                Path.Combine(
                    Path.GetDirectoryName(typeof(Strings).Assembly.Location) ?? string.Empty,
                    "Translations.config"),
                CultureInfo.CurrentUICulture));

        public static string Get(string id)
        {
            return Translations.Value.Get(id);
        }

        public static string Format(string id, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(id), args);
        }

        internal sealed class Table
        {
            private readonly IReadOnlyDictionary<string, string> _fallback;
            private readonly IReadOnlyDictionary<string, string> _strings;

            private Table(IReadOnlyDictionary<string, string> strings, IReadOnlyDictionary<string, string> fallback)
            {
                _strings = strings;
                _fallback = fallback;
            }

            public string Get(string id)
            {
                return _strings.TryGetValue(id, out var value) || _fallback.TryGetValue(id, out value)
                    ? value
                    : id;
            }

            public static Table Load(string file, CultureInfo culture)
            {
                var empty = new Dictionary<string, string>();
                if (!File.Exists(file))
                    return new Table(empty, empty);

                XElement root;
                try
                {
                    root = XDocument.Load(file).Root;
                }
                catch (Exception)
                {
                    return new Table(empty, empty);
                }

                if (root == null)
                    return new Table(empty, empty);

                var languages = root.Elements()
                    .GroupBy(e => e.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => (IReadOnlyDictionary<string, string>)g.First().Elements()
                            .GroupBy(e => e.Name.LocalName)
                            .ToDictionary(e => e.Key, e => e.First().Value),
                        StringComparer.OrdinalIgnoreCase);

                languages.TryGetValue("en", out var english);
                english = english ?? empty;

                for (var c = culture; c != null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
                {
                    if (languages.TryGetValue(c.Name, out var match))
                        return new Table(match, english);
                }

                return new Table(english, english);
            }
        }
    }

    /// <summary>
    /// XAML usage: <c>Text="{local:Translate Header_Name}"</c>.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TranslateExtension : MarkupExtension
    {
        public TranslateExtension()
        {
        }

        public TranslateExtension(string id)
        {
            Id = id;
        }

        [ConstructorArgument("id")]
        public string Id { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Strings.Get(Id);
        }
    }
}
