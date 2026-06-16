using BIS.PBO;
using BIS.PBO.Deobfuscator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BIS.Core.Config;
using ConfigValueType = BIS.Core.Config.ValueType;

namespace BIS.PBO.Deobfuscator.Profiles.Specialized
{
    public static class ProfileUtils
    {
        public static string NormalizePath(string pboPath) => pboPath.Replace('\\', '/');

        public static string GetFileName(string pboPath)
        {
            var norm = NormalizePath(pboPath);
            return norm.Contains('/') ? norm.Substring(norm.LastIndexOf('/') + 1) : norm;
        }

        public static string GetDirectoryName(string pboPath)
        {
            var norm = NormalizePath(pboPath);
            var idx = norm.LastIndexOf('/');
            return idx >= 0 ? norm.Substring(0, idx) : "";
        }

        public static void AddToLookup(Dictionary<string, List<string>> lookup, string key, string path)
        {
            if (!lookup.TryGetValue(key, out var list))
            {
                lookup[key] = new List<string> { path };
                return;
            }

            bool isAscii = !path.Any(c => c > 127);
            int insertIdx = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(path, StringComparison.OrdinalIgnoreCase))
                    return;
                if (isAscii && list[i].Any(c => c > 127) && insertIdx < 0)
                    insertIdx = i;
            }

