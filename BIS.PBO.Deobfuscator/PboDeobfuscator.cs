using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BIS.Core.Config;
using BIS.Core.Streams;
using BIS.PBO;
using BIS.PBO.Deobfuscator.Profiles;
using BIS.PBO.Deobfuscator.Profiles.Specialized;

namespace BIS.PBO.Deobfuscator
{
    /// <summary>
    /// Main entry point for PBO analysis. Runs all registered profiles against
    /// the PBO and merges their results. Profiles detect structural patterns
    /// in the PBO rather than tool-specific signatures.
    /// </summary>
    public class PboDeobfuscator
    {
        private readonly List<IObfuscationProfile> _profiles;
        private readonly List<IReferenceUpdater> _referenceUpdaters;

        public PboDeobfuscator()
        {
            _profiles = new List<IObfuscationProfile>
            {
                new DecoyInjectionProfile(),
                new ModularSuffixRecoveryProfile(),
                new SuffixRecoveryProfile(),
                new HeuristicFallbackProfile()
            };
            _referenceUpdaters = new List<IReferenceUpdater>
            {
                new P3DTextureReferenceUpdater(),
                new RVMATReferenceUpdater(),
                new ConfigReferenceUpdater()
            };
        }

        /// <summary>
        /// Register a custom profile for PBO structural analysis.
        /// </summary>
        public void RegisterProfile(IObfuscationProfile profile)
        {
            _profiles.Add(profile);
        }

        public void RegisterReferenceUpdater(IReferenceUpdater updater)
        {
            _referenceUpdaters.Add(updater);
        }

        /// <summary>
        /// Run all registered profiles against the PBO and merge results
        /// from every matching profile. The PBO itself is never modified.
        /// </summary>
        public DeobfuscationResult Process(PBO pbo)
        {
            var matched = new List<string>();
            var mergedResult = new DeobfuscationResult();

            foreach (var profile in _profiles)
            {
                if (!profile.IsMatch(pbo))
                    continue;

                Console.WriteLine($"[Deobfuscator] Matched profile: {profile.ProfileName}");
                matched.Add(profile.ProfileName);

                var result = profile.Deobfuscate(pbo);

                foreach (var kvp in result.RecoveredNames)
                    mergedResult.RecoveredNames[kvp.Key] = kvp.Value;

                foreach (var idx in result.FilteredOut)
                    mergedResult.FilteredOut.Add(idx);

                foreach (var kvp in result.Stats)
                {
                    if (mergedResult.Stats.ContainsKey(kvp.Key))
                        mergedResult.Stats[kvp.Key] += kvp.Value;
                    else
                        mergedResult.Stats[kvp.Key] = kvp.Value;
                }
            }

            if (matched.Count > 0)
            {
                mergedResult.MatchedProfile = matched.Count == 1
                    ? matched[0]
                    : string.Join(" + ", matched);
                return mergedResult;
            }

            Console.WriteLine("[Deobfuscator] No known structural patterns found. PBO appears clean.");
            return mergedResult;
        }

