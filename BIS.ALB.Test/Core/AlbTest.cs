using System;
using System.IO;
using BIS.Core.Streams;
using Xunit;

namespace BIS.ALB.Test.Core
{
    public class AlbTest
    {
        [Fact]
        public void Constructor_InvalidSignature_ThrowsFormatException()
        {
            using var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);
            writer.WriteAscii("XXXX", 4);
            writer.Write(0);
            writer.Flush();
            ms.Position = 0;

            var reader = new BinaryReaderEx(ms);
            Assert.Throws<FormatException>(() => new ALB1(reader));
        }

        [Fact]
        public void Constructor_EmptyStream_Throws()
        {
            using var ms = new MemoryStream();
            var reader = new BinaryReaderEx(ms);
            Assert.ThrowsAny<Exception>(() => new ALB1(reader));
        }

        [Fact]
        public void Constructor_NullReader_ThrowsNullReferenceException()
        {
            Assert.Throws<NullReferenceException>(() => new ALB1(default(BinaryReaderEx)));
        }

        [Fact]
        public void Constructor_TooShortStream_ThrowsEndOfStream()
        {
            using var ms = new MemoryStream(new byte[] { (byte)'A' });
            var reader = new BinaryReaderEx(ms);
            Assert.Throws<EndOfStreamException>(() => new ALB1(reader));
        }
    }
}
