using System.IO;

namespace BIS.PAA
{
    public enum PaaTextureCategory
    {
        Unknown,
        ColorMap,
        NormalMap,
        AmbientShadow,
        Mask,
        SmoothMap,
        CoverageAlpha
    }

    public class PaaAnalysis
    {
        public PAAType Format { get; }
        public int Width { get; }
        public int Height { get; }
        public int MipmapCount { get; }
        public bool HasAlpha { get; }
        public bool IsTransparent { get; }
        public PaaTextureCategory Category { get; }
        public string CategoryLabel { get; }

        public PaaAnalysis(PAA paa)
        {
            Format = paa.Type;
            Width = paa.Width;
            Height = paa.Height;
            MipmapCount = 0;
            foreach (var _ in paa.Mipmaps) MipmapCount++;
            HasAlpha = paa.Palette?.IsAlpha ?? false;
            IsTransparent = paa.Palette?.IsTransparent ?? false;
            (Category, CategoryLabel) = Classify(paa, MipmapCount);
        }

        private static (PaaTextureCategory, string) Classify(PAA paa, int mipCount)
        {
            switch (paa.Type)
            {
                case PAAType.AI88:
                    return (PaaTextureCategory.AmbientShadow, "ambient shadow");

                case PAAType.DXT1:
                    if (mipCount <= 1 || (paa.Width <= 64 && paa.Height <= 64))
                        return (PaaTextureCategory.Mask, "mask");
                    if (paa.Palette?.IsAlpha == true)
                        return (PaaTextureCategory.NormalMap, "normal map");
                    return (PaaTextureCategory.ColorMap, "color");

                case PAAType.DXT3:
                case PAAType.DXT5:
                    if (paa.Palette?.IsTransparent == true)
                        return (PaaTextureCategory.CoverageAlpha, "coverage alpha");
                    return (PaaTextureCategory.ColorMap, "color");

                case PAAType.RGBA_4444:
                case PAAType.RGBA_5551:
                case PAAType.RGBA_8888:
                    return (PaaTextureCategory.ColorMap, "color");

                case PAAType.P8:
                    return (PaaTextureCategory.ColorMap, "color (indexed)");

                default:
                    return (PaaTextureCategory.Unknown, "unknown");
            }
        }
    }

    public class PaaFormatSuggestion
    {
        public PAAType RecommendedFormat { get; set; }
        public string Rationale { get; set; }
        public float EstimatedSizeFactor { get; set; }
        public string Notes { get; set; }
    }

    public static class PaaAnalyzer
    {
        /// <summary>Analyse a PAA file from path.</summary>
        public static PaaAnalysis Analyze(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return Analyze(stream);
        }

        /// <summary>Analyse a PAA file from an open stream.</summary>
        public static PaaAnalysis Analyze(Stream stream)
        {
            var paa = new PAA(stream);
            return new PaaAnalysis(paa);
        }

        /// <summary>
        /// Suggest the optimal PAA pixel format for a given texture,
        /// based on its current format and metadata flags.
        /// </summary>
        public static PaaFormatSuggestion SuggestOptimalFormat(PaaAnalysis analysis)
        {
            var fmt = analysis.Format;
            bool hasAlpha = analysis.HasAlpha;
            bool isTransparent = analysis.IsTransparent;

            switch (fmt)
            {
                // ── Already optimal ──
                case PAAType.DXT1 when !hasAlpha && !isTransparent:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = PAAType.DXT1,
                        Rationale = "DXT1 without alpha",
                        EstimatedSizeFactor = 1.0f,
                        Notes = "Already optimal. 4 bits/pixel, no alpha needed."
                    };

                case PAAType.AI88:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = PAAType.AI88,
                        Rationale = "AI88 (grayscale + alpha) is the smallest format for this use case",
                        EstimatedSizeFactor = 1.0f,
                        Notes = "Already optimal for ambient shadow / grayscale + alpha textures."
                    };

                // ── DXT1 with alpha → DXT5 ──
                case PAAType.DXT1 when hasAlpha || isTransparent:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = PAAType.DXT5,
                        Rationale = "DXT1 has only 1-bit alpha; DXT5 gives 8-bit interpolated alpha",
                        EstimatedSizeFactor = 2.0f,
                        Notes = "DXT5 is 8 bits/pixel vs DXT1's 4, but alpha quality is vastly better. Consider DXT3 (explicit alpha) if edges are sharp."
                    };

