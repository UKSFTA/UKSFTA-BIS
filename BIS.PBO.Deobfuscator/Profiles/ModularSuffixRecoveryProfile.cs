using System.Collections.Generic;
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
                new CyrillicDetectionModule(),
                new DecoyDetectionModule()
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
            var prefix = ProfileUtils.NormalizePath(pbo.Prefix ?? "");

            foreach (var module in _recoveryModules)
            {
                result = module.Recover(pbo, result, knownPaths, prefix);
            }
            return result;
        }
    }
}
