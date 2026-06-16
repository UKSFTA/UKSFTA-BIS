using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BIS.Core.Config;
using BIS.PBO;
using BIS.PBO.Deobfuscator;

namespace BIS.PBO.Deobfuscator.Profiles.Specialized
{
    /// <summary>
    /// Matches orphan .rvmat files against known .rvmat files by analyzing their content.
    ///
    /// Two-phase strategy:
    ///   1. Binary content hash — SHA256 of the file bytes identifies exact duplicates
    ///      of already-recovered RVMATs (same approach as PaaContentMatcher).
    ///   2. Texture-reference inference — parses the RVMAT config to find referenced
    ///      texture paths, then infers the RVMAT name from a known texture's path.
    ///
    /// This recovers filenames when the obfuscator duplicated or renamed RVMATs —
    /// if an orphan .rvmat references a known texture, the RVMAT's name can be
    /// inferred from the texture's directory and name prefix (with color suffixes
    /// stripped).
    /// </summary>
    public static class RvmatContentMatcher
    {
        private static readonly HashSet<string> _colorSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "co", "ca", "nohq", "as", "smdi", "mc", "paa"
        };

        /// <summary>
        /// Matches orphan .rvmat files against recovered .rvmat files by content analysis,
        /// then applies the matches to DeobfuscationResult.RecoveredNames with
        /// collision-safe deduplication.
        ///
        /// Returns a dictionary mapping orphan PBO file index → the unique applied
        /// name (already added to result.RecoveredNames).
        /// </summary>
        public static Dictionary<int, string> ApplyMatches(PBO pbo, DeobfuscationResult result)
        {
            var matches = MatchOrphans(pbo, result);
            if (matches.Count == 0)
                return matches;

            var usedNames = new HashSet<string>(result.RecoveredNames.Values, StringComparer.OrdinalIgnoreCase);

            var applied = new Dictionary<int, string>(matches.Count);
            foreach (var kvp in matches)
            {
                var index = kvp.Key;
                var desiredName = kvp.Value;

                var uniqueName = GetUniqueName(desiredName, usedNames);
                result.RecoveredNames[index] = uniqueName.ToLowerInvariant();
                usedNames.Add(uniqueName);
                applied[index] = uniqueName;
            }

            return applied;
        }

        /// <summary>
        /// Attempts to match orphan .rvmat files against recovered .rvmat files.
        ///
        /// Phase 1: Binary content hash matching. Builds a SHA256 hash lookup from
        /// recovered .rvmat files, then checks orphan .rvmat files against it.
        /// Content-identical files receive the known RVMAT's name.
        ///
        /// Phase 2: Texture-reference inference. For orphans that didn't hash-match,
        /// parses the RVMAT config and checks if any referenced texture exists in
        /// the set of known (recovered) filenames. If so, infers the RVMAT name
        /// from the known texture's directory and name prefix (with color suffixes
        /// stripped).
        ///
        /// Returns a dictionary mapping orphan PBO file index → the matched
        /// or inferred file name (with directory).
        /// </summary>
        public static Dictionary<int, string> MatchOrphans(PBO pbo, DeobfuscationResult result)
        {
            var matches = new Dictionary<int, string>();

            // Phase 1: Build a hash → recovered-name lookup from all recovered .rvmat files.
            // Use the FIRST recovered RVMAT with each hash value.
            var nameByHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                if (result.FilteredOut.Contains(i))
                    continue;
                if (!result.RecoveredNames.TryGetValue(i, out var recoveredName))
                    continue;
                if (!recoveredName.EndsWith(".rvmat", StringComparison.OrdinalIgnoreCase))
                    continue;

                var hash = ComputeContentHash(pbo.Files[i]);
                if (hash != null && !nameByHash.ContainsKey(hash))
                    nameByHash[hash] = recoveredName;
            }

