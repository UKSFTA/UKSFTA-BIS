using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIS.PBO;
using BIS.P3D.MLOD;
using BIS.P3D.ODOL;
using BIS.Core.Streams;

namespace BIS.PBO.Deobfuscator
{
    public class P3DTextureReferenceUpdater : IReferenceUpdater
    {
        public byte[] UpdateReferences(IPBOFileEntry fileEntry, Dictionary<string, string> pathMap)
        {
            var ext = Path.GetExtension(fileEntry.FileName);
            if (!string.Equals(ext, ".p3d", StringComparison.OrdinalIgnoreCase))
                return null;

            BIS.P3D.P3D p3d;
            try
            {
                using var stream = fileEntry.OpenRead();
                p3d = new BIS.P3D.P3D(stream);
            }
            catch
            {
                return null;
            }

            bool modified = false;

            foreach (var lod in p3d.LODs)
            {
                if (lod is LOD odolLod)
                {
                    for (int i = 0; i < odolLod.Textures.Length; i++)
                    {
                        var normalized = odolLod.Textures[i].Replace('\\', '/');
                        if (TryResolvePath(normalized, pathMap, out string newPath))
                        {
                            odolLod.Textures[i] = newPath;
                            modified = true;
                        }
                    }

                    for (int i = 0; i < odolLod.Materials.Length; i++)
                    {
                        var normalized = odolLod.Materials[i].MaterialName.Replace('\\', '/');
                        if (TryResolvePath(normalized, pathMap, out string newPath))
                        {
                            odolLod.Materials[i].MaterialName = newPath;
                            modified = true;
                        }
                    }
                }

                if (lod is P3DM_LOD mlodLod)
                {
                    for (int i = 0; i < mlodLod.Faces.Length; i++)
                    {
                        var face = mlodLod.Faces[i];
                        var texNorm = face.Texture.Replace('\\', '/');
                        if (TryResolvePath(texNorm, pathMap, out string newTex))
                        {
                            face.Texture = newTex;
                            modified = true;
                        }
                        var matNorm = face.Material.Replace('\\', '/');
                        if (TryResolvePath(matNorm, pathMap, out string newMat))
                        {
                            face.Material = newMat;
                            modified = true;
                        }
                    }
                }
            }

            if (!modified)
                return null;

            using var ms = new MemoryStream();
            using (var writer = new BinaryWriterEx(ms, true))
            {
                p3d.Write(writer);
            }
            return ms.ToArray();
        }

        private static bool TryResolvePath(string contentPath, Dictionary<string, string> pathMap, out string resolved)
        {
            if (pathMap.TryGetValue(contentPath, out resolved))
                return true;

            var contentSlash = contentPath.LastIndexOf('/');
            var contentDir = contentSlash >= 0 ? contentPath.Substring(0, contentSlash) : "";
            var contentFile = contentSlash >= 0 ? contentPath.Substring(contentSlash + 1) : contentPath;

            foreach (var kvp in pathMap)
            {
                var key = kvp.Key;
                var keySlash = key.LastIndexOf('/');
                var keyDir = keySlash >= 0 ? key.Substring(0, keySlash) : "";
                var keyFile = keySlash >= 0 ? key.Substring(keySlash + 1) : key;

                if (!contentFile.EndsWith(keyFile, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!contentDir.EndsWith(keyDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolved = kvp.Value;
                return true;
            }

            resolved = null;
            return false;
        }
    }
}
