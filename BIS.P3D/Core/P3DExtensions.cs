using System.Collections.Generic;
using System.Linq;
using BIS.P3D.MLOD;
using BIS.P3D.ODOL;

namespace BIS.P3D
{
    /// <summary>
    /// Extension methods for P3D model types, providing aggregated dependency analysis
    /// across all LODs. Mirrors HEMTT's P3D.dependencies() functionality.
    /// </summary>
    public static class P3DExtensions
    {
        /// <summary>All unique texture file paths referenced across every LOD in an ODOL model.</summary>
        public static IReadOnlySet<string> Dependencies(this ODOL.ODOL model)
        {
            var deps = new HashSet<string>();
            foreach (var lod in model.Lods)
            {
                foreach (var t in lod.GetTextures() ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrEmpty(t))
                        deps.Add(t);
                foreach (var m in lod.GetMaterials() ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrEmpty(m))
                        deps.Add(m);
            }
            return deps;
        }

        /// <summary>All unique texture and material file paths referenced across every LOD in an MLOD model.</summary>
        public static IReadOnlySet<string> Dependencies(this MLOD.MLOD model)
        {
            var deps = new HashSet<string>();
            foreach (var lod in model.Lods)
            {
                foreach (var t in lod.GetTextures() ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrEmpty(t))
                        deps.Add(t);
                foreach (var m in lod.GetMaterials() ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrEmpty(m))
                        deps.Add(m);
            }
            return deps;
        }

        /// <summary>All unique texture file paths referenced across every LOD in an ODOL model.</summary>
        public static IReadOnlySet<string> GetAllTextures(this ODOL.ODOL model)
        {
            var texs = new HashSet<string>();
            foreach (var lod in model.Lods)
            {
                foreach (var t in lod.GetTextures() ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrEmpty(t))
                        texs.Add(t);
            }
            return texs;
        }

        /// <summary>All unique material file paths referenced across every LOD in an ODOL model.</summary>
        public static IReadOnlySet<string> GetAllMaterials(this ODOL.ODOL model)
        {
            var mats = new HashSet<string>();
            foreach (var lod in model.Lods)
            {
                foreach (var m in lod.GetMaterials() ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrEmpty(m))
                        mats.Add(m);
            }
            return mats;
        }
    }
}
