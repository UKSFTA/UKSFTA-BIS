using System;
using Xunit;

namespace BIS.Core.Test.Compression
{
    public class LZOTest
    {
        [Fact]
        public void LZO_Roundtrip_SmallData()
        {
            var original = new byte[] { 1, 2, 3, 4, 5 };
            var compressed = MiniLZO.MiniLZO.Compress(original);
            var decompressed = new byte[original.Length];
            MiniLZO.MiniLZO.Decompress(compressed, decompressed);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void LZO_Roundtrip_LargeData()
        {
            var original = new byte[16384];
            var rng = new Random(12345);
            rng.NextBytes(original);
            var compressed = MiniLZO.MiniLZO.Compress(original);
            var decompressed = new byte[original.Length];
            MiniLZO.MiniLZO.Decompress(compressed, decompressed);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void LZO_Roundtrip_RepeatingData()
        {
            var original = new byte[8192];
            for (int i = 0; i < original.Length; i++)
                original[i] = (byte)(i % 8);
            var compressed = MiniLZO.MiniLZO.Compress(original);
            Assert.True(compressed.Length < original.Length,
                $"LZO should compress repeating data, got {compressed.Length} >= {original.Length}");
            var decompressed = new byte[original.Length];
            MiniLZO.MiniLZO.Decompress(compressed, decompressed);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void LZO_Roundtrip_AllSame()
        {
            var original = new byte[4096];
            Array.Fill(original, (byte)0xAB);
            var compressed = MiniLZO.MiniLZO.Compress(original);
            var decompressed = new byte[original.Length];
            MiniLZO.MiniLZO.Decompress(compressed, decompressed);
            Assert.Equal(original, decompressed);
        }
    }
}
