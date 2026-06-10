using System;
using System.Collections.Generic;

namespace BIS.PBO.Deobfuscator
{
    /// <summary>
    /// Contains the results of a PBO structural analysis.
    /// Maps original filenames to their recovered or categorised names.
    /// The source PBO is not modified.
    /// </summary>
    public class DeobfuscationResult
    {
        /// <summary>
        /// Name(s) of the profile(s) that matched the PBO's structural pattern.
        /// Null if no profile matched. Multiple names joined with " + " when
        /// several profiles matched.
        /// </summary>
        public string? MatchedProfile { get; set; }

        /// <summary>
        /// Maps the index of each file in PBO.Files to its recovered/categorised name.
        /// Only entries that were modified are included. If a file index is absent,
        /// its original name was already clean.
        /// </summary>
        public Dictionary<int, string> RecoveredNames { get; } = new Dictionary<int, string>();

        /// <summary>
        /// Indices of entries that should be excluded from a rebuilt PBO
        /// (decoy files, stub scripts, padding, etc.).
        /// </summary>
        public HashSet<int> FilteredOut { get; } = new HashSet<int>();

        /// <summary>
        /// Summary statistics from the analysis.
        /// </summary>
        public Dictionary<string, int> Stats { get; } = new Dictionary<string, int>();

        /// <summary>
        /// Gets the effective filename for a given file index. Returns the recovered
        /// name if one exists, otherwise the original name from the PBO.
        /// </summary>
        public string GetEffectiveName(int fileIndex, string originalName)
        {
            return RecoveredNames.TryGetValue(fileIndex, out var recovered) ? recovered : originalName;
        }

        /// <summary>
        /// Convenience: true if any obfuscation profile matched.
        /// </summary>
        public bool IsObfuscated => MatchedProfile != null;
    }
}
