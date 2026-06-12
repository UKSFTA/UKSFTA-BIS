using System.Collections.Generic;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator
{
    public interface IRecoveryModule
    {
        string ModuleName { get; }
        DeobfuscationResult Recover(PBO pbo, DeobfuscationResult result, List<string> knownPaths, string prefix);
    }
}
