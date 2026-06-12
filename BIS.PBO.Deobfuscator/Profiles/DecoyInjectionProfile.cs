using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BIS.P3D;
using BIS.PBO;
using BIS.PBO.Deobfuscator.Profiles.Specialized;

namespace BIS.PBO.Deobfuscator.Profiles
{
    public class DecoyInjectionProfile : IObfuscationProfile
    {
        private static readonly string[] RealExtensions = new[]
        {
            ".paa", ".p3d", ".rvmat", ".dll", ".so"
        };

        private static readonly string[] KnownRootFiles = new[]
        {
            "config.cpp", "description.ext", "mission.sqm"
        };

        private static readonly Regex RandomNamePattern = new Regex(
            @"^[A-Za-z0-9]{2,12}$",
            RegexOptions.Compiled
        );

        public string ProfileName => "Decoy Injection";

        public bool IsMatch(BIS.PBO.PBO pbo)
        {
            int longProps = pbo.PropertiesPairs.Count(p =>
                p.Key.Length > 40 || p.Value.Length > 40);

            int zeroByteFiles = pbo.Files.Count(f => f.Size == 0);

            return longProps >= 2 && zeroByteFiles >= 1;
        }

        public DeobfuscationResult Deobfuscate(BIS.PBO.PBO pbo)
        {
            var result = new DeobfuscationResult { MatchedProfile = ProfileName };

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
                    result.RecoveredNames[i] = $"_decoy/{file.FileName}";
                    result.FilteredOut.Add(i);
                    decoys++;
                    continue;
                }

                // Very small files (< 20 bytes) of any type are likely obfuscator glue stubs
                if (file.Size < 20)
                {
                    result.RecoveredNames[i] = $"_stub/{file.FileName}";
                    result.FilteredOut.Add(i);
                    stubs++;
                    continue;
                }

                // Small root-level files with random-looking names are likely decoys
                if (!hasDirectory && file.Size < 1000 && !KnownRootFiles.Contains(file.FileName, StringComparer.OrdinalIgnoreCase))
                {
                    if (ext.Equals(".paa", StringComparison.OrdinalIgnoreCase))
                    {
                        // .paa files must always be in a data directory, root .paa is suspicious
                        result.RecoveredNames[i] = $"_stub/{file.FileName}";
                        result.FilteredOut.Add(i);
                        stubs++;
                        continue;
                    }

                    // Check for random-looking filenames (short alphanumeric strings)
                    if (nameOnly.Length <= 14 && RandomNamePattern.IsMatch(nameOnly))
                    {
                        // Exception: real P3D model files are often in the root
                        if (ext.Equals(".p3d", StringComparison.OrdinalIgnoreCase) && file.Size > 100000)
                            goto scanContent;

                        result.RecoveredNames[i] = $"_stub/{file.FileName}";
                        result.FilteredOut.Add(i);
                        stubs++;
                        continue;
                    }
                }

                // Check for _unknown files that are suspiciously small
                if (file.FileName.StartsWith("_unknown\\", StringComparison.OrdinalIgnoreCase) && file.Size < 200)
                {
                    result.RecoveredNames[i] = $"_stub/{file.FileName}";
                    result.FilteredOut.Add(i);
                    stubs++;
                    continue;
                }

                scanContent:
                // Scan small files for decoy content patterns
                if (file.Size < 65536)
                {
                    try
                    {
                        byte[] data;
                        using (var ms = file.OpenRead())
                        using (var br = new BinaryReader(ms))
                            data = br.ReadBytes((int)Math.Min(ms.Length, 4096));

                        string text = Encoding.ASCII.GetString(data);

                        // Check for #include with control characters
                        if (Regex.IsMatch(text, @"#include\s*""[\x00-\x1F]+""", RegexOptions.IgnoreCase))
                        {
                            result.RecoveredNames[i] = $"_stub/{file.FileName}";
                            result.FilteredOut.Add(i);
                            stubs++;
                            continue;
                        }

                        // Check for SQF entry point patterns
                        if (file.FileName.EndsWith(".sqf", StringComparison.OrdinalIgnoreCase))
                        {
                            if (Regex.IsMatch(text, @"execVM|call\s+compile|preprocessFile", RegexOptions.IgnoreCase))
                            {
                                result.RecoveredNames[i] = $"_entry/{file.FileName}";
                                entryPoints++;
                            }
                        }
                    }
                    catch { /* Skip unreadable files */ }
                }
            }

            int p3dScanned = 0;
            int p3dPaths = 0;
            var knownPaths = new List<string>();

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                var file = pbo.Files[i];
                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".p3d", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var stream = file.OpenRead();
                    var p3d = new BIS.P3D.P3D(stream);
                    p3dScanned++;

                    foreach (var lod in p3d.LODs)
                    {
                        foreach (var tex in lod.GetTextures())
                        {
                            var norm = ProfileUtils.NormalizePath(tex);
                            if (!string.IsNullOrEmpty(norm) &&
                                ProfileUtils.IsValidPathString(norm) &&
                                !knownPaths.Contains(norm))
                            {
                                knownPaths.Add(norm);
                                p3dPaths++;
                            }
                        }
                        foreach (var mat in lod.GetMaterials())
                        {
                            var norm = ProfileUtils.NormalizePath(mat);
                            if (!string.IsNullOrEmpty(norm) &&
                                ProfileUtils.IsValidPathString(norm) &&
                                !knownPaths.Contains(norm))
                            {
                                knownPaths.Add(norm);
                                p3dPaths++;
                            }
                        }
                    }
                }
                catch { }
            }

            if (p3dScanned > 0)
                Console.WriteLine($"  -> Scanned {p3dScanned} P3D files, extracted {p3dPaths} unique paths.");

            result.Stats["Decoys"] = decoys;
            result.Stats["Stubs"] = stubs;
            result.Stats["EntryPoints"] = entryPoints;
            result.Stats["Genuine"] = pbo.Files.Count - decoys - stubs - entryPoints;
            result.Stats["Total"] = pbo.Files.Count;

            Console.WriteLine($"  -> Decoy injection analysis complete.");
            Console.WriteLine($"     Decoy entries (0 bytes):  {decoys}");
            Console.WriteLine($"     Stub scripts (< 20 bytes): {stubs}");
            Console.WriteLine($"     Entry point scripts:      {entryPoints}");
            Console.WriteLine($"     Genuine asset files:      {result.Stats["Genuine"]}");
            Console.WriteLine("  -> Note: original filenames cannot be recovered (destroyed at obfuscation time).");

            return result;
        }
    }
}
