using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BIS.PAA.Test.Conversion;

public class PaaConverterTest
{
    [Fact]
    public void PaaToPng_EmptyStream_Throws()
    {
        using var emptyPaa = new MemoryStream();
        using var png = new MemoryStream();

        Assert.ThrowsAny<Exception>(() =>
            BIS.PAA.Conversion.PaaConverter.PaaToPng(emptyPaa, png));
    }

    [Fact]
    public void PngToPaa_And_PaaToPng_RoundtripViaFile()
    {
        using var pngStream = new MemoryStream();
        using (var image = new Image<Bgra32>(16, 16))
        {
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    image[x, y] = new Bgra32((byte)(x * 16), (byte)(y * 16), 128, 255);
            image.SaveAsPng(pngStream);
        }
        pngStream.Position = 0;

        using var imageLoaded = Image.Load<Bgra32>(pngStream);
        var pixels = new byte[16 * 16 * 4];
        imageLoaded.CopyPixelDataTo(pixels);

        var colorArray = new BCnEncoder.Shared.ColorRgba32[16, 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int idx = (y * 16 + x) * 4;
                colorArray[y, x] = new BCnEncoder.Shared.ColorRgba32(
                    pixels[idx + 2], pixels[idx + 1], pixels[idx], pixels[idx + 3]);
            }

        using var ms = new MemoryStream();
        var writer = new BIS.Core.Streams.BinaryWriterEx(ms);
        BIS.PAA.Encoder.PaaEncoder.WritePAA(writer, new Microsoft.Toolkit.HighPerformance.ReadOnlyMemory2D<BCnEncoder.Shared.ColorRgba32>(colorArray),
            new BCnEncoder.Shared.ColorRgba32(255, 255, 255, 255), new BCnEncoder.Shared.ColorRgba32(128, 128, 128, 255),
            BIS.PAA.PAAType.DXT1, BIS.PAA.Encoder.PAAFlags.InterpolatedAlpha);
        writer.Flush();
        ms.Position = 0;

        using var png2 = new MemoryStream();
        BIS.PAA.Conversion.PaaConverter.PaaToPng(ms, png2);

        png2.Position = 0;
        using var result = Image.Load<Bgra32>(png2);
        Assert.Equal(16, result.Width);
        Assert.Equal(16, result.Height);
    }

    [Fact]
    public void PngToPaa_ProducesValidPaaOutput()
    {
        using var pngStream = new MemoryStream();
        using (var image = new Image<Bgra32>(8, 8))
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    image[x, y] = new Bgra32(255, 0, 0, 255);
            image.SaveAsPng(pngStream);
        }
        pngStream.Position = 0;

        using var paaStream = new MemoryStream();
        BIS.PAA.Conversion.PaaConverter.PngToPaa(pngStream, paaStream);

        Assert.True(paaStream.Length > 0, "PAA output should not be empty");
        var data = new byte[paaStream.Length];
        paaStream.Position = 0;
        paaStream.Read(data);

        Assert.Equal(0xFF, data[0]);
        Assert.Equal(0x01, data[1]);
    }
}
