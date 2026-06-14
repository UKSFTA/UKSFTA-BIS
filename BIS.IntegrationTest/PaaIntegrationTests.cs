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
            var paa = new global::BIS.PAA.PAA(file);
            Assert.NotNull(paa);
            Assert.True(paa.Width > 0);
            Assert.True(paa.Height > 0);
        }
    }

    [Fact]
    public void Read_MultiplePaa_HaveReasonableDimensions()
    {
        var files = TestData.GetFiles("paa", "*.paa");
        if (files.Length == 0) return;

        foreach (var file in files)
        {
            var paa = new global::BIS.PAA.PAA(file);
            Assert.InRange(paa.Width, 1, 4096);
            Assert.InRange(paa.Height, 1, 4096);
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
}
