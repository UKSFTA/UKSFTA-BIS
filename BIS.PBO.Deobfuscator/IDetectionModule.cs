using BIS.PBO;

namespace BIS.PBO.Deobfuscator
{
    public interface IDetectionModule
    {
        string ModuleName { get; }
        bool IsMatch(PBO pbo);
    }
}
