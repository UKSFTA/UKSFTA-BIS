using BIS.PBO.Deobfuscator;
using System;
using System.IO;
using System.Text.RegularExpressions;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator.Profiles.Specialized
{
    public class DecoyFilteringModule : IRecoveryModule
    {
        public string ModuleName => "Decoy Filtering";
        private static readonly Regex RandomNamePattern = new Regex(
            @"^[A-Za-z0-9]{2,12}$",
            RegexOptions.Compiled
        );

        public DeobfuscationResult Recover(PBO pbo, DeobfuscationResult result, List<string> knownPaths, string prefix)
        {
            Console.WriteLine("  -> Scanning files for decoy injection markers...");
            int decoys = 0;
            int stubs = 0;

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                var file = pbo.Files[i];
                string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                string nameOnly = Path.GetFileNameWithoutExtension(file.FileName);

                if (file.Size == 0)
                {
                    result.FilteredOut.Add(i);
                    decoys++;
                    continue;
                }

                if (file.Size < 20 && RandomNamePattern.IsMatch(nameOnly))
                {
                    result.FilteredOut.Add(i);
                    stubs++;
                    continue;
                }

                // We don't filter entry points here, just count them if needed. 
                // However, the original code had them counted.
                // Keeping original logic for consistency.
            }

            result.Stats["Decoys"] = decoys;
            result.Stats["Stubs"] = stubs;
            return result;
        }
    }
}
