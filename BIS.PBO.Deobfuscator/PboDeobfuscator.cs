using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BIS.Core.Config;
using BIS.Core.Streams;
using BIS.PBO;
using BIS.PBO.Deobfuscator.Profiles;

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

        public PboDeobfuscator()
        {
            _profiles = new List<IObfuscationProfile>
            {
                new DecoyInjectionProfile(),
                new SuffixRecoveryProfile()
            };
        }

        /// <summary>
        /// Register a custom profile for PBO structural analysis.
        /// </summary>
        public void RegisterProfile(IObfuscationProfile profile)
        {
            _profiles.Add(profile);
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
        public static void Rebuild(PBO pbo, DeobfuscationResult result, string outputPath)
        {
            // Build dir-word → class list from config.bin
            var wordToClasses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var classNames = ExtractDeobfuscatorClassNames(pbo);
            foreach (var cls in classNames)
            {
                var clsLower = cls.ToLowerInvariant();
                var words = clsLower.Split('_').Where(w => w.Length >= 3).Distinct();
                foreach (var w in words)
                {
                    if (!wordToClasses.ContainsKey(w))
                        wordToClasses[w] = new List<string>();
                    wordToClasses[w].Add(clsLower);
                }
            }

            var keep = new List<(int Index, FileEntry Entry)>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                if (result.FilteredOut.Contains(i))
                    continue;

                var original = pbo.Files[i];
                var origNorm = original.FileName.Replace('\\', '/');
                var slashIdx = origNorm.LastIndexOf('/');
                var dir = slashIdx >= 0 ? origNorm.Substring(0, slashIdx) : "";
                var origFile = slashIdx >= 0 ? origNorm.Substring(slashIdx + 1) : origNorm;

                string finalName;

                // If the original entry was NOT stripped, keep as-is
                if (!origFile.StartsWith("_") && !origFile.StartsWith("."))
                {
                    finalName = origNorm;
                }
                else
                {
                    // Try heuristic first (uses recovered context via wordToClasses)
                    var heur = GenerateHeuristicName(dir, origFile, wordToClasses, usedNames);
                    if (heur != null)
                    {
                        finalName = heur;
                    }
                    else
                    {
                        // Forced fallback: numbered placeholder — NEVER produce _name or Cyrillic
                        dirCounters.TryGetValue(dir, out var counter);
                        counter++;
                        dirCounters[dir] = counter;
                        var ext = Path.GetExtension(origFile);
                        finalName = $"{dir}/file{counter:D3}{ext}";
                    }
                }

                usedNames.Add(finalName);
                keep.Add((i, new FileEntry
                {
                    FileName = finalName,
                    TimeStamp = original.TimeStamp,
                    DataSize = original.Size,
                    UncompressedSize = 0,
                    CompressedMagic = 0
                }));
            }

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
                    WriteProperties(output, pbo.PropertiesPairs);
                    WriteBasicHeader(output, keep.Select(k => k.Entry));
                }
                foreach (var (idx, _) in keep)
                {
                    using (var source = pbo.Files[idx].OpenRead())
                    {
                        source.CopyTo(target);
                    }
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
            var removed = result.FilteredOut.Count;
            Console.WriteLine($"  -> Rebuilt: {outputPath} ({kept} files kept, {removed} removed)");
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
                        var candidate = $"{dir}/{baseName}{origFile}";
                        if (candidate.All(c => c < 128) && !usedNames.Contains(candidate))
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
                            var candidate = $"{dir}/{baseName}{origFile}";
                            if (candidate.All(c => c < 128) && !usedNames.Contains(candidate))
                                return candidate;
                        }
                    }
                }
            }

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

        /// <summary>
        /// Extracts class names from config.bin for heuristic name generation.
        /// </summary>
        private static List<string> ExtractDeobfuscatorClassNames(PBO pbo)
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
            var config = pbo.GetRootConfig();
            if (config?.Root == null)
                return results;
            CollectClassNamesInner(config.Root, results, seen, exclude);
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
    }
}
