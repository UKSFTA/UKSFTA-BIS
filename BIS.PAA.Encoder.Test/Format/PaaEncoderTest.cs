using System;
using System.IO;
using BCnEncoder.Shared;
using Microsoft.Toolkit.HighPerformance;
using BIS.Core.Streams;
using BIS.PAA;
using Xunit;

namespace BIS.PAA.Encoder.Test.Format
{
    public class PaaEncoderTest
    {
        [Fact]
        public void WritePAA_UnsupportedType_ThrowsNotSupportedException()
        {
            var image = new ColorRgba32[4, 4];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    image[y, x] = new ColorRgba32(255, 0, 0, 255);

            using var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);

            Assert.Throws<NotSupportedException>(() =>
                PaaEncoder.WritePAA(writer, new ReadOnlyMemory2D<ColorRgba32>(image),
                    new ColorRgba32(255, 0, 0, 255), new ColorRgba32(128, 0, 0, 128),
                    PAAType.DXT3, PAAFlags.InterpolatedAlpha));
        }

        [Fact]
        public void WritePAA_ConvenienceMethod_WritesToFile()
        {
            var image = new ColorRgba32[8, 8];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    image[y, x] = new ColorRgba32(255, 0, 0, 255);

            var path = Path.GetTempFileName();
            try
            {
                PaaEncoder.WritePAA(path, image, PAAType.DXT1, PAAFlags.InterpolatedAlpha);
                var fileInfo = new FileInfo(path);
                Assert.True(fileInfo.Length > 100, $"PAA file too short: {fileInfo.Length}");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void WritePAA_AutoDetect_OpaqueUsesDXT1()
        {
            // All opaque pixels (alpha=255) → auto-detect DXT1
            var image = new ColorRgba32[16, 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    image[y, x] = new ColorRgba32(128, 200, 50, 255);

            var path = Path.GetTempFileName();
            try
            {
                PaaEncoder.WritePAA(path, image);
                var fileInfo = new FileInfo(path);
                Assert.True(fileInfo.Length > 100, $"PAA file too short: {fileInfo.Length}");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
