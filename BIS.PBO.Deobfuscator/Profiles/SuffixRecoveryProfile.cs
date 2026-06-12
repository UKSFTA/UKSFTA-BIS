using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BIS.Core.Config;
using ConfigValueType = BIS.Core.Config.ValueType;
using BIS.PBO;
using BIS.PBO.Deobfuscator.Format;
using P3DModel = BIS.P3D.P3D;

namespace BIS.PBO.Deobfuscator.Profiles
{
public class SuffixRecoveryProfile : IObfuscationProfile
{
        private static readonly string[] RealExtensions = new[]
        {
            ".paa", ".p3d", ".rvmat", ".dll", ".so"
        };

        private static readonly Regex RandomNamePattern = new Regex(
            @"^[A-Za-z0-9]{2,12}$",
            RegexOptions.Compiled
        );
    /// <summary>
    /// Handles PBOs whose filenames have been stripped to suffix-only patterns.
    ///
    /// These PBOs have file entries where the base name has been truncated, leaving
    /// only the suffix and extension (e.g. "data\abav\_as.paa" instead of
    /// "data\abav\avs_assault_as.paa") or just an extension with no base name
    /// (e.g. "acex\.paa"). The folder structure and extension are preserved.
    ///
    /// Detection uses structural heuristics:
    ///   - File entries whose names consist only of _suffix.ext or .ext
    ///   - Presence of a config.bin for cross-reference recovery
    ///
    /// Recovery strategy: scan the binarized config.bin for ASCII path strings that
    /// reference the original full file paths. Cross-reference by folder + suffix
    /// to rebuild names where possible.
    /// </summary>
        public string ProfileName => "Suffix-based Recovery";

        // PBO paths use \ as separator; normalize to / for cross-platform Path methods
        private static string NormalizePath(string pboPath) => pboPath.Replace('\\', '/');

        private static string GetFileName(string pboPath)
        {
            var norm = NormalizePath(pboPath);
            return norm.Contains('/') ? norm.Substring(norm.LastIndexOf('/') + 1) : norm;
        }

        private static string GetDirectoryName(string pboPath)
        {
            var norm = NormalizePath(pboPath);
            var idx = norm.LastIndexOf('/');
            return idx >= 0 ? norm.Substring(0, idx) : "";
        }

