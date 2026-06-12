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

    public static class PaaAnalyzer
    {
        public static PaaAnalysis Analyze(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return Analyze(stream);
        }

        public static PaaAnalysis Analyze(Stream stream)
        {
            var paa = new PAA(stream);
            return new PaaAnalysis(paa);
        }
    }
}
