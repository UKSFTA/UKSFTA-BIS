using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BIS.PAA;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator.Profiles.Specialized
{
    /// <summary>
    /// Matches orphan .paa files (not recovered by deobfuscation profiling)
    /// against known/recovered .paa files by comparing decoded pixel content.
    ///
    /// The algorithm:
    ///   1. Decode each known (recovered) .paa to ARGB32 pixel data
    ///   2. Compute a SHA256 hash of the decoded pixel data
    ///   3. For each orphan .paa, decode and hash the same way
    ///   4. If a hash matches, the orphan is pixel-identical to a known texture
    ///
    /// This recovers filenames when the obfuscator duplicated textures under
    /// random names — the content is the same even though the name is different.
    /// </summary>
    public static class PaaContentMatcher
    {
        /// <summary>
        /// Matches orphan .paa files against recovered .paa files by pixel content,
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
        /// Attempts to match orphan .paa files against recovered .paa files by
        /// comparing decoded ARGB32 pixel content via SHA256 hash.
        ///
        /// Returns a dictionary mapping orphan PBO file index → the recovered
        /// file name (with directory) of the matched known texture.
        /// </summary>
        public static Dictionary<int, string> MatchOrphans(PBO pbo, DeobfuscationResult result)
        {
            var matches = new Dictionary<int, string>();

            // Phase 1: Build a hash → recovered-name lookup from all recovered .paa files.
            // Use the FIRST recovered texture with each hash value (identical files
            // would produce the same name in practice).
            var nameByHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                if (result.FilteredOut.Contains(i))
                    continue;
                if (!result.RecoveredNames.TryGetValue(i, out var recoveredName))
                    continue;
                if (!recoveredName.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
                    continue;

                var hash = ComputePixelHash(pbo.Files[i]);
                if (hash != null && !nameByHash.ContainsKey(hash))
                    nameByHash[hash] = recoveredName;
            }

            if (nameByHash.Count == 0)
                return matches;

            // Phase 2: Check each orphan .paa (not recovered, not filtered) against
            // the known hash lookup.
            for (int i = 0; i < pbo.Files.Count; i++)
            {
                if (result.FilteredOut.Contains(i))
                    continue;
                if (result.RecoveredNames.ContainsKey(i))
                    continue;

                var name = pbo.Files[i].FileName;
                if (!name.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
                    continue;

                var hash = ComputePixelHash(pbo.Files[i]);
                if (hash != null && nameByHash.TryGetValue(hash, out var matchedName))
                    matches[i] = matchedName;
            }

            return matches;
        }

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

        private static string? ComputePixelHash(IPBOFileEntry file)
        {
            try
            {
                using var stream = file.OpenRead();
                var pixelData = BIS.PAA.PAA.GetARGB32PixelData(stream);
                if (pixelData == null || pixelData.Length == 0)
                    return null;

                var hashBytes = SHA256.HashData(pixelData);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }

        private static (byte[] Pixels, int Width, int Height)? DecodePaaWithDimensions(IPBOFileEntry file)
        {
            try
            {
                using var stream = file.OpenRead();
                var paa = new BIS.PAA.PAA(stream, false);
                var pixels = BIS.PAA.PAA.GetARGB32PixelData(paa, stream, 0);
                if (pixels == null || pixels.Length == 0)
                    return null;
                return (pixels, paa.Width, paa.Height);
            }
            catch
            {
                return null;
            }
        }

        public static float[] ComputeBlockSignature(byte[] argbPixels, int width, int height, int gridSize = 8)
        {
            var signature = new float[gridSize * gridSize];
            var blockW = (float)width / gridSize;
            var blockH = (float)height / gridSize;

            for (int blockR = 0; blockR < gridSize; blockR++)
            {
                var yStart = (int)(blockR * blockH);
                var yEnd = (blockR == gridSize - 1) ? height : (int)((blockR + 1) * blockH);

                for (int blockC = 0; blockC < gridSize; blockC++)
                {
                    var xStart = (int)(blockC * blockW);
                    var xEnd = (blockC == gridSize - 1) ? width : (int)((blockC + 1) * blockW);

                    float sum = 0;
                    int count = 0;
                    for (int y = yStart; y < yEnd; y++)
                    {
                        for (int x = xStart; x < xEnd; x++)
                        {
                            var offset = (y * width + x) * 4;
                            byte b = argbPixels[offset];
                            byte g = argbPixels[offset + 1];
                            byte r = argbPixels[offset + 2];
                            // ARGB32 little-endian: [0]=B, [1]=G, [2]=R, [3]=A
                            sum += 0.299f * r + 0.587f * g + 0.114f * b;
                            count++;
                        }
                    }
                    signature[blockR * gridSize + blockC] = count > 0 ? sum / count : 0;
                }
            }

            return signature;
        }

        public static double CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                return 0;

            double dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += (double)a[i] * b[i];
                normA += (double)a[i] * a[i];
                normB += (double)b[i] * b[i];
            }

            if (normA == 0 || normB == 0)
                return 0;

            return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        public static Dictionary<int, string> MatchFuzzyOrphans(PBO pbo, DeobfuscationResult result, int gridSize = 8, double threshold = 0.85)
        {
            var matches = new Dictionary<int, string>();

            // Phase 1: Build block signatures from recovered .paa files
            var knownSignatures = new List<(float[] Signature, string Name)>();
            for (int i = 0; i < pbo.Files.Count; i++)
            {
                if (result.FilteredOut.Contains(i))
                    continue;
                if (!result.RecoveredNames.TryGetValue(i, out var recoveredName))
                    continue;
                if (!recoveredName.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
                    continue;

                var decoded = DecodePaaWithDimensions(pbo.Files[i]);
                if (decoded == null)
                    continue;

                var sig = ComputeBlockSignature(decoded.Value.Pixels, decoded.Value.Width, decoded.Value.Height, gridSize);
                knownSignatures.Add((sig, recoveredName));
            }

            if (knownSignatures.Count == 0)
                return matches;

            // Phase 2: Check each orphan .paa against known signatures
            for (int i = 0; i < pbo.Files.Count; i++)
            {
                if (result.FilteredOut.Contains(i))
                    continue;
                if (result.RecoveredNames.ContainsKey(i))
                    continue;

                var name = pbo.Files[i].FileName;
                if (!name.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
                    continue;

                var decoded = DecodePaaWithDimensions(pbo.Files[i]);
                if (decoded == null)
                    continue;

                var orphanSig = ComputeBlockSignature(decoded.Value.Pixels, decoded.Value.Width, decoded.Value.Height, gridSize);

                double bestScore = 0;
                string? bestName = null;
                foreach (var (sig, knownName) in knownSignatures)
                {
                    var score = CosineSimilarity(orphanSig, sig);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestName = knownName;
                    }
                }

                if (bestScore >= threshold && bestName != null)
                    matches[i] = bestName;
            }

            return matches;
        }

        public static Dictionary<int, string> ApplyFuzzyMatches(PBO pbo, DeobfuscationResult result, int gridSize = 8, double threshold = 0.85)
        {
            var matches = MatchFuzzyOrphans(pbo, result, gridSize, threshold);
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
    }
}
