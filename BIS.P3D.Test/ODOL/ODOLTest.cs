using System;
using System.IO;
using BIS.Core.Streams;
using Xunit;

namespace BIS.P3D.Test.ODOL
{
    public class ODOLTest
    {
        [Fact]
        public void Read_InvalidSignature_ThrowsFormatException()
        {
            var data = new byte[] { 0, 0, 0, 0 };
            using var stream = new MemoryStream(data);
            var reader = new BinaryReaderEx(stream);

            var odol = new BIS.P3D.ODOL.ODOL();
            Assert.Throws<FormatException>(() => odol.Read(reader));
        }

        [Fact]
        public void Read_TooShortStream_Throws()
        {
            var data = new byte[] { (byte)'O', (byte)'D' };
            using var stream = new MemoryStream(data);
            var reader = new BinaryReaderEx(stream);

            var odol = new BIS.P3D.ODOL.ODOL();
            Assert.ThrowsAny<Exception>(() => odol.Read(reader));
        }

        [Fact]
        public void Read_ExactSignatureOnly_Throws()
        {
            using var stream = new MemoryStream();
            var writer = new BinaryWriterEx(stream);
            writer.WriteAscii("ODOL", 4);
            writer.Flush();
            stream.Position = 0;

            var reader = new BinaryReaderEx(stream);
            var odol = new BIS.P3D.ODOL.ODOL();
            Assert.ThrowsAny<Exception>(() => odol.Read(reader));
        }

        [Fact]
        public void Constructor_DefaultValues_AreValid()
        {
            var odol = new BIS.P3D.ODOL.ODOL();
            Assert.NotNull(odol);
            Assert.Null(odol.Lods);
            Assert.Null(odol.ModelInfo);
        }
    }
}