                // ── DXT3 → DXT5 ──
                case PAAType.DXT3:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = PAAType.DXT5,
                        Rationale = "DXT3 stores explicit 4-bit alpha (no interpolation). DXT5 gives smoother alpha gradients.",
                        EstimatedSizeFactor = 1.0f,
                        Notes = "Same size (8 bits/pixel). DXT5 interpolates alpha for smoother transitions. Keep DXT3 only for sharp binary-alpha cutouts."
                    };

                case PAAType.DXT4:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = PAAType.DXT5,
                        Rationale = "DXT4 is premultiplied DXT5. Use DXT5 unless premultiplied blending is required.",
                        EstimatedSizeFactor = 1.0f,
                        Notes = "Same size. DXT5 is the conventional choice."
                    };

                case PAAType.DXT2:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = PAAType.DXT3,
                        Rationale = "DXT2 is premultiplied DXT3. Use DXT3 unless premultiplied blending is required.",
                        EstimatedSizeFactor = 1.0f,
                        Notes = "Same size. DXT3 is the conventional choice."
                    };

                // ── DXT5 without alpha → DXT1 ──
                case PAAType.DXT5 when !hasAlpha && !isTransparent:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = PAAType.DXT1,
                        Rationale = "No alpha channel detected — DXT1 saves 50% space",
                        EstimatedSizeFactor = 0.5f,
                        Notes = "DXT1 (4 bits/pixel) vs DXT5 (8 bits/pixel). Halves texture memory at no quality cost for fully opaque textures."
                    };

                // ── RGBA_8888 → DXT1 or DXT5 ──
                case PAAType.RGBA_8888 when analysis.Width >= 32 && analysis.Height >= 32:
                    if (!hasAlpha && !isTransparent)
                        return new PaaFormatSuggestion
                        {
                            RecommendedFormat = PAAType.DXT1,
                            Rationale = "RGBA_8888 is 32 bits/pixel. DXT1 is 4 bits/pixel — 8× smaller.",
                            EstimatedSizeFactor = 0.125f,
                            Notes = "Significant size saving. Use DXT5 if alpha is needed."
                        };
                    else
                        return new PaaFormatSuggestion
                        {
                            RecommendedFormat = PAAType.DXT5,
                            Rationale = "RGBA_8888 is 32 bits/pixel. DXT5 is 8 bits/pixel — 4× smaller with good alpha.",
                            EstimatedSizeFactor = 0.25f,
                            Notes = "Major size reduction. For very small textures (< 32px), RGBA_8888 may avoid block artifacts."
                        };

                // ── RGBA_4444 / RGBA_5551 → DXT1 or DXT5 ──
                case PAAType.RGBA_4444:
                case PAAType.RGBA_5551:
                    if (!hasAlpha && !isTransparent)
                        return new PaaFormatSuggestion
                        {
                            RecommendedFormat = PAAType.DXT1,
                            Rationale = $"{fmt} is 16 bits/pixel. DXT1 is 4 bits/pixel — 4× smaller.",
                            EstimatedSizeFactor = 0.25f,
                            Notes = "DXT1 at 4bpp is smaller and quality is comparable for opaque textures."
                        };
                    else
                        return new PaaFormatSuggestion
                        {
                            RecommendedFormat = PAAType.DXT5,
                            Rationale = $"{fmt} is 16 bits/pixel. DXT5 is 8 bits/pixel — 2× smaller with better alpha.",
                            EstimatedSizeFactor = 0.5f,
                            Notes = "DXT5 has better alpha quality than RGBA_4444's 4-bit alpha."
                        };

                // ── RGBA_8888 small textures ──
                case PAAType.RGBA_8888:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = PAAType.RGBA_8888,
                        Rationale = "Texture is small (< 32px). Block compression would cause artifacts.",
                        EstimatedSizeFactor = 1.0f,
                        Notes = "Keep RGBA_8888 for small textures. If size is critical, try RGBA_4444 (2× smaller, lower quality)."
                    };

                // ── P8 ──
                case PAAType.P8:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = PAAType.DXT1,
                        Rationale = "P8 (palette-indexed) is 8 bits/pixel. DXT1 is 4 bits/pixel with higher color precision.",
                        EstimatedSizeFactor = 0.5f,
                        Notes = "DXT1 is smaller and supports full RGB. Only keep P8 if palette effects (swizzling) are required."
                    };

                default:
                    return new PaaFormatSuggestion
                    {
                        RecommendedFormat = fmt,
                        Rationale = "Unknown format — no suggestion available.",
                        EstimatedSizeFactor = 1.0f,
                        Notes = ""
                    };
            }
        }
    }
}
