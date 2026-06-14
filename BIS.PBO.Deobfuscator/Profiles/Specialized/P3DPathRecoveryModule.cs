using BIS.PBO.Deobfuscator;
using System;
using System.IO;
using BIS.PBO;
using BIS.P3D;

namespace BIS.PBO.Deobfuscator.Profiles.Specialized
{
    public class P3DPathRecoveryModule : IRecoveryModule
    {
        public string ModuleName => "P3D Path Recovery";
        public DeobfuscationResult Recover(PBO pbo, DeobfuscationResult result, List<string> knownPaths, string prefix)
        {
            Console.WriteLine("  -> Scanning P3D files for embedded paths...");
            int p3dScanned = 0;
            int p3dPaths = 0;

            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var file = pbo.Files[fi];
                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".p3d", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var stream = file.OpenRead();
                    var p3d = new BIS.P3D.P3D(stream);
                    p3dScanned++;

                    foreach (var lod in p3d.LODs)
                    {
                        foreach (var tex in lod.GetTextures())
                        {
                            var norm = ProfileUtils.NormalizePath(tex);
                            if (!string.IsNullOrEmpty(norm) && ProfileUtils.IsValidPathString(norm) && !knownPaths.Contains(norm))
                            {
                                knownPaths.Add(norm);
                                p3dPaths++;
                            }
                        }
                        foreach (var mat in lod.GetMaterials())
                        {
                            var norm = ProfileUtils.NormalizePath(mat);
                            if (!string.IsNullOrEmpty(norm) && ProfileUtils.IsValidPathString(norm) && !knownPaths.Contains(norm))
                            {
                                knownPaths.Add(norm);
                                p3dPaths++;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            if (p3dScanned > 0)
                Console.WriteLine($"  -> Scanned {p3dScanned} P3D files, extracted {p3dPaths} unique paths.");

            return result;
        }
    }
}
