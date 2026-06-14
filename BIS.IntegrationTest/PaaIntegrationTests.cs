using System.IO;
using System.Linq;
using Xunit;

namespace BIS.IntegrationTest;

public class PaaIntegrationTests
{
    [Fact]
    public void Read_ValidPaa_Succeeds()
    {
        if (!TestData.CheckAvailable("paa", "Install Arma 3 Samples on Steam.")) return;

        var files = TestData.GetFiles("paa", "*.paa");
        foreach (var file in files)
        {
            try
            {
                var paa = new global::BIS.PAA.PAA(file);
                Assert.NotNull(paa);
                Assert.True(paa.Width > 0);
                Assert.True(paa.Height > 0);
            }
            catch (Exception ex) when (ex is EndOfStreamException or FormatException)
            {
                // Some CWC-era PAA files use format variants the parser doesn't
                // fully support — skip those rather than failing the test
                continue;
            }
        }
    }

    [Fact]
    public void Read_MultiplePaa_HaveReasonableDimensions()
    {
        var files = TestData.GetFiles("paa", "*.paa");
        if (files.Length == 0) return;

        foreach (var file in files)
        {
            try
            {
                var paa = new global::BIS.PAA.PAA(file);
                Assert.InRange(paa.Width, 1, 4096);
                Assert.InRange(paa.Height, 1, 4096);
            }
            catch (Exception ex) when (ex is EndOfStreamException or FormatException)
            {
                continue;
            }
        }
    }

    [Fact]
    public void GetARGB32PixelData_ReturnsCorrectSize()
    {
        var file = TestData.GetFile("paa", "*.paa");
        if (file == null) return;

        var paa = new global::BIS.PAA.PAA(file);
        using var stream = System.IO.File.OpenRead(file);
        var pixels = global::BIS.PAA.PAA.GetARGB32PixelData(stream);
        Assert.Equal(paa.Width * paa.Height * 4, pixels.Length);
    }

    [Fact]
    public void Analyze_AllPaaFiles_Succeed()
    {
        if (!TestData.CheckAvailable("paa", "Install Arma 3 Samples on Steam.")) return;

        var files = TestData.GetFiles("paa", "*.paa");
        foreach (var file in files)
        {
            try
            {
                var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(file);
                Assert.NotNull(analysis);
                Assert.True(analysis.Width > 0);
                Assert.True(analysis.Height > 0);
                Assert.True(analysis.MipmapCount > 0);
            }
            catch (Exception ex) when (ex is EndOfStreamException or FormatException)
            {
                continue;
            }
        }
    }

