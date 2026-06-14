#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BIS.Stringtable;

/// <summary>Severity level for stringtable lint diagnostics.</summary>
public enum StringtableLintSeverity
{
    Error,
    Warning,
    Help,
}

/// <summary>A single stringtable lint diagnostic.</summary>
public readonly struct StringtableLintDiagnostic
{
    public string Code { get; }
    public StringtableLintSeverity Severity { get; }
    public string Message { get; }
    public string KeyId { get; }
    public string Package { get; }
    public string? Language { get; }

    public StringtableLintDiagnostic(string code, StringtableLintSeverity severity, string message,
        string package = "", string keyId = "", string? language = null)
    {
        Code = code;
        Severity = severity;
        Message = message;
        Package = package;
        KeyId = keyId;
        Language = language;
    }

    public override string ToString()
    {
        var loc = !string.IsNullOrEmpty(KeyId) ? KeyId : "(unknown)";
        if (!string.IsNullOrEmpty(Package))
            loc = $"{Package}/{loc}";
        if (!string.IsNullOrEmpty(Language))
            loc = $"{loc}[{Language}]";
        return $"[{Code}] {Severity}: {Message} at {loc}";
    }
}

/// <summary>Linter for Arma stringtable.xml files.</summary>
public class StringtableLinter
{
    private readonly List<StringtableLintDiagnostic> _diagnostics;

    public StringtableLinter()
    {
        _diagnostics = new List<StringtableLintDiagnostic>();
    }

    public IReadOnlyList<StringtableLintDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Lint a parsed Stringtable. Returns the list of diagnostics.</summary>
    public IReadOnlyList<StringtableLintDiagnostic> Lint(Stringtable table)
    {
        _diagnostics.Clear();

        // Run all checks
        CheckNotSorted(table);
        CheckNewlinesInTags(table);
        CheckUnknownLanguage(table);
        CheckEmptyKey(table);
        CheckEmptyTranslation(table);
        CheckMissingOriginal(table);

        return _diagnostics;
    }

    /// <summary>L-L01: Check keys are alphabetically sorted within each package.</summary>
    private void CheckNotSorted(Stringtable table)
    {
        foreach (var pkg in table.Project.Packages)
        {
            for (int i = 1; i < pkg.Keys.Count; i++)
            {
                var prev = pkg.Keys[i - 1].ID;
                var curr = pkg.Keys[i].ID;
                if (string.Compare(prev, curr, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    _diagnostics.Add(new StringtableLintDiagnostic(
                        "L-L01", StringtableLintSeverity.Warning,
                        $"Key '{curr}' is not sorted (appears after '{prev}')",
                        pkg.Name, curr));
                }
            }
        }
    }

    /// <summary>L-L03: Check translation values have no leading/trailing whitespace.</summary>
    private void CheckNewlinesInTags(Stringtable table)
    {
        foreach (var pkg in table.Project.Packages)
        {
            foreach (var key in pkg.Keys)
            {
                foreach (var (lang, text) in key.Translations)
                {
                    if (!string.IsNullOrEmpty(text) && text != text.Trim())
                    {
                        _diagnostics.Add(new StringtableLintDiagnostic(
                            "L-L03", StringtableLintSeverity.Warning,
                            $"Translation in '{lang}' has leading or trailing whitespace",
                            pkg.Name, key.ID, lang));
                    }
                }
            }
        }
    }

    /// <summary>Check for language codes not in the known languages list.</summary>
    private void CheckUnknownLanguage(Stringtable table)
    {
        foreach (var pkg in table.Project.Packages)
        {
            foreach (var key in pkg.Keys)
            {
                foreach (var lang in key.Translations.Keys)
                {
                    if (!StringtableUtil.KnownLanguages.Contains(lang))
                    {
                        _diagnostics.Add(new StringtableLintDiagnostic(
                            "L-L04", StringtableLintSeverity.Warning,
                            $"Unknown language '{lang}'",
                            pkg.Name, key.ID, lang));
                    }
                }
            }
        }
    }

    /// <summary>Check for keys with no translations.</summary>
    private void CheckEmptyKey(Stringtable table)
    {
        foreach (var pkg in table.Project.Packages)
        {
            foreach (var key in pkg.Keys)
            {
                if (key.Translations.Count == 0)
                {
                    _diagnostics.Add(new StringtableLintDiagnostic(
                        "L-L05", StringtableLintSeverity.Error,
                        "Key has no translations",
                        pkg.Name, key.ID));
                }
            }
        }
    }

    /// <summary>Check for translation values that are empty or whitespace-only.</summary>
    private void CheckEmptyTranslation(Stringtable table)
    {
        foreach (var pkg in table.Project.Packages)
        {
            foreach (var key in pkg.Keys)
            {
                foreach (var (lang, text) in key.Translations)
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _diagnostics.Add(new StringtableLintDiagnostic(
                            "L-L06", StringtableLintSeverity.Warning,
                            $"Empty translation in '{lang}'",
                            pkg.Name, key.ID, lang));
                    }
                }
            }
        }
    }

    /// <summary>Check that every key has an 'Original' language translation.</summary>
    private void CheckMissingOriginal(Stringtable table)
    {
        foreach (var pkg in table.Project.Packages)
        {
            foreach (var key in pkg.Keys)
            {
                if (!key.Translations.ContainsKey("Original"))
                {
                    _diagnostics.Add(new StringtableLintDiagnostic(
                        "L-L07", StringtableLintSeverity.Warning,
                        "Key is missing 'Original' language",
                        pkg.Name, key.ID));
                }
            }
        }
    }
}
