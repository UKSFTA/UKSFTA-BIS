using BIS.PBO.Deobfuscator;
using System;
using System.Collections.Generic;
using System.IO;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator.Profiles.Specialized
{
    public class SuffixBasedFilenameRecoveryModule : IRecoveryModule
    {
        public string ModuleName => "Suffix-based Filename Recovery";
        public DeobfuscationResult Recover(PBO pbo, DeobfuscationResult result, List<string> knownPaths, string prefix)
        {
            // Build lookup from knownPaths
            var pathLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in knownPaths)
            {
                var dir = ProfileUtils.GetDirectoryName(path);
                var file = ProfileUtils.GetFileName(path);

                var suffixMatch = System.Text.RegularExpressions.Regex.Match(file, @"(_[^_]+\.[a-zA-Z0-9]+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (suffixMatch.Success)
                {
                    var key = $"{dir}|{suffixMatch.Value}".ToLowerInvariant();
                    ProfileUtils.AddToLookup(pathLookup, key, path);
                }

                var extKey = $"{dir}|{Path.GetExtension(file)}".ToLowerInvariant();
                ProfileUtils.AddToLookup(pathLookup, extKey, path);
            }

            int recovered = 0;
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                var file = pbo.Files[i];
                var dir = ProfileUtils.GetDirectoryName(file.FileName);
                var name = ProfileUtils.GetFileName(file.FileName);

                if (!name.StartsWith("_") && !name.StartsWith("."))
                    continue;

                var suffixKey = $"{dir}|{name}".ToLowerInvariant();
                string? matchedPath = ProfileUtils.FindUnusedMatch(pathLookup, suffixKey, usedPaths);

                if (matchedPath == null)
                {
                    var extKey = $"{dir}|{Path.GetExtension(name)}".ToLowerInvariant();
                    matchedPath = ProfileUtils.FindUnusedMatch(pathLookup, extKey, usedPaths);
                }

                if (matchedPath != null)
                {
                    Console.WriteLine($"  -> Recovered: {file.FileName}  =>  {matchedPath}");
                    result.RecoveredNames[i] = matchedPath;
                    usedPaths.Add(matchedPath);
                    recovered++;
                }
            }
            result.Stats["Recovered"] = result.Stats.GetValueOrDefault("Recovered", 0) + recovered;
            return result;
        }
    }
}
