using System;
using System.IO;
using BIS.Core.Streams;
using Xunit;

namespace BIS.WRP.Test
{
    public class WrpTest
    {
        [Fact]
        public void Constructor_Default_IsNotNull()
        {
            var wrp = new AnyWrp();
            Assert.NotNull(wrp);
        }

        [Fact]
        public void Read_UnknownSignature_ThrowsInvalidOperationException()
        {
            using var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);
            writer.WriteAscii("XXXX", 4);
            writer.Flush();
            ms.Position = 0;

            var wrp = new AnyWrp();
            Assert.Throws<InvalidOperationException>(() => wrp.Read(new BinaryReaderEx(ms)));
        }

        [Fact]
        public void Read_EmptyStream_Throws()
        {
            using var ms = new MemoryStream();
            var reader = new BinaryReaderEx(ms);

            var wrp = new AnyWrp();
            Assert.ThrowsAny<Exception>(() => wrp.Read(reader));
        }

        [Fact]
        public void Read_OPRW_TooShort_Throws()
        {
            // OPRW signature without enough data for version check
            using var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);
            writer.WriteAscii("OPRW", 4);
            writer.Write(3); // version < 10 should throw NotSupportedException
            writer.Flush();
            ms.Position = 0;

            var wrp = new AnyWrp();
            Assert.Throws<NotSupportedException>(() => wrp.Read(new BinaryReaderEx(ms)));
        }

        [Fact]
        public void Read_8WVR_TooShort_Throws()
        {
            // 8WVR signature (EditableWrp) without enough data
            using var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);
            writer.WriteAscii("8WVR", 4);
            writer.Flush();
            ms.Position = 0;

            var wrp = new AnyWrp();
            Assert.ThrowsAny<Exception>(() => wrp.Read(new BinaryReaderEx(ms)));
        }

        [Fact]
        public void GetEditableWrp_BeforeRead_ReturnsNull()
        {
            var wrp = new AnyWrp();
            var editable = wrp.GetEditableWrp();
            Assert.Null(editable);
        }
    }
}
