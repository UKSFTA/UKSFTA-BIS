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

        public static bool IsValidPathString(string s) =>
            s.All(c => c >= 32 && c <= 126) && !s.Contains('?') && !s.Contains('*');

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

        public static Dictionary<string, List<string>> BuildSuffixToClassMap(List<string> classNames, string prefix)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var cls in classNames)
            {
                var normalized = cls;
                if (!string.IsNullOrEmpty(prefix))
                {
                    var prefixClean = prefix.TrimEnd('/');
                    if (normalized.StartsWith(prefixClean, StringComparison.OrdinalIgnoreCase))
                        normalized = normalized.Substring(prefixClean.Length);
                }

                var words = normalized.Split('_')
                    .Where(w => w.Length >= 3 && !w.All(char.IsDigit))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var word in words)
                {
                    if (!map.ContainsKey(word))
                        map[word] = new List<string>();
                    if (!map[word].Contains(cls))
                        map[word].Add(cls);
                }
            }

            return map;
        }
    }
}
