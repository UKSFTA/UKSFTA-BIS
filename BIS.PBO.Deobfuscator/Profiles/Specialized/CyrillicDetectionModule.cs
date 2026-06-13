using System.Linq;
using BIS.PBO;
using BIS.PBO.Deobfuscator;

namespace BIS.PBO.Deobfuscator.Profiles.Specialized
{
    public class CyrillicDetectionModule : IDetectionModule
    {
        public string ModuleName => "Cyrillic Detection";
        public bool IsMatch(PBO pbo)
        {
            // Check for proper Cyrillic chars (would appear if PBO was read with UTF-8 reader)
            // or Latin-1 supplement chars (0x80-0xFF) which indicate Cyrillic mojibake from
            // byte->char cast via ReadAsciiz on Windows-1251 encoded filenames.
            return pbo.Files.Any(f =>
            {
                var name = ProfileUtils.GetFileName(f.FileName);
                for (int i = 0; i < name.Length; i++)
                {
                    var c = name[i];
                    // Proper Cyrillic: U+0400-U+04FF
                    if (c >= 0x0400 && c <= 0x04FF)
                        return true;
                    // Latin-1 supplement: U+0080-U+00FF (Cyrillic mojibake via byte->char cast)
                    if (c >= 0x80)
                        return true;
                }
                return false;
            });
        }
    }
}
