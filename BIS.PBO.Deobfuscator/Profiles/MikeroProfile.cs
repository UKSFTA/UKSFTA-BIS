using System;
using System.Linq;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator.Profiles
{
    public class MikeroProfile : IObfuscationProfile
    {
        public string ProfileName => "Mikero DeObo/MakePbo";

        public bool IsMatch(BIS.PBO.PBO pbo)
        {
            // Mikero tools inject a "Mikero" key into the header properties
            return pbo.PropertiesPairs.Any(p => p.Key.Equals("Mikero", StringComparison.OrdinalIgnoreCase));
        }

        public void Deobfuscate(BIS.PBO.PBO pbo)
        {
            // Placeholder for Mikero-specific logic.
            // E.g., using config.bin context to recover stripped base names like "_as.paa".
            Console.WriteLine("  -> Applying Mikero deobfuscation heuristic...");
        }
    }
}