            if (isAscii && insertIdx >= 0)
                list.Insert(insertIdx, path);
            else
                list.Add(path);
        }

        public static string? FindUnusedMatch(
            Dictionary<string, List<string>> lookup,
            string key,
            HashSet<string> used)
        {
            if (!lookup.TryGetValue(key, out var candidates))
                return null;

            return candidates.FirstOrDefault(c => !used.Contains(c));
        }

        public static List<string> ExtractPathsFromRap(ParamClass cls)
        {
            var results = new List<string>();
            var pathPattern = new Regex(
                @".+\.[a-zA-Z0-9]{2,5}$",
                RegexOptions.Compiled);

            CollectStringValues(cls, results, pathPattern);
            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Accepts paths containing Cyrillic obfuscation chars (> 127) and wildcards (?/*)
        /// which are common in obfuscated config.bin paths. Only rejects control chars
        /// and the Unicode replacement character (bad UTF-8 decoding).
        /// </summary>
        public static bool IsValidPathString(string s) =>
            s.All(c => c >= 32 && c != '\uFFFD');

        private static void CollectStringValues(ParamClass cls, List<string> results, Regex pathPattern)
        {
            foreach (var entry in cls.Entries)
            {
                switch (entry)
                {
                    case ParamClass nested:
                        CollectStringValues(nested, results, pathPattern);
                        break;

                    case ParamValue pv when pv.Value.Type == ConfigValueType.Generic || pv.Value.Type == ConfigValueType.Expression:
                        var strVal = pv.Value.Value as string;
                        if (!string.IsNullOrEmpty(strVal) && strVal.Contains('\\') && pathPattern.IsMatch(strVal) && IsValidPathString(strVal))
                            results.Add(strVal.Replace('/', '\\'));
                        break;

                    case ParamArray pa:
                        foreach (var rv in pa.Array.Entries)
                        {
                            if ((rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression) && rv.Value is string s)
                            {
                                if (!string.IsNullOrEmpty(s) && s.Contains('\\') && pathPattern.IsMatch(s) && IsValidPathString(s))
                                    results.Add(s.Replace('/', '\\'));
                            }
                        }
                        break;

                    case ParamArraySpec pas:
                        foreach (var rv in pas.Array.Entries)
                        {
                            if ((rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression) && rv.Value is string s)
                            {
                                if (!string.IsNullOrEmpty(s) && s.Contains('\\') && pathPattern.IsMatch(s) && IsValidPathString(s))
                                    results.Add(s.Replace('/', '\\'));
                            }
                        }
                        break;
                }
            }
        }

        public static List<string> ExtractClassNames(ParamClass root)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CfgPatches", "CfgWeapons", "CfgVehicles", "CfgMagazines",
                "CfgAmmo", "CfgGlasses", "CfgUnitInsignia", "CfgFactionClasses",
                "CfgEditorCategories", "CfgEditorSubcategories", "CfgMods",
                "XtdGearModels", "XtdGearInfos", "units", "weapons", "items",
                "containers", "accessories", "ammunition", "grenades",
                "launchers", "missiles", "bombs", "mines", "explosives",
                "throw", "put", "Default", "WeaponSlotInfo", "SlotInfo",
                "PointerSlot", "MuzzleSlot", "CowsSlot", "UnderbarrelSlot",
                "CfgNonAIVehicles", "CfgMarkerColors", "CfgMarkerShapes",
                "CfgWaypoints", "CfgActionSounds", "CfgCloudlets",
                "CfgSoundShaders", "CfgSoundSets", "CfgUnitSounds",
                "CfgMusic", "CfgRadio", "CfgVoice", "CfgSFX",
                "CfgWorlds", "CfgWorldList"
            };

            CollectClassNames(root, results, seen, exclude);
            return results;
        }

        private static void CollectClassNames(ParamClass cls, List<string> names, HashSet<string> seen, HashSet<string> exclude)
        {
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass nested)
                {
                    if (nested.Name != null &&
                        nested.Name.Length >= 3 &&
                        !exclude.Contains(nested.Name) &&
                        seen.Add(nested.Name))
                    {
                        names.Add(nested.Name);
                    }
                    CollectClassNames(nested, names, seen, exclude);
                }
            }
        }

        /// <summary>
        /// Strips known color/camo suffixes from the end of a class name.
        /// E.g. "ADAMS_AVS_BELT_MC_TAN" → "ADAMS_AVS_BELT",
        ///      "JSOAR_AVS_MC_BLK" → "JSOAR_AVS",
        ///      "UKSF_MOD3_TAN" → "UKSF_MOD3".
        /// Tokens are stripped from the right while they match known color/camo tokens.
        /// Returns the original name if no suffixes are removed.
        /// </summary>
        public static string StripColorSuffixes(string className)
        {
            var tokens = className.Split('_');
            int stripCount = 0;
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                if (_colorTokens.Contains(tokens[i]))
                    stripCount++;
                else
                    break;
            }
            return stripCount > 0
                ? string.Join("_", tokens, 0, tokens.Length - stripCount)
                : className;
        }

        private static readonly HashSet<string> _colorTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "mc", "mcb", "mct",           // multi-cam variants
            "aor1", "aor2",               // AOR patterns
            "blk", "bl", "black",         // black
            "tan",                         // tan
            "sim",                         // suit-intermediate
            "nb",                          // no-belt (variant modifier)
            "olv", "oli", "olive",         // olive
            "grn", "green",                // green
            "khk", "khaki",                // khaki
            "gry", "grey", "gray",         // grey
            "coy", "coyote",               // coyote brown
            "rgr",                         // ranger green
            "arid", "tropic", "alpine", "winter", "desert", "woodland",
            "wd", "dcu", "ocp", "dpcu", "erdl", "flecktarn", "flktn",
            "multicam", "kryptek", "cadpat", "marpat", "atacs", "atacsfg", "atacsxg",
            "nwu", "ttsk",
        };

        public static Dictionary<string, List<string>> BuildSuffixToClassMap(List<string> classNames, string prefix)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var cls in classNames)
            {
                var normalized = cls;
                if (!string.IsNullOrEmpty(prefix))
                {
                    var prefixClean = prefix.TrimEnd('/');
                    // Only strip PBO prefix if followed by underscore to prevent
                    // partial-word matches (e.g., prefix "avs" should not match
                    // "avs_assault_vest" unless explicitly "avs_").
                    if (normalized.StartsWith(prefixClean + "_", StringComparison.OrdinalIgnoreCase))
                        normalized = normalized.Substring(prefixClean.Length + 1).TrimStart('/');
                }

                var words = normalized.Split('_')
                    .Where(w => w.Length >= 3 && !w.All(char.IsDigit))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var word in words)
                {
                    if (!map.ContainsKey(word))
                        map[word] = new List<string>();
                    if (!map[word].Contains(normalized))
                        map[word].Add(normalized);
                }
            }

            return map;
        }

        /// <summary>
        /// Attempts to parse a PBO entry as a config file (binary raP or text format).
        /// Returns null if the entry is not a valid config or an unrecoverable format.
        /// Detection: first 4 bytes == \0raP → binary; otherwise → text.
        /// </summary>
        public static ParamFile? TryParseBinEntry(IPBOFileEntry entry)
        {
            using var stream = entry.OpenRead();
            var header = new byte[4];
            if (stream.Read(header, 0, 4) < 4)
                return null;

            stream.Position = 0;

            // Binary raP format: starts with \0raP (0x00 'r' 'a' 'P')
            if (header[0] == 0x00 && header[1] == 0x72 && header[2] == 0x61 && header[3] == 0x50)
            {
                return new ParamFile(stream);
            }

            // Text format: read to string, tokenize, parse (skip preprocessor —
            // split configs like CfgFunctions rarely use #include)
            using var reader = new StreamReader(stream);
            var source = reader.ReadToEnd();
            var tokens = ConfigTokenizer.Tokenize(source, entry.FileName);
            var parser = new ConfigParser();
            return parser.Parse(tokens);
        }
    }
}
