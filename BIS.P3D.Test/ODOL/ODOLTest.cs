using System;
using System.IO;
using BIS.Core.Streams;
using BIS.P3D;
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

        [Fact]
        public void Dependencies_FromBarbedwire_ReturnsNonEmpty()
        {
            var p3dPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..", "_testdata", "p3d", "Barbedwire.p3d");
            if (!File.Exists(p3dPath))
                return; // test data not available

            var odol = new BIS.P3D.ODOL.ODOL();
            using var stream = File.OpenRead(p3dPath);
            var reader = new BinaryReaderEx(stream);
            odol.Read(reader);

            var deps = odol.Dependencies();
            Assert.NotEmpty(deps);
            Assert.All(deps, d => Assert.False(string.IsNullOrWhiteSpace(d)));
        }
    }
}
