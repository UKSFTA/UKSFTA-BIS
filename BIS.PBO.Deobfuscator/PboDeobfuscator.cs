using System;
using System.Collections.Generic;
using BIS.PBO;
using BIS.PBO.Deobfuscator.Profiles;

namespace BIS.PBO.Deobfuscator
{
    public class PboDeobfuscator
    {
        private readonly List<IObfuscationProfile> _profiles;

        public PboDeobfuscator()
        {
            _profiles = new List<IObfuscationProfile>
            {
                new MaverickProfile(),
                new MikeroProfile()
            };
        }

        public void RegisterProfile(IObfuscationProfile profile)
        {
            _profiles.Add(profile);
        }

        public void Process(PBO pbo)
        {
            foreach (var profile in _profiles)
            {
                if (profile.IsMatch(pbo))
                {
                    Console.WriteLine($"[Deobfuscator] Matched profile: {profile.ProfileName}");
                    profile.Deobfuscate(pbo);
                    return; // Stop after first match
                }
            }

            Console.WriteLine("[Deobfuscator] No known obfuscation signatures found. PBO appears clean or uses an unknown method.");
        }
    }
}
