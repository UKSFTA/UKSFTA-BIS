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
            return pbo.Files.Any(f =>
            {
                var name = ProfileUtils.GetFileName(f.FileName);
                return name.Any(c => c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я');
            });
        }
    }
}
