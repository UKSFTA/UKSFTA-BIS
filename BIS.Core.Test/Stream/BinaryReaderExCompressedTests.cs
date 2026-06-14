using System;
using System.IO;
using System.Linq;
using Xunit;
using BIS.Core.Streams;

namespace BIS.Core.Test.Stream
{
    public class BinaryReaderExCompressedTests
    {
        [Fact]
        public void ReadCompressedFloatArray_Small_RawPath()
        {
            var expected = new float[] { 1.0f, 2.5f, -3.0f, 100.0f, 0.0f, -1.5f };
            var bytes = new byte[expected.Length * 4];
            Buffer.BlockCopy(expected, 0, bytes, 0, bytes.Length);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            writer.Write(expected.Length);
            writer.Write(bytes); // raw ( < 1024 bytes -> no compression )
            writer.Flush();
            ms.Position = 0;

            var reader = new BinaryReaderEx(ms);
            var result = reader.ReadCompressedFloatArray();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ReadCompressedFloatArray_Large_CompressedPath()
        {
            var expected = Enumerable.Range(0, 300).Select(i => (float)i * 0.5f).ToArray();
            var bytes = new byte[expected.Length * 4];
            Buffer.BlockCopy(expected, 0, bytes, 0, bytes.Length);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            writer.WriteCompressedFloatArray(expected);
            writer.Flush();
            ms.Position = 0;

            var reader = new BinaryReaderEx(ms);
            var result = reader.ReadCompressedFloatArray();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ReadCompressedIntArray_Roundtrip()
        {
            var expected = Enumerable.Range(0, 500).Select(i => i * 100).ToArray();

            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            writer.WriteCompressedIntArray(expected);
            writer.Flush();
            ms.Position = 0;

            var reader = new BinaryReaderEx(ms);
            var result = reader.ReadCompressedIntArray();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ReadCompressedShortArray_Roundtrip()
        {
            var expected = Enumerable.Range(0, 500).Select(i => (short)(i * 10)).ToArray();

            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            writer.WriteCompressedArray(expected, (w, v) => w.Write(v), 2);
            writer.Flush();
            ms.Position = 0;

            var reader = new BinaryReaderEx(ms);
            var result = reader.ReadCompressedShortArray();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ReadCompressedArray_Empty_RawPath()
        {
            var expected = Array.Empty<float>();

            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            writer.WriteCompressedFloatArray(expected);
            writer.Flush();
            ms.Position = 0;

            var reader = new BinaryReaderEx(ms);
            var result = reader.ReadCompressedFloatArray();
            Assert.Empty(result);
        }

        [Fact]
        public void ReadCompressed_FloatArray_ExactBoundary_1023Bytes()
        {
            // 255 floats = 1020 bytes, just under the 1024 compression threshold
            var expected = Enumerable.Range(0, 255).Select(i => (float)i).ToArray();

            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            writer.WriteCompressedFloatArray(expected);
            writer.Flush();
            ms.Position = 0;

            var reader = new BinaryReaderEx(ms);
            var result = reader.ReadCompressedFloatArray();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ReadCompressed_FloatArray_ExactBoundary_1024Bytes()
        {
            // 256 floats = 1024 bytes, at the compression threshold
            var expected = Enumerable.Range(0, 256).Select(i => (float)i).ToArray();

            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            writer.WriteCompressedFloatArray(expected);
            writer.Flush();
            ms.Position = 0;

            var reader = new BinaryReaderEx(ms);
            var result = reader.ReadCompressedFloatArray();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WriteLZSS_Greedy_ManualLoop_ProducesCorrectOutput()
        {
            // BinaryWriterEx.WriteLZSS uses a manual for-loop (no longer LINQ Sum).
            // Verify roundtrip preserves data with signed checksum (PAA mode).
            var data = new byte[4096];
            var rng = new Random(42);
            rng.NextBytes(data);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            using var reader = new BinaryReaderEx(ms);

            writer.WriteLZSS(data, true);
            ms.Position = 0;
            var result = reader.ReadLZSS((uint)data.Length, true);

            Assert.Equal(data, result);
        }

        [Fact]
        public void ReadCompressed_BelowThreshold_ReadsRaw()
        {
            // < 1024 bytes: ReadLZSS returns raw bytes, not compressed
            using var ms = new MemoryStream();
            using var writer = new BinaryWriterEx(ms, true);
            writer.Write(new byte[] { 1, 2, 3, 4, 5 });
            writer.Flush();
            ms.Position = 0;

            var reader = new BinaryReaderEx(ms);
            var result = reader.ReadLZSS(5, false);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, result);
        }
    }
}
