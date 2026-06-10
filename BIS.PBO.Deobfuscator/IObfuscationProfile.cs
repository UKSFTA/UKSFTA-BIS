using System;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator
{
    public interface IObfuscationProfile
    {
        string ProfileName { get; }
        
        /// <summary>
        /// Checks if the given PBO matches this obfuscator's known signatures.
        /// </summary>
        bool IsMatch(PBO pbo);

        /// <summary>
        /// Attempts to reverse the obfuscation applied to the given PBO.
        /// </summary>
        void Deobfuscate(PBO pbo);
    }
}
