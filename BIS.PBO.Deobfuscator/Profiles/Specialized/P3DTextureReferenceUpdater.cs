using System;
using System.Collections.Generic;
using System.IO;
using BIS.PBO;
using BIS.P3D;
using BIS.P3D.ODOL;
using BIS.Core.Streams;

namespace BIS.PBO.Deobfuscator
{
    public class P3DTextureReferenceUpdater : IReferenceUpdater
    {
        public void UpdateReferences(PBO pbo, IPBOFileEntry fileEntry, Dictionary<string, string> pathMap)
        {
            using var stream = fileEntry.OpenRead();
            var p3d = new BIS.P3D.P3D(stream);

            bool modified = false;

            foreach (var lod in p3d.LODs)
            {
                if (lod is BIS.P3D.ODOL.LOD odolLod)
                {
                    for (int i = 0; i < odolLod.Textures.Length; i++)
                    {
                        if (pathMap.TryGetValue(odolLod.Textures[i], out string newPath))
                        {
                            odolLod.Textures[i] = newPath;
                            modified = true;
                        }
                    }
                }
            }

            if (modified)
            {
                var memStream = new MemoryStream();
                using var writer = new BIS.Core.Streams.BinaryWriterEx(memStream, true);
                p3d.Write(writer);
                
                // --- Re-persisting ---
                // This is a placeholder for actual entry replacement in the PBO structure
                // which should be handled by the orchestrator in the final pipeline.
            }
        }
    }
}