        /// <summary>
        /// Rebuild a clean PBO file from a deobfuscation result.
        /// - Excludes entries in FilteredOut (decoys, stubs, padding)
        /// - Renames entries with heuristic ASCII names when possible
        /// - Falls back to cleaned original names
        /// - Preserves original header properties and timestamps
        /// </summary>
        /// <summary>
        /// Rebuild a clean PBO file from a deobfuscation result.
        /// - Excludes entries in FilteredOut (decoys, stubs, padding)
        /// - Renames entries with heuristic ASCII names when possible
        /// - Falls back to cleaned original names
        /// - Preserves original header properties and timestamps
        /// </summary>
        public void Rebuild(PBO pbo, DeobfuscationResult result, string outputPath)
        {
            // Parse config.bin once — reused by class naming, model map, and image map
            var root = pbo.GetRootConfig()?.Root;

            // Build dir-word → class list from config.bin class names
            // (class names are clean even when file paths in config are obfuscated)
            var wordToClasses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var classNames = root != null ? ExtractDeobfuscatorClassNames(root) : new List<string>();
            var modPrefix = DetectModPrefix(classNames, pbo.Prefix);
            foreach (var cls in classNames)
            {
                var stripped = cls;
                if (!string.IsNullOrEmpty(modPrefix) &&
                    stripped.StartsWith(modPrefix, StringComparison.OrdinalIgnoreCase))
                    stripped = stripped.Substring(modPrefix.Length);

                var clsLower = stripped.ToLowerInvariant();
                var words = clsLower.Split('_').Where(w => w.Length >= 3).Distinct();
                foreach (var w in words)
                {
                    if (!wordToClasses.ContainsKey(w))
                        wordToClasses[w] = new List<string>();
                    if (!wordToClasses[w].Contains(clsLower))
                        wordToClasses[w].Add(clsLower);
                }
            }

            var keep = new List<(int Index, FileEntry Entry)>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int zeroBytesSkipped = 0;

            // Build config model path -> class name map for .p3d naming
            var configModelMap = root != null ? BuildConfigModelMap(root, pbo.Prefix) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Build config image path -> variant name map for icon naming (acex, etc.)
            var configImageMap = root != null ? BuildConfigImageMap(root) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                if (result.FilteredOut.Contains(i))
                    continue;

                var original = pbo.Files[i];
                if (original.Size == 0)
                {
                    zeroBytesSkipped++;
                    continue;
                }
                var origNorm = original.FileName.Replace('\\', '/');
                var slashIdx = origNorm.LastIndexOf('/');
                var dir = slashIdx >= 0 ? origNorm.Substring(0, slashIdx) : "";
                var origFile = slashIdx >= 0 ? origNorm.Substring(slashIdx + 1) : origNorm;

                string? finalName = null;

                // Use recovered name if available (from profile deobfuscation)
                if (result.RecoveredNames.TryGetValue(i, out var recoveredName))
                {
                    finalName = recoveredName;
                }
                // If the original entry was NOT stripped, keep as-is
                else if (!origFile.StartsWith("_") && !origFile.StartsWith("."))
                {
                    finalName = origNorm;
                }
                else
                {
                    var ext = DetectExtension(original);

                    if (configModelMap.Count > 0 && ext == ".p3d")
                    {
                        var rawNorm = original.RawFileName.Replace('\\', '/');
                        if (configModelMap.TryGetValue(rawNorm, out var modelClassName))
                        {
                            var stripped = ProfileUtils.StripColorSuffixes(modelClassName);
                            finalName = $"{dir}/{stripped.ToLowerInvariant()}.p3d";
                            if (usedNames.Contains(finalName))
                                finalName = $"{dir}/{modelClassName.ToLowerInvariant()}.p3d";
                        }
                    }

                    if (finalName == null && configImageMap.Count > 0 && ext == ".paa")
                    {
                        var rawNorm = original.RawFileName.Replace('\\', '/');
                        if (configImageMap.TryGetValue(rawNorm, out var variantName))
                        {
                            var candidate = $"{dir}/{variantName.ToLowerInvariant()}.paa";
                            if (usedNames.Contains(candidate))
                            {
                                // Collision: add numeric suffix
                                for (int n = 2; n < 100; n++)
                                {
                                    var alt = $"{dir}/{variantName.ToLowerInvariant()}_{n}.paa";
                                    if (!usedNames.Contains(alt))
                                    {
                                        candidate = alt;
                                        break;
                                    }
                                }
                            }
                            finalName = candidate;
                        }
                    }

                    if (finalName == null)
                    {
                        // Try heuristic (uses class-name word index)
                        var heur = GenerateHeuristicName(dir, origFile, wordToClasses, usedNames);
                        if (heur != null)
                        {
                            finalName = heur;
                        }
                        else
                        {
                            // Forced fallback: numbered placeholder
                            dirCounters.TryGetValue(dir, out var counter);
                            counter++;
                            dirCounters[dir] = counter;
                            var dirPrefix = dir.Split('/').LastOrDefault() ?? "file";
                            if (dirPrefix == "_unknown")
                                dirPrefix = "file";
                            var baseName = $"{dirPrefix}_{counter:D3}";
                            finalName = $"{dir}/{baseName}{ext}";
                        }
                    }
                }

                usedNames.Add(finalName);
                var pboName = finalName.Replace('\\', '/');
                keep.Add((i, new FileEntry
                {
                    FileName = pboName,
                    TimeStamp = original.TimeStamp,
                    DataSize = original.Size,
                    UncompressedSize = 0,
                    CompressedMagic = 0
                }));
            }

