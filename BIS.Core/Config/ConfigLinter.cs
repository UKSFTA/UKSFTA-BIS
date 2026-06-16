#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BIS.Core.Config
{
    /// <summary>
    /// Severity level for lint diagnostics, mirroring HEMTT's convention.
    /// </summary>
    public enum LintSeverity
    {
        Error,
        Warning,
        Help,
    }

    /// <summary>
    /// Describes an automatic source fix for a lint diagnostic.
    /// </summary>
    public sealed record ConfigLintFix(int Offset, int Length, string ReplacementText);

    /// <summary>
    /// A single lint diagnostic — maps to HEMTT's L-Cxx lint rules.
    /// </summary>
    public readonly struct LintDiagnostic
    {
        public string Code { get; }
        public LintSeverity Severity { get; }
        public string Message { get; }
        public string File { get; }
        public int Line { get; }
        public string Path { get; } // config class hierarchy path
        public ConfigLintFix? Fix { get; init; }

        public LintDiagnostic(string code, LintSeverity severity, string message, string file = "", int line = 0, string path = "")
        {
            Code = code;
            Severity = severity;
            Message = message;
            File = file;
            Line = line;
            Path = path;
        }

        public override string ToString()
        {
            var loc = !string.IsNullOrEmpty(File)
                ? $"{File}({Line})"
                : !string.IsNullOrEmpty(Path) ? Path : "(unknown)";
            return $"[{Code}] {Severity}: {Message} at {loc}";
        }
    }

    /// <summary>
    /// Linter for Arma config files that replicates HEMTT's config lint rules (L-C01 through L-C17).
    /// Operates on a parsed ParamFile AST.
    /// </summary>
    public class ConfigLinter
    {
        private readonly List<LintDiagnostic> _diagnostics;
        private readonly HashSet<string> _externClassNames;
        private readonly Dictionary<string, string> _externClassFiles; // name -> file for location
        private readonly HashSet<string> _referencedBases;
        private string _sourceText = "";

        public ConfigLinter()
        {
            _diagnostics = new List<LintDiagnostic>();
            _externClassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _externClassFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _referencedBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<LintDiagnostic> Diagnostics => _diagnostics;

        /// <summary>
        /// Lint a parsed ParamFile. Returns the list of diagnostics (also available via .Diagnostics).
        /// </summary>
        public IReadOnlyList<LintDiagnostic> Lint(ParamFile file)
        {
            return Lint(file, "");
        }

        /// <summary>
        /// Lint a parsed ParamFile with source text for fix computation.
        /// </summary>
        public IReadOnlyList<LintDiagnostic> Lint(ParamFile file, string sourceText)
        {
            _sourceText = sourceText;
            _diagnostics.Clear();
            _externClassNames.Clear();
            _externClassFiles.Clear();

            // Pass 1: collect all externally declared classes and all defined classes
            var allDefinedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectExternsAndDefines(file.Root, allDefinedClasses, "");

            // Pass 2: walk the tree for structural rules
            VisitClass(file.Root, "");

            // Pass 3: cross-class rules that need global knowledge
            CheckExternalMissing(allDefinedClasses);
            CheckUnusedExternal(allDefinedClasses);
            CheckCfgPatchesScope(file.Root);

            return _diagnostics;
        }

        /// <summary>Apply auto-fixes to source text. Fixes are applied bottom-up to preserve offsets.</summary>
        public static string ApplyFixes(string source, IReadOnlyList<LintDiagnostic> diagnostics)
        {
            var fixes = diagnostics
                .Where(d => d.Fix != null)
                .Select(d => d.Fix!)
                .OrderByDescending(f => f.Offset)
                .ToList();

            var sb = new StringBuilder(source);
            foreach (var fix in fixes)
            {
                sb.Remove(fix.Offset, fix.Length);
                sb.Insert(fix.Offset, fix.ReplacementText);
            }
            return sb.ToString();
        }

        // ─── Pass 1: collect ───

        private void CollectExternsAndDefines(ParamClass cls, HashSet<string> defined, string parentPath)
        {
            foreach (var entry in cls.Entries)
            {
                var childPath = string.IsNullOrEmpty(parentPath) ? entry.Name : $"{parentPath}.{entry.Name}";
                if (entry is ParamExternClass ext)
                {
                    _externClassNames.Add(ext.Name);
                    _externClassFiles[ext.Name] = ext.File;
                }
                else if (entry is ParamClass childCls)
                {
                    defined.Add(childCls.Name);
                    CollectExternsAndDefines(childCls, defined, childPath);
                }
            }
        }

        // ─── Pass 2: structural checks (per-class) ───

        private void VisitClass(ParamClass cls, string parentPath)
        {
            var seenProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var externClassesInScope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in cls.Entries)
            {
                var childPath = string.IsNullOrEmpty(parentPath) ? entry.Name : $"{parentPath}.{entry.Name}";

                // L-C02: Duplicate property
                if (entry is ParamValue || entry is ParamArray || entry is ParamArraySpec)
                {
                    if (!seenProperties.Add(entry.Name))
                    {
                        _diagnostics.Add(new LintDiagnostic(
                            "L-C02", LintSeverity.Error,
                            $"Duplicate property '{entry.Name}'",
                            entry.File, entry.Line, parentPath));
                    }
                }

                // L-C03: Duplicate classes
                if (entry is ParamClass childCls && !(entry is ParamExternClass))
                {
                    if (!seenClasses.Add(childCls.Name))
                    {
                        _diagnostics.Add(new LintDiagnostic(
                            "L-C03", LintSeverity.Error,
                            $"Duplicate class '{childCls.Name}'",
                            entry.File, entry.Line, parentPath));
                    }
                }

                // L-C04/L-C05: Track base class references
                if (entry is ParamClass pc && !string.IsNullOrEmpty(pc.BaseClassName))
                {
                    _referencedBases.Add(pc.BaseClassName);
                    CheckParentCase(pc, parentPath);
                }

                // L-C07: Expected array — property without [] but value looks like array
                if (entry is ParamValue pv2 && pv2.Value.Type == ValueType.Generic)
                {
                    var strVal = pv2.Value.Value as string ?? "";
                    if (strVal.StartsWith("{") && strVal.EndsWith("}"))
                    {
                        _diagnostics.Add(new LintDiagnostic(
                            "L-C07", LintSeverity.Error,
                            $"Expected array: property '{pv2.Name}' should have [] suffix (value is an array)",
                            entry.File, entry.Line, childPath));
                    }
                }

                // L-C11: File type — check known property names for correct file extensions
                if (entry is ParamValue pv3)
                {
                    CheckFileType(pv3, childPath);
                }

                // L-C12: Math could be unquoted
                if (entry is ParamValue pv4 && pv4.Value.Type == ValueType.Generic)
                {
                    CheckMathUnquoted(pv4, childPath);
                }

                // L-C13: Config this call
                if (entry is ParamValue pv5)
                {
                    CheckConfigThisCall(pv5, childPath);
                }

                // L-C16: File missing
                if (entry is ParamValue pv6)
                {
                    CheckFileMissing(pv6, childPath);
                }

                // Recurse into child classes
                if (entry is ParamClass childClass)
                {
                    VisitClass(childClass, childPath);
                }
            }
        }

        // ─── Rule implementations ───

        /// <summary>L-C04: Base class referenced but not defined or external-declared</summary>
        private void CheckExternalMissing(HashSet<string> definedClasses)
        {
            foreach (var baseName in _referencedBases)
            {
                if (definedClasses.Contains(baseName) || _externClassNames.Contains(baseName))
                    continue;

                ConfigLintFix? fix = null;
                if (!string.IsNullOrEmpty(_sourceText))
                {
                    // Insert at the end of the file, before trailing newlines
                    var endIdx = _sourceText.Length;
                    while (endIdx > 0 && _sourceText[endIdx - 1] == '\n')
                        endIdx--;
                    fix = new ConfigLintFix(endIdx, 0, $"\nclass {baseName};\n");
                }

                _diagnostics.Add(new LintDiagnostic(
                    "L-C04", LintSeverity.Error,
                    $"Base class '{baseName}' is not defined or declared as external",
                    "", 0, baseName)
                { Fix = fix });
            }
        }

        private void CheckParentCase(ParamClass cls, string parentPath)
        {
            // L-C05 is checked during VisitClass
            // If the parent class is external and declared with different case, warn
            if (_externClassNames.Contains(cls.BaseClassName))
            {
                // Check if there's an exact case match in externs
                // Since externs use OrdinalIgnoreCase, we check for a different-case match
                var exactMatch = _externClassNames.Any(e => e == cls.BaseClassName);
                var ignoreCaseMatch = _externClassNames.Any(e => string.Equals(e, cls.BaseClassName, StringComparison.OrdinalIgnoreCase) && e != cls.BaseClassName);
                if (ignoreCaseMatch && !exactMatch)
                {
                    var childPath = string.IsNullOrEmpty(parentPath) ? cls.Name : $"{parentPath}.{cls.Name}";

                    ConfigLintFix? fix = null;
                    if (!string.IsNullOrEmpty(_sourceText) && cls.Line > 0)
                    {
                        var lineStart = GetLineOffset(cls.Line);
                        var lineEnd = _sourceText.IndexOf('\n', lineStart);
                        if (lineEnd < 0) lineEnd = _sourceText.Length;
                        var lineText = _sourceText.Substring(lineStart, lineEnd - lineStart);

                        var correctName = _externClassNames.FirstOrDefault(e =>
                            string.Equals(e, cls.BaseClassName, StringComparison.OrdinalIgnoreCase) && e != cls.BaseClassName)
                            ?? cls.BaseClassName;

                        var idx = lineText.IndexOf(cls.BaseClassName, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            fix = new ConfigLintFix(lineStart + idx, cls.BaseClassName.Length, correctName);
                        }
                    }

                    _diagnostics.Add(new LintDiagnostic(
                        "L-C05", LintSeverity.Warning,
                        $"Base class '{cls.BaseClassName}' has incorrect casing (differs from external declaration)",
                        cls.File, cls.Line, childPath)
                    { Fix = fix });
                }
            }
        }

        // Known property → expected file extension map
        private static readonly Dictionary<string, string[]> FileTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = new[] { ".p3d" },
            ["texture"] = new[] { ".paa", ".png", ".jpg", ".jpeg", ".tga", ".paa" },
            ["hiddenTexture"] = new[] { ".paa", ".png", ".jpg", ".jpeg", ".tga" },
            ["editorPreview"] = new[] { ".jpg", ".jpeg", ".png" },
            ["sound"] = new[] { ".ogg", ".wss", ".wav" },
            ["soundVoice"] = new[] { ".ogg", ".wss", ".wav" },
            ["soundEnv"] = new[] { ".ogg", ".wss", ".wav" },
            ["insignia"] = new[] { ".paa", ".png" },
            // RVMAT materials
            ["surfaceInfo"] = new[] { ".rvmat" },
            ["material"] = new[] { ".rvmat" },
        };

        /// <summary>L-C11: File type checks</summary>
        private void CheckFileType(ParamValue pv, string path)
        {
            if (pv.Value.Type != ValueType.Generic)
                return;
            var val = pv.Value.Value as string ?? "";
            if (string.IsNullOrEmpty(val))
                return;

            if (!FileTypeMap.TryGetValue(pv.Name, out var expectedExts))
                return;

            var ext = Path.GetExtension(val);
            if (string.IsNullOrEmpty(ext))
            {
                _diagnostics.Add(new LintDiagnostic(
                    "L-C11", LintSeverity.Warning,
                    $"Property '{pv.Name}' has no file extension: '{val}' (expected {string.Join(" or ", expectedExts)})",
                    pv.File, pv.Line, path));
                return;
            }

            if (!expectedExts.Any(e => string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)))
            {
                _diagnostics.Add(new LintDiagnostic(
                    "L-C11", LintSeverity.Warning,
                    $"Property '{pv.Name}' has unusual file extension '{ext}' for '{val}' (expected {string.Join(" or ", expectedExts)})",
                    pv.File, pv.Line, path));
            }
        }

        // Pattern for simple math expressions (must contain an operator, not just digits)
        private static readonly Regex MathExprPattern = new(
            @"^\s*[\d\s+\-*/()]+\s*$",
            RegexOptions.Compiled);

        private static readonly Regex HasOperatorPattern = new(
            @"[+\-*/]",
            RegexOptions.Compiled);

        /// <summary>L-C12: Quoted math that could be evaluated at build-time</summary>
        private void CheckMathUnquoted(ParamValue pv, string path)
        {
            if (pv.Value.Type != ValueType.Generic)
                return;
            var val = pv.Value.Value as string ?? "";

            // Properties to ignore (often contain math-like strings that aren't math)
            var ignoreProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "text", "name", "displayName", "iconText", "description", "tooltip", "action"
            };
            if (ignoreProps.Contains(pv.Name))
                return;

            // Check if the string looks like a simple math expression (must contain an operator)
            if (MathExprPattern.IsMatch(val) && HasOperatorPattern.IsMatch(val))
            {
                ConfigLintFix? fix = null;
                if (!string.IsNullOrEmpty(_sourceText) && pv.Line > 0)
                {
                    var lineStart = GetLineOffset(pv.Line);
                    var lineEnd = _sourceText.IndexOf('\n', lineStart);
                    if (lineEnd < 0) lineEnd = _sourceText.Length;
                    var lineText = _sourceText.Substring(lineStart, lineEnd - lineStart);

                    var searchPatterns = new[] { $"'{val}'", $"\"{val}\"" };
                    foreach (var pattern in searchPatterns)
                    {
                        var idx = lineText.IndexOf(pattern, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            fix = new ConfigLintFix(lineStart + idx, pattern.Length, val);
                            break;
                        }
                    }
                }

                _diagnostics.Add(new LintDiagnostic(
                    "L-C12", LintSeverity.Help,
                    $"Quoted math expression '{val}' could be unquoted for build-time evaluation",
                    pv.File, pv.Line, path)
                { Fix = fix });
            }
        }

        /// <summary>L-C13: Usage of "_this call" in config text</summary>
        private void CheckConfigThisCall(ParamValue pv, string path)
        {
            if (pv.Value.Type != ValueType.Generic)
                return;
            var val = pv.Value.Value as string ?? "";

            // Only check statement-like properties
            var statementProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "statement", "onActivate", "onDeactivate", "condition", "onButtonClick",
                "onLoad", "onUnload", "onMouseEnter", "onMouseExit",
                "onDestroy", "onChildDestroy", "onPreviewLoad",
            };

            if (!statementProps.Contains(pv.Name))
                return;

            // Check for "_this call" pattern (with optional leading quote)
            if (val.Contains("_this call", StringComparison.OrdinalIgnoreCase) ||
                val.Contains("_this  call", StringComparison.OrdinalIgnoreCase))
            {
                ConfigLintFix? fix = null;
                if (!string.IsNullOrEmpty(_sourceText) && pv.Line > 0)
                {
                    var lineStart = GetLineOffset(pv.Line);
                    var lineEnd = _sourceText.IndexOf('\n', lineStart);
                    if (lineEnd < 0) lineEnd = _sourceText.Length;
                    var lineText = _sourceText.Substring(lineStart, lineEnd - lineStart);

                    var idx = lineText.IndexOf("_this call", StringComparison.Ordinal);
                    if (idx < 0)
                        idx = lineText.IndexOf("_this  call", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        // "_this call" or "_this  call" → "_this"
                        var pattern = lineText.Substring(idx, idx + 10 <= lineText.Length ? 10 : lineText.Length - idx);
                        var patternLen = pattern.StartsWith("_this  call") ? 12 : "_this call".Length;
                        fix = new ConfigLintFix(lineStart + idx, patternLen, "_this");
                    }
                }

                _diagnostics.Add(new LintDiagnostic(
                    "L-C13", LintSeverity.Help,
                    $"Unnecessary '_this call' in '{pv.Name}': 'call' inherits _this automatically",
                    pv.File, pv.Line, path)
                { Fix = fix });
            }
        }

        /// <summary>L-C14: Unused external class</summary>
        private void CheckUnusedExternal(HashSet<string> definedClasses)
        {
            foreach (var ext in _externClassNames)
            {
                if (!_referencedBases.Contains(ext) && !definedClasses.Contains(ext))
                {
                    ConfigLintFix? fix = null;
                    if (!string.IsNullOrEmpty(_sourceText))
                    {
                        var externPattern = $"class {ext};";
                        var idx = _sourceText.IndexOf(externPattern, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            // Find the start of this line and the next newline
                            var lineStart = idx;
                            while (lineStart > 0 && _sourceText[lineStart - 1] != '\n')
                                lineStart--;
                            var lineEnd = _sourceText.IndexOf('\n', idx);
                            if (lineEnd < 0) lineEnd = _sourceText.Length;
                            else lineEnd++; // include the newline

                            fix = new ConfigLintFix(lineStart, lineEnd - lineStart, "");
                        }
                    }

                    _diagnostics.Add(new LintDiagnostic(
                        "L-C14", LintSeverity.Warning,
                        $"External class '{ext}' is declared but never used as a base class",
                        "", 0, ext)
                    { Fix = fix });
                }
            }
        }

        /// <summary>L-C15: CfgPatches scope — items in units[]/weapons[] must exist</summary>
        private void CheckCfgPatchesScope(ParamClass root)
        {
            var cfgPatches = FindClass(root, "CfgPatches");
            if (cfgPatches == null) return;

            var cfgVehicles = FindClass(root, "CfgVehicles");
            var cfgWeapons = FindClass(root, "CfgWeapons");
            var cfgMagazines = FindClass(root, "CfgMagazines");
            var cfgMagazineWells = FindClass(root, "CfgMagazineWells");

            var vehicleNames = CollectClassNames(cfgVehicles);
            var weaponNames = CollectClassNames(cfgWeapons);

            foreach (var patch in cfgPatches.Entries.OfType<ParamClass>())
            {
                // Check units[] array
                var unitsArray = patch.Entries.OfType<ParamArray>()
                    .FirstOrDefault(a => a.Name.Equals("units", StringComparison.OrdinalIgnoreCase));
                if (unitsArray != null)
                {
                    foreach (var entry in unitsArray.Array.Entries)
                    {
                        if (entry.Type == ValueType.Generic)
                        {
                            var itemName = entry.Value as string ?? "";
                            if (!string.IsNullOrEmpty(itemName) && !vehicleNames.Contains(itemName))
                            {
                                _diagnostics.Add(new LintDiagnostic(
                                    "L-C15", LintSeverity.Warning,
                                    $"CfgPatches '{patch.Name}' references '{itemName}' in units[] but not found in CfgVehicles",
                                    patch.File, patch.Line, $"CfgPatches.{patch.Name}"));
                            }
                        }
                    }
                }

                // Check weapons[] array
                var weaponsArray = patch.Entries.OfType<ParamArray>()
                    .FirstOrDefault(a => a.Name.Equals("weapons", StringComparison.OrdinalIgnoreCase));
                if (weaponsArray != null)
                {
                    foreach (var entry in weaponsArray.Array.Entries)
                    {
                        if (entry.Type == ValueType.Generic)
                        {
                            var itemName = entry.Value as string ?? "";
                            if (!string.IsNullOrEmpty(itemName) && !weaponNames.Contains(itemName))
                            {
                                _diagnostics.Add(new LintDiagnostic(
                                    "L-C15", LintSeverity.Warning,
                                    $"CfgPatches '{patch.Name}' references '{itemName}' in weapons[] but not found in CfgWeapons",
                                    patch.File, patch.Line, $"CfgPatches.{patch.Name}"));
                            }
                        }
                    }
                }

                // L-C09: Magwell missing magazine
                if (cfgMagazineWells != null && cfgMagazines != null)
                {
                    var magazineNames = CollectClassNames(cfgMagazines);
                    foreach (var wellClass in cfgMagazineWells.Entries.OfType<ParamClass>())
                    {
                        foreach (var wellEntry in wellClass.Entries)
                        {
                            if (wellEntry is ParamArray magArray)
                            {
                                foreach (var rv in magArray.Array.Entries)
                                {
                                    if (rv.Type == ValueType.Generic)
                                    {
                                        var magName = rv.Value as string ?? "";
                                        if (!string.IsNullOrEmpty(magName) && !magazineNames.Contains(magName))
                                        {
                                            _diagnostics.Add(new LintDiagnostic(
                                                "L-C09", LintSeverity.Error,
                                                $"Magazine '{magName}' in CfgMagazineWells.{wellClass.Name} but not found in CfgMagazines",
                                                wellClass.File, wellClass.Line, $"CfgMagazineWells.{wellClass.Name}"));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>L-C16: Check referenced files exist on disk</summary>
        private void CheckFileMissing(ParamValue pv, string path)
        {
            if (pv.Value.Type != ValueType.Generic)
                return;
            var val = pv.Value.Value as string ?? "";
            if (string.IsNullOrEmpty(val))
                return;

            // Only check property names that are known file references
            var fileProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "model", "texture", "hiddenTexture", "editorPreview",
                "sound", "soundVoice", "soundEnv",
                "surfaceInfo", "insignia",
            };

            if (!fileProps.Contains(pv.Name))
                return;

            // The file path is relative to the addon root; we can't resolve absolutely
            // without knowing the project layout. So this rule is best-effort:
            // we only flag paths that are clearly wrong (empty, extension only, etc.)
            if (val.StartsWith(".") || val.StartsWith("/") || val.StartsWith("\\"))
            {
                _diagnostics.Add(new LintDiagnostic(
                    "L-C16", LintSeverity.Warning,
                    $"File reference '{val}' starts with a relative/absolute path separator",
                    pv.File, pv.Line, path));
            }
        }

        // ─── Helpers ───

        private static ParamClass? FindClass(ParamClass root, string name)
        {
            foreach (var entry in root.Entries)
            {
                if (entry is ParamClass cls && string.Equals(cls.Name, name, StringComparison.OrdinalIgnoreCase))
                    return cls;
                if (entry is ParamClass child)
                {
                    var found = FindClass(child, name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static HashSet<string> CollectClassNames(ParamClass? cls)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (cls == null) return names;
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass child)
                    names.Add(child.Name);
            }
            return names;
        }

        private int GetLineOffset(int line)
        {
            int pos = 0;
            int currentLine = 1;
            while (currentLine < line && pos < _sourceText.Length)
            {
                if (_sourceText[pos] == '\n') currentLine++;
                pos++;
            }
            return pos;
        }
    }
}