        public bool IsMatch(BIS.PBO.PBO pbo)
        {
            // Check for decoy markers (merged from DecoyInjectionProfile)
            int longProps = pbo.PropertiesPairs.Count(p =>
                p.Key.Length > 40 || p.Value.Length > 40);
            int zeroByteFiles = pbo.Files.Count(f => f.Size == 0);
            if (longProps >= 2 && zeroByteFiles >= 1) return true;

            bool hasStrippedNames = pbo.Files.Any(f =>
            {
                var name = GetFileName(f.FileName);

                // Match: extension-only names like ".paa", ".rvmat"
                if (name.StartsWith("."))
                    return true;

                // Match: suffix-only names like "_as.paa", "_mc.paa"
                // Exclude generic "_unknown_*" patterns produced by sanitization
                if (name.StartsWith("_") && !name.StartsWith("_unknown_"))
                    return true;

                // Match: Cyrillic characters in filename
                if (name.Any(c => c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я'))
                    return true;

                return false;
            });

            return hasStrippedNames;
        }

        public DeobfuscationResult Deobfuscate(BIS.PBO.PBO pbo)
        {
            var result = new DeobfuscationResult { MatchedProfile = ProfileName };

            Console.WriteLine("  -> Parsing config.bin for path references...");

            var config = pbo.GetRootConfig();
            if (config == null)
            {
                Console.WriteLine("  -> No config.bin found, skipping context recovery.");
                return result;
            }

            var knownPaths = ExtractPathsFromRap(config.Root);
            Console.WriteLine($"  -> Found {knownPaths.Count} candidate path strings in config.bin.");
            var prefix = NormalizePath(pbo.Prefix ?? "");

            // Step 1: Detect and filter out decoy files and stub scripts
            Console.WriteLine("  -> Scanning files for decoy injection markers...");
            int decoys = 0;
            int stubs = 0;
            int entryPoints = 0;

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                var file = pbo.Files[i];
                string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                string nameOnly = Path.GetFileNameWithoutExtension(file.FileName);
                bool hasDirectory = file.FileName.Contains('\\');

                // Zero-byte files are known decoy entries
                if (file.Size == 0)
                {
                    result.FilteredOut.Add(i);
                    decoys++;
                    continue;
                }

                // Stub scripts are small files with random names
                if (file.Size < 20 && RandomNamePattern.IsMatch(nameOnly))
                {
                    result.FilteredOut.Add(i);
                    stubs++;
                    continue;
                }

                // Entry point scripts are larger files with known extensions
                if (RealExtensions.Contains(ext) && file.Size > 100)
                {
                    entryPoints++;
                }
            }

            result.Stats["Decoys"] = decoys;
            result.Stats["Stubs"] = stubs;
            result.Stats["EntryPoints"] = entryPoints;
            result.Stats["Genuine"] = pbo.Files.Count - decoys - stubs;

            // Step 2: scan P3D/ODOL files for embedded texture/material paths
            Console.WriteLine("  -> Scanning P3D files for embedded paths...");
            int p3dScanned = 0;
            int p3dPaths = 0;
            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var file = pbo.Files[fi];
                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".p3d", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var stream = file.OpenRead();
                    var p3d = new P3DModel(stream);
                    p3dScanned++;

                    foreach (var lod in p3d.LODs)
                    {
                        foreach (var tex in lod.GetTextures())
                        {
                            var norm = NormalizePath(tex);
                            if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                            {
                                knownPaths.Add(norm);
                                p3dPaths++;
                            }
                        }
                        foreach (var mat in lod.GetMaterials())
                        {
                            var norm = NormalizePath(mat);
                            if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                            {
                                knownPaths.Add(norm);
                                p3dPaths++;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            if (p3dScanned > 0)
                Console.WriteLine($"  -> Scanned {p3dScanned} P3D files, extracted {p3dPaths} unique paths (total candidates: {knownPaths.Count}).");
            else
                Console.WriteLine($"  -> No P3D files found in PBO.");

            // Step 3b: scan standalone RVMAT files for texture/material references
            int rvmatScanned = 0;
            int rvmatPaths = 0;
            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var file = pbo.Files[fi];
                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".rvmat", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var rvmat = file.ReadAsConfig();
                    rvmatScanned++;
                    var rvmatExtracted = ExtractPathsFromRap(rvmat.Root);
                    foreach (var path in rvmatExtracted)
                    {
                        var norm = NormalizePath(path);
                        if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                        {
                            knownPaths.Add(norm);
                            rvmatPaths++;
                        }
                    }
                }
                catch
                {
                }
            }
            if (rvmatScanned > 0)
                Console.WriteLine($"  -> Scanned {rvmatScanned} RVMAT files, extracted {rvmatPaths} unique paths (total candidates: {knownPaths.Count}).");

            // Step 3c: scan texHeaders.bin for texture path references
            int texHeaderPaths = 0;
            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var file = pbo.Files[fi];
                if (!file.FileName.Equals("texHeaders.bin", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var stream = file.OpenRead();
                    var texHeaders = TexHeaders.Read(stream);
                    foreach (var tex in texHeaders.Textures)
                    {
                        var norm = NormalizePath(tex.PAAFile);
                        if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                        {
                            knownPaths.Add(norm);
                            texHeaderPaths++;
                        }
                    }
                    Console.WriteLine($"  -> Scanned texHeaders.bin: {texHeaders.Textures.Count} entries, {texHeaderPaths} new paths (total candidates: {knownPaths.Count}).");
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    if (msg.Length > 80) msg = msg[..80] + "...";
                    Console.WriteLine($"  -> texHeaders.bin parse failed: {ex.GetType().Name}: {msg}");
                }
                break;
            }

            // Strip PBO prefix from all candidate paths
            if (!string.IsNullOrEmpty(prefix))
            {
                var prefixSlash = prefix.TrimEnd('/') + "/";
                for (int i = 0; i < knownPaths.Count; i++)
                {
                    var norm = NormalizePath(knownPaths[i]);
                    if (norm.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase))
                        knownPaths[i] = norm.Substring(prefixSlash.Length);
                }
            }

            int total = pbo.Files.Count;
            int recovered = 0;
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Step 4a: heuristic class-name-to-filename matching (priority)
            // Uses clean class names from config.bin rather than obfuscated path strings
            var classNames = ExtractClassNames(config.Root);
            var modPrefix = DetectModPrefix(classNames, prefix);
            if (modPrefix != null)
                Console.WriteLine($"  -> Detected mod prefix: \"{modPrefix}\" — stripping from class names");
            var suffixToClass = BuildSuffixToClassMap(classNames, prefix, modPrefix);
            if (suffixToClass.Count > 0)
            {
                Console.WriteLine($"  -> Matching {classNames.Count} class names to {total} files...");
                for (int i = 0; i < total; i++)
                {
                    if (result.RecoveredNames.ContainsKey(i))
                        continue;

                    var file = pbo.Files[i];
                    var dir = GetDirectoryName(file.FileName);
                    var name = GetFileName(file.FileName);

                    if (!name.StartsWith("_") && !name.StartsWith("."))
                        continue;

                    var ext = Path.GetExtension(name);
                    var suffix = Path.GetFileNameWithoutExtension(name);

                    bool matched = false;
                    foreach (var kvp in suffixToClass)
                    {
                        var dirWord = kvp.Key;
                        var candidates = kvp.Value;

                        if (!dir.Contains(dirWord, StringComparison.OrdinalIgnoreCase) &&
                            !dirWord.Contains(dir.Replace("/", ""), StringComparison.OrdinalIgnoreCase))
                            continue;

                        foreach (var cls in candidates)
                        {
                            var reconstructed = $"{dir}/{cls.ToLowerInvariant()}{suffix}{ext}";
                            if (!usedPaths.Contains(reconstructed))
                            {
                                Console.WriteLine($"  -> Class match: {file.FileName}  =>  {reconstructed}");
                                result.RecoveredNames[i] = reconstructed;
                                usedPaths.Add(reconstructed);
                                recovered++;
                                matched = true;
                                break;
                            }
                        }
                        if (matched)
                            break;
                    }
                }
            }

            // Step 4b: build path lookup keyed by (folder, suffix+extension)
            // Fallback for files not recovered by class-name matching
            var pathLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in knownPaths)
            {
                var dir = GetDirectoryName(path);
                var file = GetFileName(path);

                var suffixMatch = Regex.Match(file, @"(_[^_]+\.[a-zA-Z0-9]+)$", RegexOptions.IgnoreCase);
                if (suffixMatch.Success)
                {
                    var key = $"{dir}|{suffixMatch.Value}".ToLowerInvariant();
                    AddToLookup(pathLookup, key, path);
                }

                var extKey = $"{dir}|{Path.GetExtension(file)}".ToLowerInvariant();
                AddToLookup(pathLookup, extKey, path);
            }

            // Step 4c: fallback path-based matching for remaining entries
            for (int i = 0; i < total; i++)
            {
                if (result.RecoveredNames.ContainsKey(i))
                    continue;

                var file = pbo.Files[i];
                var dir = GetDirectoryName(file.FileName);
                var name = GetFileName(file.FileName);

                if (!name.StartsWith("_") && !name.StartsWith("."))
                    continue;

                var suffixKey = $"{dir}|{name}".ToLowerInvariant();
                string? matchedPath = FindUnusedMatch(pathLookup, suffixKey, usedPaths);

                if (matchedPath == null)
                {
                    var extKey = $"{dir}|{Path.GetExtension(name)}".ToLowerInvariant();
                    matchedPath = FindUnusedMatch(pathLookup, extKey, usedPaths);
                }

                if (matchedPath != null)
                {
                    Console.WriteLine($"  -> Path match: {file.FileName}  =>  {matchedPath}");
                    result.RecoveredNames[i] = matchedPath;
                    usedPaths.Add(matchedPath);
                    recovered++;
                }
            }

            result.Stats["Recovered"] = recovered;
            result.Stats["Total"] = total;
            result.Stats["Unrecovered"] = total - recovered;
            Console.WriteLine($"  -> Recovery complete. {recovered}/{total} filenames recovered from config.bin context.");

            return result;
        }

