using BIS.P3D.ODOL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BIS.P3D
{
    /// <summary>
    /// Result of a P3D validation / analysis pass.
    /// </summary>
    public class P3DValidationResult
    {
        /// <summary>File path (if opened from file).</summary>
        public string FilePath { get; internal set; }

        // ── Format info ──
        public bool IsValid { get; internal set; }
        public bool IsODOL { get; internal set; }
        public bool IsMLOD { get; internal set; }
        public int Version { get; internal set; }
        public bool IsEncrypted { get; internal set; }
        public string ModelClass { get; internal set; }
        public string MapType { get; internal set; }

        // ── LOD summary ──
        public int LodCount { get; internal set; }
        public List<LodInfo> LODs { get; internal set; } = new List<LodInfo>();

        // ── Issues ──
        public List<ValidationIssue> Issues { get; internal set; } = new List<ValidationIssue>();

        // ── Totals ──
        public int TotalVertices { get; internal set; }
        public int TotalFaces { get; internal set; }

        public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
        public bool HasWarnings => Issues.Any(i => i.Severity == IssueSeverity.Warning);
    }

    public class LodInfo
    {
        public float Resolution { get; set; }
        public LodName Type { get; set; }
        public string TypeName => Type.GetLODName(Resolution);
        public int VertexCount { get; set; }
        public int FaceCount { get; set; }
        public int TextureCount { get; set; }
        public List<string> Textures { get; set; } = new List<string>();
        public bool HasSkeletonBinding { get; set; }
        public bool HasNamedSelections { get; set; }
        public float BoundingRadius { get; set; }
        public ulong FaceArea { get; set; }
    }

    public enum IssueSeverity { Info, Warning, Error }

    public class ValidationIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string LodInfo { get; set; }

        public override string ToString()
            => $"[{Severity}] {Code}: {Message}" + (LodInfo != null ? $" ({LodInfo})" : "");
    }

    /// <summary>
    /// Static validator / analyser for P3D model files.
    /// Covers format validation, LOD completeness checks, and content statistics.
    /// </summary>
    public static class P3DValidator
    {
        /// <summary>Analyse a P3D file from path.</summary>
        public static P3DValidationResult Analyse(string filePath)
        {
            try
            {
                using var fs = File.OpenRead(filePath);
                return Analyse(fs, filePath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return new P3DValidationResult
                {
                    FilePath = filePath,
                    IsValid = false,
                    Issues = { new ValidationIssue { Severity = IssueSeverity.Error, Code = "FILE_NOT_FOUND", Message = $"File not found: {ex.Message}" } }
                };
            }
        }

        /// <summary>Analyse a P3D file from an open stream.</summary>
        public static P3DValidationResult Analyse(Stream stream, string filePath = null)
        {
            var result = new P3DValidationResult { FilePath = filePath };

            try
            {
                var p3d = new P3D(stream);
                result.IsODOL = p3d.IsODOLFormat;
                result.IsMLOD = p3d.IsMLODFormat;
                result.IsValid = true;
                result.Version = p3d.Version;

                var lods = p3d.LODs?.ToList() ?? new List<ILevelOfDetail>();
                result.LodCount = lods.Count;

                // Collect per-LOD info
                foreach (var lod in lods)
                {
                    var info = new LodInfo
                    {
                        Resolution = lod.Resolution,
                        Type = lod.Resolution.GetLODType(),
                        VertexCount = (int)lod.VertexCount,
                        FaceCount = lod.FaceCount,
                        TextureCount = lod.GetTextures()?.Count() ?? 0,
                        Textures = lod.GetTextures()?.ToList() ?? new List<string>(),
                        HasNamedSelections = lod.NamedSelections?.Any() == true,
                        BoundingRadius = 0f, // not on interface, OK
                    };
                    result.LODs.Add(info);
                    result.TotalVertices += info.VertexCount;
                    result.TotalFaces += info.FaceCount;
                }

                // Populate model-level info
                if (p3d.ModelInfo != null)
                {
                    result.ModelClass = p3d.ModelInfo.Class;
                    result.MapType = p3d.ModelInfo.MapType.ToString();
                }

                // ── Validation checks ──

                // 1. Empty model
                if (result.LodCount == 0)
                    result.Issues.Add(new ValidationIssue
                    { Severity = IssueSeverity.Error, Code = "EMPTY", Message = "Model contains no LODs." });

                // 2. Encrypted ODOL (v75+ with non-zero keys)
                // ODOL constructor throws on encryption; reaching here means data is readable

                // 3. ODOL version
                if (result.IsODOL)
                {
                    if (result.Version < 30)
                        result.Issues.Add(new ValidationIssue
                        { Severity = IssueSeverity.Warning, Code = "OLD_VER", Message = $"Very old ODOL version {result.Version} — may have compatibility issues." });
                    if (result.Version >= 75)
                        result.Issues.Add(new ValidationIssue
                        { Severity = IssueSeverity.Info, Code = "V75PLUS", Message = $"Version {result.Version} — P3D may be encrypted or use advanced features." });
                }

                // 4. Check for critical special LODs (geometry LODs)
                CheckCriticalLods(result);

                // 5. LOD resolution spread
                CheckResolutionSpread(result);

                // 6. Individual LOD issues
                CheckLodHealth(result);

                // 7. Vertex limits
                CheckVertexLimits(result);

                // 8. Material / texture references
                CheckTextureRefs(result);
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Issues.Add(new ValidationIssue
                { Severity = IssueSeverity.Error, Code = "PARSE_ERR", Message = $"Failed to parse: {ex.Message}" });
            }

            return result;
        }

        // ──────────────────────────────────────────────
        // LOD completeness checks
        // ──────────────────────────────────────────────

        private static void CheckCriticalLods(P3DValidationResult result)
        {
            var types = new HashSet<LodName>(result.LODs.Select(l => l.Type));

            // Collision LODs: at least one of Geometry/PhysX must exist
            bool hasGeo = types.Contains(LodName.Geometry) || types.Contains(LodName.PhysX);
            if (!hasGeo)
                result.Issues.Add(new ValidationIssue
                { Severity = IssueSeverity.Warning, Code = "NO_GEO", Message = "No Geometry or PhysX collision LOD found. Model will not have collision." });

            // Fire geometry
            if (!types.Contains(LodName.FireGeometry))
                result.Issues.Add(new ValidationIssue
                { Severity = IssueSeverity.Info, Code = "NO_FIREGEO", Message = "No Fire Geometry LOD (7e15). Bullet impacts use geometry LOD instead." });

            // Hitpoints
            if (!types.Contains(LodName.HitPoints))
                result.Issues.Add(new ValidationIssue
                { Severity = IssueSeverity.Info, Code = "NO_HITPTS", Message = "No HitPoints LOD (5e15). Model has no damage hitpoint zones." });

            // Memory / selections
            if (!types.Contains(LodName.Memory))
                result.Issues.Add(new ValidationIssue
                { Severity = IssueSeverity.Warning, Code = "NO_MEMORY", Message = "No Memory LOD (1e15). Model lacks named selection / memory point definitions." });

            // Roads / Paths (vehicle-specific)
            if (types.Contains(LodName.Roadway))
                result.Issues.Add(new ValidationIssue
                { Severity = IssueSeverity.Info, Code = "HAS_ROAD", Message = $"Model has Roadway LOD — intended for road/waypoint use." });

            // View geometry checks
            if (types.Any(t => t == LodName.ViewPilot || t == LodName.ViewPilotGeometry))
            {
                if (!types.Contains(LodName.ViewPilotGeometry))
                    result.Issues.Add(new ValidationIssue
                    { Severity = IssueSeverity.Warning, Code = "NO_VPILOTGEO", Message = "ViewPilot LOD present but no ViewPilotGeometry (13e15). First-person may lack collision." });
            }

            // Wreck LOD
            if (!types.Contains(LodName.Wreck))
                result.Issues.Add(new ValidationIssue
                { Severity = IssueSeverity.Info, Code = "NO_WRECK", Message = "No Wreck LOD (21e15). Model uses default wreck visual." });
        }

        private static void CheckResolutionSpread(P3DValidationResult result)
        {
            // Only check visual resolution LODs (non-special)
            var resLods = result.LODs
                .Where(l => l.Type == LodName.Resolution)
                .OrderByDescending(l => l.Resolution)
                .ToList();

            if (resLods.Count <= 1) return;

            for (int i = 0; i < resLods.Count - 1; i++)
            {
                float gap = resLods[i].Resolution - resLods[i + 1].Resolution;
                float ratio = resLods[i].Resolution / resLods[i + 1].Resolution;

                if (ratio > 10f)
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Code = "LOD_GAP",
                        Message = $"Large LOD gap between {resLods[i].Resolution:F1}→{resLods[i + 1].Resolution:F1} ({ratio:F1}x). Pop-in may be visible.",
                        LodInfo = $"{resLods[i].TypeName} → {resLods[i + 1].TypeName}"
                    });
            }
        }

        private static void CheckLodHealth(P3DValidationResult result)
        {
            foreach (var lod in result.LODs)
            {
                // Overly detailed Geometry LOD
                if (lod.Type == LodName.Geometry && lod.FaceCount > 2000)
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Code = "GEO_HIPOLY",
                        Message = $"Geometry LOD has {lod.FaceCount} faces. Should be under ~1000 for good physics performance.",
                        LodInfo = lod.TypeName
                    });

                // Zero-vertex LODs
                if (lod.VertexCount == 0 && lod.Type != LodName.Memory)
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Code = "ZERO_VTX",
                        Message = $"LOD has zero vertices — may be empty or corrupt.",
                        LodInfo = lod.TypeName
                    });

                // Massive LODs
                if (lod.VertexCount > 65535)
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Code = "VTX_LIMIT_OLD",
                        Message = $"{lod.VertexCount} vertices exceeds pre-v69 65535 limit. Only ODOL v69+ can load this LOD.",
                        LodInfo = lod.TypeName
                    });
            }
        }

        private static void CheckVertexLimits(P3DValidationResult result)
        {
            // If total model has millions of vertices, warn about resource usage
            if (result.TotalVertices > 500000)
                result.Issues.Add(new ValidationIssue
                {
                    Severity = IssueSeverity.Warning,
                    Code = "HI_TOTAL_VTX",
                    Message = $"Model has {result.TotalVertices} total vertices. May cause GPU/CPU load issues."
                });

            // If highest-detail LOD has very high vertex count
            var highest = result.LODs.Where(l => l.Type == LodName.Resolution)
                                     .OrderByDescending(l => l.Resolution)
                                     .FirstOrDefault();
            if (highest != null && highest.VertexCount > 50000)
                result.Issues.Add(new ValidationIssue
                {
                    Severity = IssueSeverity.Warning,
                    Code = "HI_RES_VTX",
                    Message = $"Highest-detail LOD has {highest.VertexCount} vertices. Consider optimisation.",
                    LodInfo = highest.TypeName
                });
        }

        private static void CheckTextureRefs(P3DValidationResult result)
        {
            var allTexs = result.LODs.SelectMany(l => l.Textures).Distinct().ToList();
            if (allTexs.Count == 0)
            {
                result.Issues.Add(new ValidationIssue
                { Severity = IssueSeverity.Info, Code = "NO_TEX", Message = "Model references no textures — may use in-line materials or be textureless." });
            }
        }
    }

    /// <summary>
    /// Extension to make LodName printable even for Resolution-type LODs.
    /// </summary>
    internal static class LodNameExtensions
    {
        public static string GetLODName(this LodName type, float resolution)
        {
            if (type == LodName.Resolution)
                return $"Resolution LOD {resolution:F1}";
            if (type == LodName.ShadowVolume && resolution >= 10000f && resolution < 20000f)
                return $"ShadowVolume {(resolution - 10000f):F3}";
            return type.ToString();
        }
    }
}