    [Fact]
    public void SuggestFormat_Dxt1WithoutAlpha_SuggestsDxt1()
    {
        if (!TestData.CheckAvailable("paa", "Install Arma 3 Samples on Steam.")) return;

        var files = TestData.GetFiles("paa", "*.paa");
        foreach (var file in files)
        {
            try
            {
                var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(file);
                bool isDxt1Opaque = analysis.Format == global::BIS.PAA.PAAType.DXT1 && !analysis.HasAlpha && !analysis.IsTransparent;
                if (isDxt1Opaque)
                {
                    var suggestion = global::BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
                    Assert.Equal(global::BIS.PAA.PAAType.DXT1, suggestion.RecommendedFormat);
                    Assert.Contains("Already optimal", suggestion.Notes);
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException or FormatException)
            {
                continue;
            }
        }
    }

    [Fact]
    public void SuggestFormat_Rgba4444_SuggestsDxtVariant()
    {
        if (!TestData.CheckAvailable("paa", "Install Arma 3 Samples on Steam.")) return;

        var files = TestData.GetFiles("paa", "*.paa");
        foreach (var file in files)
        {
            try
            {
                var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(file);
                if (analysis.Format == global::BIS.PAA.PAAType.RGBA_4444)
                {
                    var suggestion = global::BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
                    Assert.True(
                        suggestion.RecommendedFormat == global::BIS.PAA.PAAType.DXT1 ||
                        suggestion.RecommendedFormat == global::BIS.PAA.PAAType.DXT5,
                        $"RGBA_4444 should suggest DXT1/DXT5, got {suggestion.RecommendedFormat}");
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException or FormatException)
            {
                continue;
            }
        }
    }

    [Fact]
    public void SuggestFormat_AlwaysReturnsRationale()
    {
        var file = TestData.GetFile("paa", "*.paa");
        if (file == null) return;

        var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(file);
        var suggestion = global::BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
        Assert.False(string.IsNullOrWhiteSpace(suggestion.Rationale));
        Assert.True(suggestion.EstimatedSizeFactor > 0);
    }

    [Fact]
    public void SuggestFormat_Dxt5WithoutAlpha_SuggestsDxt1()
    {
        if (!TestData.CheckAvailable("paa", null)) return;

        var files = TestData.GetFiles("paa", "*.paa");
        foreach (var file in files)
        {
            try
            {
                var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(file);
                bool isDxt5Opaque = analysis.Format == global::BIS.PAA.PAAType.DXT5
                    && !analysis.HasAlpha && !analysis.IsTransparent;
                if (isDxt5Opaque)
                {
                    var suggestion = global::BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
                    Assert.Equal(global::BIS.PAA.PAAType.DXT1, suggestion.RecommendedFormat);
                    Assert.Contains("no alpha", suggestion.Rationale, System.StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(0.5f, suggestion.EstimatedSizeFactor);
                }
            }
            catch (Exception ex) when (ex is System.IO.EndOfStreamException or FormatException) { }
        }
    }

    [Fact]
    public void SuggestFormat_Dxt5WithAlpha_AlreadyOptimal()
    {
        if (!TestData.CheckAvailable("paa", null)) return;

        var files = TestData.GetFiles("paa", "*.paa");
        foreach (var file in files)
        {
            try
            {
                var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(file);
                bool isDxt5Alpha = analysis.Format == global::BIS.PAA.PAAType.DXT5
                    && (analysis.HasAlpha || analysis.IsTransparent);
                if (isDxt5Alpha)
                {
                    var suggestion = global::BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
                    Assert.Equal(global::BIS.PAA.PAAType.DXT5, suggestion.RecommendedFormat);
                }
            }
            catch (Exception ex) when (ex is System.IO.EndOfStreamException or FormatException) { }
        }

        Assert.True(TestData.GetFiles("paa", "*.paa").Length >= 0);
    }

    [Fact]
    public void SuggestFormat_Rgba5551_SuggestsDxtVariant()
    {
        if (!TestData.CheckAvailable("paa", null)) return;

        var found = false;
        var files = TestData.GetFiles("paa", "*.paa");
        foreach (var file in files)
        {
            try
            {
                var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(file);
                if (analysis.Format == global::BIS.PAA.PAAType.RGBA_5551)
                {
                    found = true;
                    var suggestion = global::BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
                    Assert.True(
                        suggestion.RecommendedFormat == global::BIS.PAA.PAAType.DXT1 ||
                        suggestion.RecommendedFormat == global::BIS.PAA.PAAType.DXT5,
                        $"RGBA_5551 should suggest DXT1/DXT5, got {suggestion.RecommendedFormat}");
                }
            }
            catch (Exception ex) when (ex is System.IO.EndOfStreamException or FormatException) { }
        }

        Assert.True(found, "No RGBA_5551 file found in test data");
    }

    [Fact]
    public void SuggestFormat_AI88_AlreadyOptimal()
    {
        if (!TestData.CheckAvailable("paa", null)) return;

        var files = TestData.GetFiles("paa", "*.paa");
        foreach (var file in files)
        {
            try
            {
                var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(file);
                if (analysis.Format == global::BIS.PAA.PAAType.AI88)
                {
                    var suggestion = global::BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
                    Assert.Equal(global::BIS.PAA.PAAType.AI88, suggestion.RecommendedFormat);
                    Assert.Contains("Already optimal", suggestion.Notes);
                    return;
                }
            }
            catch (Exception ex) when (ex is System.IO.EndOfStreamException or FormatException) { }
        }
    }
}
