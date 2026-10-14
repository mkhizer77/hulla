using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization.SmartFormat;

namespace Hulla.Presentation.Text
{
    /// <summary>
    /// The string tables (GDD 26.1, ADR-0023): content/strings exported by tools/strings-export to
    /// Resources/Strings/strings.&lt;locale&gt;.json as Smart Strings, looked up by stable key and
    /// formatted with the Localization package's Smart Strings engine in the locale's culture.
    /// English fills any key a locale lacks; an unknown key shows as itself so it is easy to spot.
    /// </summary>
    public static class Strings
    {
        public const string Fallback = "en";

        private static Table? _english;
        private static Table? _active;

        /// <summary>The locale in use, such as "en" or the pseudo-locale "en-XA".</summary>
        public static string Locale => Active.Locale;

        public static CultureInfo Culture => Active.Culture;

        /// <summary>True for right-to-left locales; the shell mirrors layouts (GDD 26.1).</summary>
        public static bool RightToLeft => Active.RightToLeft;

        /// <summary>Raised after <see cref="Use"/> switches the table, so open screens can redraw.</summary>
        public static event Action? Changed;

        private static Table English => _english ??= Table.Load(Fallback) ?? Table.Empty(Fallback);

        private static Table Active => _active ?? English;

        /// <summary>Switches to a locale's table; false (and English) when the build has no table for it.</summary>
        public static bool Use(string locale)
        {
            var table = locale == Fallback ? English : Table.Load(locale);
            _active = table ?? English;
            Changed?.Invoke();
            return table != null;
        }

        /// <summary>
        /// A key's pattern, unformatted: for plain strings the text itself, for the rest a Smart String
        /// to pass to <see cref="Format"/> with its arguments. The text classes return these.
        /// </summary>
        public static string Text(string key)
        {
            if (Active.Entries.TryGetValue(key, out var entry) || English.Entries.TryGetValue(key, out entry))
            {
                return entry.Text;
            }

            return key;
        }

        /// <summary>A key's text with its arguments, plurals and selects applied.</summary>
        public static string Get(string key, params object[] args)
        {
            if (Active.Entries.TryGetValue(key, out var entry) || English.Entries.TryGetValue(key, out entry))
            {
                return entry.Smart ? Format(entry.Text, args) : entry.Text;
            }

            return key;
        }

        /// <summary>Formats a Smart String pattern, as returned by a text property, in the locale's culture.</summary>
        public static string Format(string pattern, params object[] args)
        {
            try
            {
                return Smart.Format(Culture, pattern, args);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"strings: \"{pattern}\" did not format: {error.Message}");
                return pattern;
            }
        }

        public static bool Has(string key) => Active.Entries.ContainsKey(key) || English.Entries.ContainsKey(key);

        [Serializable]
        private sealed class FileEntry
        {
            public string key = "";
            public string text = "";
            public bool smart;
        }

        [Serializable]
        private sealed class File
        {
            public string locale = "";
            public bool rtl;
            public FileEntry[] entries = Array.Empty<FileEntry>();
        }

        private sealed class Entry
        {
            public Entry(string text, bool smart)
            {
                Text = text;
                Smart = smart;
            }

            public string Text { get; }

            public bool Smart { get; }
        }

        private sealed class Table
        {
            private Table(string locale, bool rtl, Dictionary<string, Entry> entries)
            {
                Locale = locale;
                RightToLeft = rtl;
                Entries = entries;
                Culture = CultureFor(locale);
            }

            public string Locale { get; }

            public bool RightToLeft { get; }

            public CultureInfo Culture { get; }

            public Dictionary<string, Entry> Entries { get; }

            public static Table Empty(string locale) => new Table(locale, false, new Dictionary<string, Entry>());

            public static Table? Load(string locale)
            {
                var asset = Resources.Load<TextAsset>("Strings/strings." + locale);
                if (asset == null)
                {
                    return null;
                }

                var file = JsonUtility.FromJson<File>(asset.text);
                var entries = new Dictionary<string, Entry>(file.entries.Length);
                foreach (var e in file.entries)
                {
                    entries[e.key] = new Entry(e.text, e.smart);
                }

                return new Table(locale, file.rtl, entries);
            }

            /// <summary>Pseudo-locales (en-XA, ar-XB) format like their base language.</summary>
            private static CultureInfo CultureFor(string locale)
            {
                foreach (var name in new[] { locale, locale.Split('-')[0] })
                {
                    try
                    {
                        return CultureInfo.GetCultureInfo(name);
                    }
                    catch (CultureNotFoundException)
                    {
                    }
                }

                return CultureInfo.InvariantCulture;
            }
        }
    }
}
