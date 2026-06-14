using BIS.Core.Compression;
using BIS.Core.Streams;
using System;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace BIS.Core.Test.Compression
{
    public class LZSSTest
    {
        private byte[] data;

        public LZSSTest()
        {
            // Use random bytes with low entropy as test input
            data = new byte[8192];
            var rng = new Random();
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)rng.Next(0, 10);
            }
        }

        [Fact]
        public void CanEncode()
        {
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriterEx(buffer, true);

            writer.WriteLZSS(data, false);
            var compressed = buffer.ToArray();

            Assert.True(compressed.Length < data.Length);
        }

        [Fact]
        public void CanDecode()
        {
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriterEx(buffer, true);
            using var reader = new BinaryReaderEx(buffer);

            writer.WriteLZSS(data, false);
            reader.BaseStream.Position = 0;
            var result = reader.ReadLZSS((uint)data.Length, false);

            Assert.Equal(data, result);
        }

        [Fact]
        public void LzssStreamConsistent()
        {
            var buffer = new MemoryStream();
            using var compression = new LzssStream(buffer, CompressionMode.Compress, true);
            using var decompression = new LzssStream(buffer, CompressionMode.Decompress, true);

            compression.Write(data, 0, data.Length);
            buffer.Seek(0, SeekOrigin.Begin);
            var result = new byte[data.Length];
            int totalRead = 0;
            while (totalRead < result.Length)
            {
                int r = decompression.Read(result, totalRead, result.Length - totalRead);
                if (r == 0) break;
                totalRead += r;
            }

            Assert.Equal(data, result);
        }

        // ---- LzssOptimalEncoder tests ----

        [Fact]
        public void OptimalEncoder_EmptyInput_ReturnsEmpty()
        {
            var result = LzssOptimalEncoder.Compress(Array.Empty<byte>());
            Assert.Empty(result);
        }

        [Fact]
        public void OptimalEncoder_Roundtrip_RandomData()
        {
            var result = RoundtripOptimal(data);
            Assert.Equal(data, result);
        }

        [Fact]
        public void OptimalEncoder_Roundtrip_SmallData()
        {
            var small = new byte[] { 1, 2, 3, 4, 5 };
            var result = RoundtripOptimal(small);
            Assert.Equal(small, result);
        }

        [Fact]
        public void OptimalEncoder_Roundtrip_RepeatingData()
        {
            var repeating = new byte[4096];
            var rng = new Random();
            for (int i = 0; i < repeating.Length; i++)
                repeating[i] = (byte)(i % 4 * 64);
            var result = RoundtripOptimal(repeating);
            Assert.Equal(repeating, result);
        }

        [Fact]
        public void OptimalEncoder_Roundtrip_SingleByte()
        {
            var single = new byte[] { 0xAB };
            var result = RoundtripOptimal(single);
            Assert.Equal(single, result);
        }

        [Fact]
        public void OptimalEncoder_Roundtrip_AllSame()
        {
            var same = new byte[1024];
            Array.Fill(same, (byte)0x42);
            var result = RoundtripOptimal(same);
            Assert.Equal(same, result);
        }

        [Fact]
        public void OptimalEncoder_Compresses_RepeatingData()
        {
            var repeating = new byte[4096];
            var rng = new Random();
            for (int i = 0; i < repeating.Length; i++)
                repeating[i] = (byte)(i % 4 * 64);
            var compressed = LzssOptimalEncoder.Compress(repeating);
            Assert.True(compressed.Length < repeating.Length,
                $"Expected compression, got {compressed.Length} >= {repeating.Length}");
        }

        [Fact]
        public void OptimalEncoder_Output_DecodableByStandardDecoder()
        {
            // Optimal encoder output must be decodable by LZSS.ReadLZSS (same format)
            var compressed = LzssOptimalEncoder.Compress(data);
            int csum = 0;
            for (int i = 0; i < data.Length; i++)
                csum += data[i];

            using var ms = new MemoryStream(compressed.Length + 4);
            ms.Write(compressed, 0, compressed.Length);
            ms.Write(BitConverter.GetBytes(csum), 0, 4);
            ms.Position = 0;

            byte[] decompressed;
            LZSS.ReadLZSS(ms, out decompressed, (uint)data.Length, false);
            Assert.Equal(data, decompressed);
        }

        [Fact]
        public void WriteLZSS_Optimal_Roundtrip()
        {
            // Full integration: BinaryWriterEx.WriteLZSS with useOptimal=true → BinaryReaderEx.ReadLZSS
            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            using var reader = new BinaryReaderEx(ms);

            writer.WriteLZSS(data, inPAA: false, useOptimal: true);
            writer.Flush();
            ms.Position = 0;
            var result = reader.ReadLZSS((uint)data.Length, inPAA: false);

            Assert.Equal(data, result);
        }

        private static byte[] RoundtripOptimal(byte[] input)
        {
            var compressed = LzssOptimalEncoder.Compress(input);
            int csum = 0;
            for (int i = 0; i < input.Length; i++)
                csum += input[i];

            using var ms = new MemoryStream(compressed.Length + 4);
            ms.Write(compressed, 0, compressed.Length);
            ms.Write(BitConverter.GetBytes(csum), 0, 4);
            ms.Position = 0;

            LZSS.ReadLZSS(ms, out byte[] decompressed, (uint)input.Length, false);
            return decompressed;
        }
    }
}
