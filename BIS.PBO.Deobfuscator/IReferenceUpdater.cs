using System.Collections.Generic;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator
{
    public interface IReferenceUpdater
    {
        byte[] UpdateReferences(IPBOFileEntry fileEntry, Dictionary<string, string> pathMap);
    }
}
