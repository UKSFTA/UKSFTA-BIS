using BIS.PBO.Deobfuscator;
using BIS.PBO;
using System.Collections.Generic;

namespace BIS.PBO.Deobfuscator.Profiles.Specialized
{
    public class CyrillicRecoveryModule : IRecoveryModule
    {
        public string ModuleName => "Cyrillic Recovery";

        public DeobfuscationResult Recover(PBO pbo, DeobfuscationResult result, List<string> knownPaths, string prefix)
        {
            int recovered = 0;

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                var name = pbo.Files[i].FileName;
                if (!HasHighByteChars(name))
                    continue;

                var recoveredName = TranscodeLatin1ToWindows1251(name);
                if (recoveredName != name)
                {
                    result.RecoveredNames[i] = recoveredName;
                    recovered++;
                }
            }

            result.Stats["CyrillicRecovered"] = result.Stats.GetValueOrDefault("CyrillicRecovered", 0) + recovered;
            return result;
        }

        private static bool HasHighByteChars(string name)
        {
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] >= 0x80)
                    return true;
            }
            return false;
        }

        private static string TranscodeLatin1ToWindows1251(string mojibake)
        {
            var chars = new char[mojibake.Length];
            for (int i = 0; i < mojibake.Length; i++)
            {
                var b = (byte)mojibake[i];
                if (b < 0x80)
                    chars[i] = (char)b;
                else if (b >= 0xC0 && b <= 0xDF)
                    chars[i] = (char)(0x0410 + (b - 0xC0));
                else if (b >= 0xE0)
                    chars[i] = (char)(0x0430 + (b - 0xE0));
                else
                    chars[i] = (char)b;
            }
            return new string(chars);
        }
    }
}