            // --- Reference Updating ---
            // Build normalized path map: original entry path -> output entry path
            // Use RawFileName (preserved obfuscated Cyrillic) as keys so that
            // config.bin references (which contain the same obfuscated paths)
            // can be matched correctly during reference updating.
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // First, add recovered (class-based) names so they take priority
            foreach (var kvp in result.RecoveredNames)
            {
                var orig = pbo.Files[kvp.Key].RawFileName.Replace('\\', '/');
                var recovered = kvp.Value.Replace('\\', '/');
                pathMap[orig] = recovered;
                AddPrefixedPath(pbo.Prefix, orig, recovered, pathMap);
            }
            // Then, add all kept entries that were NOT recovered, so their
            // heuristic/placeholder names are available for reference updating.
            var recoveredIndices = new HashSet<int>(result.RecoveredNames.Keys);
            foreach (var (idx, entry) in keep)
            {
                if (recoveredIndices.Contains(idx))
                    continue;
                var orig = pbo.Files[idx].RawFileName.Replace('\\', '/');
                var final = entry.FileName.Replace('\\', '/');
                if (!pathMap.ContainsKey(orig))
                    pathMap[orig] = final;
                AddPrefixedPath(pbo.Prefix, orig, final, pathMap);
            }

            // Collect modified entry content from reference updaters
            var modifiedData = new Dictionary<int, byte[]>();
            foreach (var updater in _referenceUpdaters)
            {
                foreach (var (idx, entry) in keep)
                {
                    if (modifiedData.ContainsKey(idx))
                        continue;
                    var data = updater.UpdateReferences(pbo.Files[idx], pathMap);
                    if (data != null)
                    {
                        modifiedData[idx] = data;
                        entry.DataSize = data.Length;
                    }
                }
            }
            // --------------------------

            // Compute offsets
            var offset = 0;
            foreach (var (_, entry) in keep)
            {
                entry.StartOffset = offset;
                offset += entry.DataSize;
            }