        private static string? FindUnusedMatch(
            Dictionary<string, List<string>> lookup,
            string key,
            HashSet<string> used)
        {
            if (!lookup.TryGetValue(key, out var candidates))
                return null;

            return candidates.FirstOrDefault(c => !used.Contains(c));
        }

        private static void AddToLookup(Dictionary<string, List<string>> lookup, string key, string path)
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

        /// <summary>
        /// Recursively walks a parsed raP class tree and collects all string values
        /// that look like file paths (contain directory separators and a file extension).
        /// </summary>
        private static List<string> ExtractPathsFromRap(ParamClass cls)
        {
            var results = new List<string>();
            var pathPattern = new Regex(
                @".+\.[a-zA-Z0-9]{2,5}$",
                RegexOptions.Compiled);

            CollectStringValues(cls, results, pathPattern);
            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsValidPathString(string s) =>
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

        /// <summary>
        /// Recursively collects all class names from the raP tree.
        /// Filters out common BIS config class names and short names.
        /// </summary>
        private static List<string> ExtractClassNames(ParamClass root)
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
                "MuzzleCoef", "AmmoCoef", "MagazineCoef",
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

        /// Detects a common mod prefix from class names (e.g., "jsoar_", "uksf_").
        /// Class-name frequency analysis (>70%), with fallback from PBO prefix property.
        private static string? DetectModPrefix(List<string> classNames, string pboPrefix)
        {
            if (classNames.Count == 0)
                return null;

            var firstWords = classNames
                .Select(n => n.Split('_')[0])
                .Where(w => w.Length >= 2)
                .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Word = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            if (firstWords.Count > 0)
            {
                var mostCommon = firstWords[0];
                if ((double)mostCommon.Count / classNames.Count >= 0.7)
                    return mostCommon.Word + "_";
            }

            // Fallback: derive tag from PBO prefix property
            if (!string.IsNullOrEmpty(pboPrefix))
            {
                var parts = pboPrefix.Split(new[] { '\\', '/' });
                var lastPart = parts[^1];
                var tag = lastPart.Split('_')[0];
                if (tag.Length >= 2 && classNames.Any(n =>
                    n.StartsWith(tag + "_", StringComparison.OrdinalIgnoreCase)))
                    return tag.ToLowerInvariant() + "_";
            }

            return null;
        }

        /// <summary>
        /// Builds a mapping from directory words (e.g., "avs", "abav") to class names
        /// that contain those words. Strips the PBO prefix and mod prefix from class names
        /// before matching, and stores the stripped names for cleaner output filenames.
        /// </summary>
        private static Dictionary<string, List<string>> BuildSuffixToClassMap(List<string> classNames, string prefix, string? modPrefix = null)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var cls in classNames)
            {
                var normalized = cls;

                if (!string.IsNullOrEmpty(modPrefix) &&
                    normalized.StartsWith(modPrefix, StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(modPrefix.Length);

                if (!string.IsNullOrEmpty(prefix))
                {
                    var prefixClean = prefix.TrimEnd('/');
                    if (normalized.StartsWith(prefixClean, StringComparison.OrdinalIgnoreCase))
                        normalized = normalized.Substring(prefixClean.Length);
                }

                if (string.IsNullOrEmpty(normalized))
                    continue;

                var words = normalized.Split('_')
                    .Where(w => w.Length >= 3 && !w.All(char.IsDigit))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var word in words)
                {
                    if (!map.TryGetValue(word, out var list))
                    {
                        map[word] = new List<string> { normalized };
                    }
                    else if (!list.Contains(normalized))
                    {
                        list.Add(normalized);
                    }
                }
            }

            return map;
        }
    }
}
