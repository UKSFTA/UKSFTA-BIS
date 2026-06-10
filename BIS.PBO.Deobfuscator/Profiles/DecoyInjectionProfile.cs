using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator.Profiles
{
    /// <summary>
    /// Handles PBOs that exhibit a decoy-entry structural pattern.
    ///
    /// These PBOs contain injected fake 0-byte file entries and small stub scripts
    /// scattered among genuine assets. Header properties often contain unusually
    /// long random-looking key/value pairs that serve as signatures.
    /// Original filenames are typically replaced with random garbage.
    ///
    /// Detection uses structural heuristics rather than tool-specific strings:
    ///   - Properties with abnormally long keys or values (> 40 chars)
    ///   - Presence of multiple zero-byte file entries
    ///
    /// Recovery strategy:
    ///   1. Mark 0-byte entries as decoys.
    ///   2. Mark very small script/bin files as stubs.
    ///   3. Scan SQF files for entry-point patterns (execVM, call compile).
    ///   4. Categorise genuine files by their data (textures, models, configs).
    /// </summary>
    public class DecoyInjectionProfile : IObfuscationProfile
    {
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

                // Zero-byte files are known decoy entries
                if (file.Size == 0)
                {
                    result.RecoveredNames[i] = $"_decoy/{file.FileName}";
                    result.FilteredOut.Add(i);
                    decoys++;
                    continue;
                }

                // Very small files (< 20 bytes) are likely obfuscator glue stubs
                if (file.Size < 20 &&
                    (file.FileName.EndsWith(".sqf", StringComparison.OrdinalIgnoreCase) ||
                     file.FileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
                {
                    result.RecoveredNames[i] = $"_stub/{file.FileName}";
                    result.FilteredOut.Add(i);
                    stubs++;
                    continue;
                }

                // Scan SQF files for entry point patterns
                if (file.FileName.EndsWith(".sqf", StringComparison.OrdinalIgnoreCase) && file.Size < 65536)
                {
                    try
                    {
                        byte[] data;
                        using (var ms = file.OpenRead())
                        using (var br = new BinaryReader(ms))
                            data = br.ReadBytes((int)ms.Length);

                        string text = Encoding.ASCII.GetString(data);
                        if (Regex.IsMatch(text, @"execVM|call\s+compile|preprocessFile", RegexOptions.IgnoreCase))
                        {
                            result.RecoveredNames[i] = $"_entry/{file.FileName}";
                            entryPoints++;
                        }
                    }
                    catch { /* Skip unreadable files */ }
                }
            }

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
