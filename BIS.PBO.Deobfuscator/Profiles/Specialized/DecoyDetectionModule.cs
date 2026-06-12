using BIS.PBO.Deobfuscator;
using System.Linq;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator.Profiles.Specialized
{
    public class DecoyDetectionModule : IDetectionModule
    {
        public string ModuleName => "Decoy Detection";
        public bool IsMatch(PBO pbo)
        {
            int longProps = pbo.PropertiesPairs.Count(p =>
                p.Key.Length > 40 || p.Value.Length > 40);
            int zeroByteFiles = pbo.Files.Count(f => f.Size == 0);
            return longProps >= 2 && zeroByteFiles >= 1;
        }
    }
}