            if (nameByHash.Count > 0)
            {
                for (int i = 0; i < pbo.Files.Count; i++)
                {
                    if (result.FilteredOut.Contains(i))
                        continue;
                    if (result.RecoveredNames.ContainsKey(i))
                        continue;

                    var name = pbo.Files[i].FileName;
                    if (!name.EndsWith(".rvmat", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var hash = ComputeContentHash(pbo.Files[i]);
                    if (hash != null && nameByHash.TryGetValue(hash, out var matchedName))
                        matches[i] = matchedName;
                }
            }

            // Phase 2: Texture-reference inference for orphans that didn't hash-match.
            // Check if any referenced texture path is a known file.
            var knownNames = new HashSet<string>(result.RecoveredNames.Values, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                if (result.FilteredOut.Contains(i))
                    continue;
                if (result.RecoveredNames.ContainsKey(i))
                    continue;
                if (matches.ContainsKey(i))
                    continue;

                var name = pbo.Files[i].FileName;
                if (!name.EndsWith(".rvmat", StringComparison.OrdinalIgnoreCase))
                    continue;

                var inferredName = InferFromTextures(pbo.Files[i], knownNames);
                if (inferredName != null)
                    matches[i] = inferredName;
            }

            return matches;
        }

        /// <summary>
        /// Computes a SHA256 hash of the raw binary content of a file entry.
        /// Returns null if the file cannot be read.
        /// </summary>
        private static string? ComputeContentHash(IPBOFileEntry file)
        {
            try
            {
                using var ms = new MemoryStream();
                file.OpenRead().CopyTo(ms);
                var bytes = ms.ToArray();
                var hashBytes = SHA256.HashData(bytes);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parses an RVMAT file and checks whether any referenced texture paths
        /// are known (recovered) files. If a known texture reference is found,
        /// infers the RVMAT name from the texture's directory and name prefix
        /// (with color suffixes stripped).
        ///
        /// Returns the inferred RVMAT name (with directory), or null if no
        /// known texture reference is found.
        /// </summary>
        private static string? InferFromTextures(IPBOFileEntry file, HashSet<string> knownNames)
        {
            try
            {
                using var stream = file.OpenRead();
                var config = new ParamFile(stream);
                var paths = ProfileUtils.ExtractPathsFromRap(config.Root);

                foreach (var path in paths)
                {
                    var normalized = path.Replace('\\', '/');
                    if (!knownNames.Contains(normalized))
                        continue;

                    // Found a known texture reference. Derive the RVMAT name from it.
                    var dir = ProfileUtils.GetDirectoryName(normalized);
                    var texName = Path.GetFileNameWithoutExtension(normalized);
                    var stripped = StripColorSuffix(texName);

                    return string.IsNullOrEmpty(dir)
                        ? $"{stripped}.rvmat"
                        : $"{dir}/{stripped}.rvmat";
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Strips known PAA color/map suffixes from the end of a texture filename
        /// (without extension). E.g. "adams_avs_belt_ca" → "adams_avs_belt",
        /// "tex_nohq" → "tex", "tex_smdi" → "tex".
        /// Returns the original name if no known suffix is found.
        /// </summary>
        private static string StripColorSuffix(string textureNameWithoutExt)
        {
            foreach (var suffix in _colorSuffixes)
            {
                var suffixWithUnderscore = "_" + suffix;
                if (textureNameWithoutExt.EndsWith(suffixWithUnderscore, StringComparison.OrdinalIgnoreCase))
                    return textureNameWithoutExt.Substring(0, textureNameWithoutExt.Length - suffixWithUnderscore.Length);
            }
            return textureNameWithoutExt;
        }

        /// <summary>
        /// Generates a collision-safe name from a desired name. If the desired name
        /// is already in usedNames, appends incrementing suffixes (_2, _3, …) before
        /// the extension. Falls back to a GUID-based suffix after 100 attempts.
        /// </summary>
        private static string GetUniqueName(string desiredName, HashSet<string> usedNames)
        {
            if (!usedNames.Contains(desiredName))
                return desiredName;

            var dir = Path.GetDirectoryName(desiredName) ?? "";
            var nameWithoutExt = Path.GetFileNameWithoutExtension(desiredName);
            var ext = Path.GetExtension(desiredName);
            dir = dir.Replace('\\', '/');

            for (int suffix = 2; suffix < 100; suffix++)
            {
                var candidate = string.IsNullOrEmpty(dir)
                    ? $"{nameWithoutExt}_{suffix}{ext}"
                    : $"{dir}/{nameWithoutExt}_{suffix}{ext}";
                if (!usedNames.Contains(candidate))
                    return candidate;
            }

            return string.IsNullOrEmpty(dir)
                ? $"{nameWithoutExt}_{Guid.NewGuid().ToString("N")[..8]}{ext}"
                : $"{dir}/{nameWithoutExt}_{Guid.NewGuid().ToString("N")[..8]}{ext}";
        }
    }
}