            using (var target = File.Create(outputPath))
            {
                using (var output = new BinaryWriterEx(target, true))
                {
                    // Strip decoy/obfuscation properties; keep only prefix (and product if set)
                    var cleanProps = pbo.PropertiesPairs
                        .Where(p => p.Key.Equals("prefix", StringComparison.OrdinalIgnoreCase) ||
                                    p.Key.Equals("product", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    WriteProperties(output, cleanProps);
                    WriteBasicHeader(output, keep.Select(k => k.Entry));
                }
                foreach (var (idx, _) in keep)
                {
                    if (modifiedData.TryGetValue(idx, out var data))
                        target.Write(data, 0, data.Length);
                    else
                        using (var source = pbo.Files[idx].OpenRead())
                            source.CopyTo(target);
                }
                target.Position = 0;
                using (var sha1 = SHA1.Create())
                {
                    var hash = sha1.ComputeHash(target);
                    target.WriteByte(0x0);
                    target.Write(hash, 0, 20);
                }
            }

            var kept = keep.Count;
            var removed = result.FilteredOut.Count + zeroBytesSkipped;
            Console.WriteLine($"  -> Rebuilt: {outputPath} ({kept} files kept, {removed} removed)");
            if (zeroBytesSkipped > 0)
                Console.WriteLine($"  -> Skipped {zeroBytesSkipped} zero-byte padding entries.");
        }

        /// <summary>
        /// Reads the first bytes of a PBO entry to detect the actual file type
        /// and return the correct extension. Falls back to the original extension
        /// when the type is unknown.
        /// </summary>
        private static string DetectExtension(IPBOFileEntry entry)
        {
            try
            {
                using var stream = entry.OpenRead();
                byte[] header = new byte[4];
                int read = stream.Read(header, 0, 4);
                if (read < 4)
                    return Path.GetExtension(entry.FileName) ?? ".bin";

                // raP\0 = binarized config or RVMAT
                if (header[0] == 'r' && header[1] == 'a' && header[2] == 'P' && header[3] == 0)
                    return ".bin";

                // \0raS = PAA
                if (header[0] == 0 && header[1] == 'r' && header[2] == 'a' && header[3] == 'S')
                    return ".paa";

                // PAA: first 2 bytes as LE uint16
                ushort fmt = (ushort)(header[0] | (header[1] << 8));
                if (fmt == 0xFF01 || fmt == 0xFF02 || fmt == 0xFF03 ||
                    fmt == 0xFF04 || fmt == 0xFF05 || fmt == 0x4444 ||
                    fmt == 0x8080 || fmt == 0x1555)
                    return ".paa";

                // PAA: also matches \0raS (handled above), but some PAA variants
                // start with the format code directly — the above covers DXT1-5 and common formats.

                // ODOL or MLOD = P3D model
                if (header[0] == 'O' && header[1] == 'D' && header[2] == 'O' && header[3] == 'L')
                    return ".p3d";
                if (header[0] == 'M' && header[1] == 'L' && header[2] == 'O' && header[3] == 'D')
                    return ".p3d";

                // 0DHT = texHeaders cache
                if (header[0] == '0' && header[1] == 'D' && header[2] == 'H' && header[3] == 'T')
                    return ".bin";

                // Text files: start with printable ASCII, BOM, or common script chars
                if (header[0] >= 0x20 && header[0] <= 0x7E)
                    return Path.GetExtension(entry.FileName) ?? ".txt";

                return Path.GetExtension(entry.FileName) ?? ".bin";
            }
            catch
            {
                return Path.GetExtension(entry.FileName) ?? ".bin";
            }
        }

        private static string? GenerateHeuristicName(
            string dir, string origFile,
            Dictionary<string, List<string>> wordToClasses,
            HashSet<string> usedNames)
        {
            if (wordToClasses.Count == 0)
                return null;

            var dirWords = dir.Split('/')
                .SelectMany(s => s.Split('_'))
                .Where(w => w.Length >= 3 && !w.All(char.IsDigit))
                .Select(w => w.ToLowerInvariant())
                .Distinct()
                .ToArray();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dw in dirWords)
            {
                if (wordToClasses.TryGetValue(dw, out var directMatches))
                {
                    foreach (var baseName in directMatches)
                    {
                        if (!seen.Add(baseName)) continue;
                        var candidate = BuildHeuristicCandidate(dir, baseName, origFile, usedNames);
                        if (candidate != null)
                            return candidate;
                    }
                }
            }

            foreach (var kvp in wordToClasses)
            {
                foreach (var dw in dirWords)
                {
                    if (kvp.Key.IndexOf(dw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        dw.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foreach (var baseName in kvp.Value)
                        {
                            if (!seen.Add(baseName)) continue;
                            var candidate = BuildHeuristicCandidate(dir, baseName, origFile, usedNames);
                            if (candidate != null)
                                return candidate;
                        }
                    }
                }
            }

            return null;
        }

        private static string? BuildHeuristicCandidate(string dir, string baseName, string origFile, HashSet<string> usedNames)
        {
            // Try stripped version first
            var stripped = ProfileUtils.StripColorSuffixes(baseName);
            var candidate = $"{dir}/{stripped}{origFile}";
            if (candidate.All(c => c < 128) && !usedNames.Contains(candidate))
                return candidate;
            // Fall back to original (may contain color suffixes)
            candidate = $"{dir}/{baseName}{origFile}";
            if (candidate.All(c => c < 128) && !usedNames.Contains(candidate))
                return candidate;
            return null;
        }

        private static void WriteProperties(BinaryWriterEx output, List<KeyValuePair<string, string>> props)
        {
            var versionEntry = new FileEntry
            {
                CompressedMagic = BitConverter.ToInt32(System.Text.Encoding.ASCII.GetBytes("sreV"), 0),
                UncompressedSize = 0,
                StartOffset = 0,
                TimeStamp = 0,
                DataSize = 0
            };
            versionEntry.Write(output);

            foreach (var (key, value) in props)
            {
                output.WriteAsciiz(key);
                output.WriteAsciiz(value);
            }
            output.Write((byte)0);
        }

        private static void WriteBasicHeader(BinaryWriterEx output, IEnumerable<FileEntry> entries)
        {
            foreach (var entry in entries)
                entry.Write(output);
            output.Write((byte)0);
            output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(0);
        }

        /// Detects a common mod prefix from class names (e.g., "jsoar_", "uksf_").
        /// Class-name frequency analysis (>70%), with fallback from PBO prefix property.
        internal static string? DetectModPrefix(List<string>? classNames, string? pboPrefix)
        {
            if (classNames == null || classNames.Count == 0) return null;

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
        /// Extracts class names from config.bin for heuristic name generation.
        /// </summary>
        private static List<string> ExtractDeobfuscatorClassNames(ParamClass root)
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
            CollectClassNamesInner(root, results, seen, exclude);
            return results;
        }

        private static void CollectClassNamesInner(ParamClass cls, List<string> names, HashSet<string> seen, HashSet<string> exclude)
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
                    CollectClassNamesInner(nested, names, seen, exclude);
                }
            }
        }

        private static void AddPrefixedPath(string prefix, string orig, string target, Dictionary<string, string> pathMap)
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                var prefixed = prefix.Replace('\\', '/').TrimEnd('/') + "/" + orig;
                if (!pathMap.ContainsKey(prefixed))
                    pathMap[prefixed] = target;
            }
        }

        /// <summary>
        /// Parses config.bin to build a map from normalized model path -> best class name.
        /// Config.bin stores model paths with the PBO prefix (e.g. "jsoar\model\....p3d").
        /// PBO entry raw names store the relative path without the prefix ("model\....p3d").
        /// This method normalises both sides and picks the best class name for each unique model.
        /// </summary>
        private static Dictionary<string, string> BuildConfigModelMap(ParamClass root, string? pboPrefix)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var prefix = (pboPrefix ?? "").Replace('\\', '/').TrimEnd('/');
            CollectModelPaths(root, map, prefix);
            return map;
        }

        private static void CollectModelPaths(ParamClass cls, Dictionary<string, string> result, string pboPrefix)
        {
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass nested)
                {
                    foreach (var e in nested.Entries)
                    {
                        if (e is ParamValue val &&
                            val.Name == "model" &&
                            val.Value?.Value is string sv &&
                            sv.EndsWith(".p3d", StringComparison.OrdinalIgnoreCase))
                        {
                            var rawPath = sv.Replace('\\', '/');
                            // Config paths may include the PBO prefix (e.g. "jsoar/model/....p3d")
                            // or be relative (e.g. "muzzle/muzzlecoef.p3d"). Only strip the prefix
                            // if the path actually starts with it.
                            var relPath = rawPath;
                            if (!string.IsNullOrEmpty(pboPrefix) &&
                                relPath.StartsWith(pboPrefix + "/", StringComparison.OrdinalIgnoreCase))
                            {
                                relPath = relPath.Substring(pboPrefix.Length + 1);
                            }

                            // Pick the shortest class name as the "base" name for this model.
                            // Multiple classes (variants) can share the same model file.
                            if (!result.TryGetValue(relPath, out var existing) ||
                                nested.Name.Length < existing.Length)
                            {
                                result[relPath] = nested.Name;
                            }
                        }
                    }
                    CollectModelPaths(nested, result, pboPrefix);
                }
            }
        }

        /// <summary>
        /// Builds a map from raw Cyrillic-obfuscated image paths to the
        /// parent class (variant) name. Enables semantic naming of icon
        /// files (e.g. acex_001.paa → acex/mc.paa).
        /// </summary>
        private static Dictionary<string, string> BuildConfigImageMap(ParamClass root)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectImagePaths(root, map);
            return map;
        }

        private static void CollectImagePaths(ParamClass cls, Dictionary<string, string> result)
        {
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass nested)
                {
                    // Check if this class has an "image" property with a local .paa path
                    foreach (var e in nested.Entries)
                    {
                        if (e is ParamValue val &&
                            string.Equals(val.Name, "image", StringComparison.OrdinalIgnoreCase) &&
                            val.Value?.Value is string sv &&
                            sv.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
                        {
                            var rawPath = sv.Replace('\\', '/');
                            // Strip prefix (first path component) to match PBO entry raw name
                            var slashIdx = rawPath.IndexOf('/');
                            var relPath = slashIdx >= 0 ? rawPath.Substring(slashIdx + 1) : rawPath;

                            // Use the IMMEDIATE parent class name as the variant name
                            if (!result.ContainsKey(relPath))
                                result[relPath] = nested.Name.ToLowerInvariant();
                        }
                    }
                    CollectImagePaths(nested, result);
                }
            }
        }
    }
}
