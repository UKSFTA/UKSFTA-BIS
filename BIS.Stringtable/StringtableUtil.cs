using System;
using System.Collections.Generic;
using System.Linq;

namespace BIS.Stringtable;

/// <summary>Utility methods for stringtable analysis, validation, and manipulation.</summary>
public static partial class StringtableUtil
{
    /// <summary>Language codes commonly found in Arma stringtables.</summary>
    public static readonly IReadOnlySet<string> KnownLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Original", "English", "French", "German", "Spanish", "Italian", "Portuguese",
        "Polish", "Russian", "Czech", "Hungarian", "Japanese", "Korean", "Chinese",
        "Turkish", "Greek", "Dutch", "Swedish", "Norwegian", "Finnish", "Danish",
        "Romanian", "Bulgarian", "Serbian", "Croatian", "Slovak", "Slovenian",
        "Estonian", "Latvian", "Lithuanian", "Ukrainian", "Belarusian",
        "Arabic", "Hebrew", "Hindi", "Bengali", "Thai", "Vietnamese",
    };

    /// <summary>Find all keys missing specific languages (case-insensitive).</summary>
    public static Dictionary<string, ISet<string>> FindMissingLanguages(Stringtable table, params string[] requiredLanguages)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (requiredLanguages == null || requiredLanguages.Length == 0)
            return new Dictionary<string, ISet<string>>();

        var result = new Dictionary<string, ISet<string>>();
        foreach (var key in table.AllKeys())
        {
            var missing = requiredLanguages
                .Where(l => !key.Translations.ContainsKey(l))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (missing.Count > 0)
                result[key.ID] = missing;
        }
        return result;
    }

    /// <summary>Find duplicate key IDs across the stringtable (same ID in multiple packages).</summary>
    public static ILookup<string, string> FindDuplicateKeys(Stringtable table)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        return table.AllKeys()
            .GroupBy(k => k.ID, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToLookup(g => g.Key, g => string.Join(", ", g.SelectMany(k =>
                table.Project.Packages.Where(p => p.Keys.Contains(k)).Select(p => p.Name))));
    }

    /// <summary>Validate that all key IDs follow the STR_Package_ convention.</summary>
    public static IEnumerable<string> ValidateKeyNaming(Stringtable table, string expectedPrefix = null)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        foreach (var key in table.AllKeys())
        {
            if (string.IsNullOrWhiteSpace(key.ID))
                yield return $"Key with empty ID in package {GetPackageOf(table, key)?.Name}";

            if (expectedPrefix != null && !key.ID.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                yield return $"Key '{key.ID}' does not start with expected prefix '{expectedPrefix}'";
        }
    }

    /// <summary>Find keys that are present in <paramref name="source"/> but missing from <paramref name="target"/>.</summary>
    public static IEnumerable<Key> FindMissingKeys(Stringtable source, Stringtable target)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));

        var targetIds = new HashSet<string>(target.AllKeys().Select(k => k.ID), StringComparer.OrdinalIgnoreCase);
        return source.AllKeys().Where(k => !targetIds.Contains(k.ID));
    }

    /// <summary>Merge translations from <paramref name="source"/> into <paramref name="target"/>.
    /// Existing translations in target are not overwritten unless <paramref name="overwrite"/> is true.</summary>
    public static Stringtable Merge(Stringtable target, Stringtable source, bool overwrite = false)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (source == null) throw new ArgumentNullException(nameof(source));

        var targetKeys = target.AllKeys().ToDictionary(k => k.ID, StringComparer.OrdinalIgnoreCase);
        var sourceKeys = source.AllKeys().ToDictionary(k => k.ID, StringComparer.OrdinalIgnoreCase);

        var mergedKeys = new List<Key>();
        foreach (var key in target.AllKeys())
        {
            var mergedTranslations = new Dictionary<string, string>(key.Translations, StringComparer.OrdinalIgnoreCase);
            if (sourceKeys.TryGetValue(key.ID, out var sourceKey))
            {
                foreach (var (lang, text) in sourceKey.Translations)
                {
                    if (overwrite || !mergedTranslations.ContainsKey(lang))
                        mergedTranslations[lang] = text;
                }
            }
            mergedKeys.Add(new Key(key.ID, mergedTranslations));
        }

        foreach (var (id, sourceKey) in sourceKeys)
        {
            if (!targetKeys.ContainsKey(id))
            {
                mergedKeys.Add(sourceKey);
            }
        }

        return new Stringtable(new Project(target.Project.Name,
            [new Package(target.Project.Name, mergedKeys)]));
    }

    /// <summary>Count keys per language.</summary>
    public static IReadOnlyDictionary<string, int> CountByLanguage(Stringtable table)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in table.AllKeys())
        {
            foreach (var lang in key.Translations.Keys)
            {
                counts.TryGetValue(lang, out var c);
                counts[lang] = c + 1;
            }
        }
        return counts;
    }

    private static Package GetPackageOf(Stringtable table, Key key)
    {
        return table.Project.Packages.FirstOrDefault(p => p.Keys.Contains(key));
    }
}
