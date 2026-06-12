using System;
using System.IO;
using System.Linq;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator.Profiles
{
    /// <summary>
    /// Detects obfuscated PBOs based on structural anomalies rather than explicit markers.
    /// This acts as a fallback when SuffixRecoveryProfile and others fail.
    /// </summary>
    public class HeuristicFallbackProfile : IObfuscationProfile
    {
        public string ProfileName => "HeuristicFallback";

        public bool IsMatch(PBO pbo)
        {
            int longProps = pbo.PropertiesPairs.Count(p =>
                p.Key.Length > 40 || p.Value.Length > 40);
            int zeroByteFiles = pbo.Files.Count(f => f.Size == 0);
            if (longProps >= 2 && zeroByteFiles >= 1)
                return false;

            bool hasSuffiXPattern = pbo.Files.Any(f =>
            {
                var name = Path.GetFileName(f.FileName);
                return name.StartsWith(".") ||
                       (name.StartsWith("_") && !name.StartsWith("_unknown_")) ||
                       name.Any(c => c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я');
            });
            if (hasSuffiXPattern)
                return false;

            // Heuristic 1: High ratio of small files to large files
            int smallFiles = pbo.Files.Count(f => f.Size < 512);
            int totalFiles = pbo.Files.Count;
            if (totalFiles > 10 && (double)smallFiles / totalFiles > 0.6) return true;

            // Heuristic 2: Unusual file name patterns (high number of files starting with non-standard chars)
            int unusualNames = pbo.Files.Count(f => {
                var name = System.IO.Path.GetFileName(f.FileName);
                return name.StartsWith("_") || name.StartsWith(".");
            });
            if (totalFiles > 5 && (double)unusualNames / totalFiles > 0.4) return true;

            return false;
        }

        public DeobfuscationResult Deobfuscate(PBO pbo)
        {
            // For now, reuse SuffixRecoveryProfile logic but signal low-confidence
            var profile = new ModularSuffixRecoveryProfile();
            return profile.Deobfuscate(pbo);
        }
    }
}
