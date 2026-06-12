using System.Collections.Generic;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator
{
    public interface IReferenceUpdater
    {
        void UpdateReferences(PBO pbo, IPBOFileEntry fileEntry, Dictionary<string, string> pathMap);
    }
}
