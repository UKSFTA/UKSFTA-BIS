using System;
using System.Collections.Generic;
using System.IO;
using BIS.PBO;
using BIS.PBO.Deobfuscator;
using BIS.PBO.Deobfuscator.Profiles.Specialized;

namespace BIS.PBO.Deobfuscator.Profiles
{
    public class ModularSuffixRecoveryProfile : IObfuscationProfile
    {
        public string ProfileName => "Modular Suffix Recovery";
        private readonly List<IDetectionModule> _detectionModules;
        private readonly List<IRecoveryModule> _recoveryModules;

        public ModularSuffixRecoveryProfile()
        {
            _detectionModules = new List<IDetectionModule>
            {
                new CyrillicDetectionModule()
            };
            _recoveryModules = new List<IRecoveryModule>
            {
                new DecoyFilteringModule(),
                new P3DPathRecoveryModule(),
                new SuffixBasedFilenameRecoveryModule(),
                new CyrillicRecoveryModule()
            };
        }

        public bool IsMatch(PBO pbo)
        {
            foreach (var module in _detectionModules)
            {
                if (module.IsMatch(pbo)) return true;
            }
            return false;
        }

        public DeobfuscationResult Deobfuscate(PBO pbo)
        {
            var result = new DeobfuscationResult { MatchedProfile = ProfileName };
            var config = pbo.GetRootConfig();
            var knownPaths = config != null ? ProfileUtils.ExtractPathsFromRap(config.Root) : new List<string>();

            // Scan supplementary .bin files for text-format config data (CfgFunctions split files, etc.)
            int extraBinPaths = 0;
            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var file = pbo.Files[fi];
                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(file.FileName, "config.bin", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(file.FileName, "texHeaders.bin", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var binConfig = ProfileUtils.TryParseBinEntry(file);
                    if (binConfig == null) continue;

                    var extracted = ProfileUtils.ExtractPathsFromRap(binConfig.Root);
                    foreach (var path in extracted)
                    {
                        var norm = ProfileUtils.NormalizePath(path);
                        if (!string.IsNullOrEmpty(norm) && ProfileUtils.IsValidPathString(norm) && !knownPaths.Contains(norm))
                        {
                            knownPaths.Add(norm);
                            extraBinPaths++;
                        }
                    }
                }
                catch
                {
                }
            }
            if (extraBinPaths > 0)
                Console.WriteLine($"  -> Scanned supplementary .bin files: {extraBinPaths} new paths.");

            var prefix = ProfileUtils.NormalizePath(pbo.Prefix ?? "");

            foreach (var module in _recoveryModules)
            {
                result = module.Recover(pbo, result, knownPaths, prefix);
            }
            return result;
        }
    }
}
