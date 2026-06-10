using System;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator
{
    /// <summary>
    /// Defines a strategy for detecting a structural PBO pattern and
    /// recovering or categorising its file entries. Implementations must
    /// not mutate the PBO; they return results via DeobfuscationResult.
    /// </summary>
    public interface IObfuscationProfile
    {
        /// <summary>
        /// Human-readable name of the structural pattern this profile detects.
        /// </summary>
        string ProfileName { get; }

        /// <summary>
        /// Checks if the given PBO matches this profile's structural pattern.
        /// </summary>
        bool IsMatch(PBO pbo);

        /// <summary>
        /// Analyses the PBO and attempts to recover original filenames or categorise entries.
        /// Returns a DeobfuscationResult containing a mapping of file indices to recovered names.
        /// </summary>
        DeobfuscationResult Deobfuscate(PBO pbo);
    }
}
