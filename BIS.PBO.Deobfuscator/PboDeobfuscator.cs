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
            int zeroBytesSkipped = 0;

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
                        var ext = DetectExtension(original);
                        var dirPrefix = dir.Split('/').LastOrDefault() ?? "file";
                        if (dirPrefix == "_unknown")
                            dirPrefix = "file";
                        var baseName = $"{dirPrefix}_{counter:D3}";
                        finalName = $"{dir}/{baseName}{ext}";
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
