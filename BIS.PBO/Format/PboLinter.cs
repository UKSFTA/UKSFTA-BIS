#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BIS.PBO
{
    /// <summary>Severity level for PBO lint diagnostics.</summary>
    public enum PboLintSeverity
    {
        Error,
        Warning,
        Help,
    }

    /// <summary>A single PBO lint diagnostic.</summary>
    public readonly struct PboLintDiagnostic
    {
        public string Code { get; }
        public PboLintSeverity Severity { get; }
        public string Message { get; }
        public string EntryName { get; }

        public PboLintDiagnostic(string code, PboLintSeverity severity, string message, string entryName = "")
        {
            Code = code;
            Severity = severity;
            Message = message;
            EntryName = entryName;
        }

        public override string ToString()
        {
            var loc = string.IsNullOrEmpty(EntryName) ? "" : $" in entry '{EntryName}'";
            return $"[{Code}] {Severity}: {Message}{loc}";
        }
    }

    /// <summary>Linter for Arma PBO archives. Checks header-level consistency and naming.</summary>
    public class PboLinter
    {
        private readonly List<PboLintDiagnostic> _diagnostics = new();

        public IReadOnlyList<PboLintDiagnostic> Diagnostics => _diagnostics;

        /// <summary>Lint a loaded PBO. Returns the list of diagnostics.</summary>
        public IReadOnlyList<PboLintDiagnostic> Lint(PBO pbo)
        {
            _diagnostics.Clear();

            CheckDuplicateEntries(pbo);
            CheckObfuscatedNames(pbo);
            CheckMissingPrefix(pbo);
            CheckEmptyPbo(pbo);
            CheckSuspiciousTimestamps(pbo);

            return _diagnostics;
        }

        /// <summary>L-P01: Duplicate file entries with the same name.</summary>
        private void CheckDuplicateEntries(PBO pbo)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in pbo.Files)
            {
                if (!seen.Add(entry.FileName))
                {
                    _diagnostics.Add(new PboLintDiagnostic(
                        "L-P01", PboLintSeverity.Error,
                        $"Duplicate entry '{entry.FileName}'",
                        entry.FileName));
                }
            }
        }

        /// <summary>L-P02: Entries with obfuscated names (sanitized != raw, or non-ASCII chars).</summary>
        private void CheckObfuscatedNames(PBO pbo)
        {
            foreach (var entry in pbo.Files)
            {
                // Entry has a sanitized name that differs from raw — obfuscation pattern
                if (!string.IsNullOrEmpty(entry.RawFileName) &&
                    !string.Equals(entry.FileName, entry.RawFileName, StringComparison.Ordinal))
                {
                    _diagnostics.Add(new PboLintDiagnostic(
                        "L-P02", PboLintSeverity.Warning,
                        $"Entry name was obfuscated/sanitized: raw '{entry.RawFileName}' -> '{entry.FileName}'",
                        entry.FileName));
                    continue;
                }

                // Name is empty or starts with _unknown (fell through sanitizer)
                if (string.IsNullOrEmpty(entry.FileName) ||
                    entry.FileName.StartsWith("_unknown", StringComparison.OrdinalIgnoreCase))
                {
                    _diagnostics.Add(new PboLintDiagnostic(
                        "L-P02", PboLintSeverity.Warning,
                        $"Entry has obfuscated/unknown name",
                        entry.FileName));
                    continue;
                }

                // Check for non-printable ASCII or non-ASCII chars
                foreach (var c in entry.FileName)
                {
                    if (char.IsControl(c) || c > 127)
                    {
                        _diagnostics.Add(new PboLintDiagnostic(
                            "L-P02", PboLintSeverity.Warning,
                            $"Entry name contains non-ASCII or control character (U+{(int)c:X4})",
                            entry.FileName));
                        break;
                    }
                }
            }
        }

        /// <summary>L-P03: Missing or empty 'prefix' property.</summary>
        private void CheckMissingPrefix(PBO pbo)
        {
            // Check PropertiesPairs for 'prefix' key
            var hasPrefix = pbo.PropertiesPairs.Any(
                p => string.Equals(p.Key, "prefix", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(p.Value));

            if (!hasPrefix)
            {
                _diagnostics.Add(new PboLintDiagnostic(
                    "L-P03", PboLintSeverity.Warning,
                    "PBO is missing a 'prefix' property"));
            }
        }

        /// <summary>L-P04: PBO with no file entries (empty archive).</summary>
        private void CheckEmptyPbo(PBO pbo)
        {
            if (pbo.Files.Count == 0)
            {
                _diagnostics.Add(new PboLintDiagnostic(
                    "L-P04", PboLintSeverity.Warning,
                    "PBO contains no file entries"));
            }
        }

        /// <summary>L-P05: Suspicious timestamps (epoch 0 for non-empty files).</summary>
        private void CheckSuspiciousTimestamps(PBO pbo)
        {
            foreach (var entry in pbo.Files)
            {
                // Unix epoch timestamp of 0 strongly suggests obfuscation
                if (entry.TimeStamp == 0 && entry.Size > 0)
                {
                    _diagnostics.Add(new PboLintDiagnostic(
                        "L-P05", PboLintSeverity.Warning,
                        $"Entry has zero timestamp (obfuscation signal) for {entry.Size}-byte file",
                        entry.FileName));
                }
            }
        }
    }
}
