using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIS.PBO;
using BIS.P3D.MLOD;
using BIS.P3D.ODOL;
using BIS.Core.Streams;

namespace BIS.PBO.Deobfuscator
{
    public class P3DTextureReferenceUpdater : IReferenceUpdater
    {
        public byte[]? UpdateReferences(IPBOFileEntry fileEntry, Dictionary<string, string> pathMap)
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
                        var fixedPath = FixEncoding(odolLod.Textures[i]).Replace('\\', '/');
                        if (TryResolvePath(fixedPath, pathMap, out string newPath))
                        {
                            odolLod.Textures[i] = newPath;
                            modified = true;
                        }
                    }

                    for (int i = 0; i < odolLod.Materials.Length; i++)
                    {
                        var fixedPath = FixEncoding(odolLod.Materials[i].MaterialName).Replace('\\', '/');
                        if (TryResolvePath(fixedPath, pathMap, out string newPath))
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
                        var texNorm = FixEncoding(face.Texture).Replace('\\', '/');
                        if (TryResolvePath(texNorm, pathMap, out string newTex))
                        {
                            face.Texture = newTex;
                            modified = true;
                        }
                        var matNorm = FixEncoding(face.Material).Replace('\\', '/');
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

        /// <summary>
        /// P3D internal text strings (textures, materials) are read via ReadAsciiz(), which casts each
        /// byte to char directly — treating UTF-8 multi-byte sequences as two Latin-1 chars (mojibake).
        /// This reverses that by casting chars back to bytes and re-decoding as proper UTF-8,
        /// so Cyrillic obfuscation names match the pathMap keys (which come from ReadUTF8z()).
        /// Pure-ASCII paths pass through unchanged.
        /// </summary>
        internal static string FixEncoding(string s)
        {
            var bytes = new byte[s.Length];
            bool needsFix = false;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c > 127)
                    needsFix = true;
                bytes[i] = (byte)c;
            }
            return needsFix ? Encoding.UTF8.GetString(bytes) : s;
        }

        private static bool TryResolvePath(string contentPath, Dictionary<string, string> pathMap, out string? resolved)
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
