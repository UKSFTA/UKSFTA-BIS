using System;
using System.IO;
using System.Linq;
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

        [Fact]
        public void Constructor_ValidMinimal_NoTagsNoClasses()
        {
            // Build a minimal valid ALB1 with no tags, no classes, no entries
            using var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);
            writer.WriteAscii("ALB1", 4);  // signature
            writer.Write(new byte[15]);     // unknown header
            writer.Write(0);                // 0 tags
            writer.Write(new byte[3]);      // unknown after tags
            writer.Write(0);                // 0 classes
            writer.Write(new byte[6]);      // unknown after classes
            // No entries
            writer.Flush();
            ms.Position = 0;

            var alb = new ALB1(new BinaryReaderEx(ms));
            Assert.NotNull(alb);

            // ToString should produce empty output for no entries
            var output = alb.ToString();
            Assert.Equal("", output.Trim());
        }

        [Fact]
        public void Constructor_WithTag_CreatesEntry()
        {
            // Build ALB1 with 1 tag and 1 entry (string value)
            using var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);
            writer.WriteAscii("ALB1", 4);  // signature
            writer.Write(new byte[15]);     // unknown header

            // 1 tag: length-prefixed ascii (ushort length + data)
            writer.Write(1);                // nTags
            writer.Write((ushort)1);        // tagID = 1
            var tagName = "test_tag";
            writer.Write((ushort)tagName.Length);
            writer.Write(tagName.ToCharArray());

            writer.Write(new byte[3]);      // unknown after tags

            // 0 classes
            writer.Write(0);

            writer.Write(new byte[6]);      // unknown after classes

            // 1 entry: tagID=1, type=String(11), value="hello"
            writer.Write((short)1);         // TagID
            writer.Write((byte)11);         // datatype: String
            var value = "hello";
            writer.Write((ushort)value.Length);
            writer.Write(value.ToCharArray());

            writer.Flush();
            ms.Position = 0;

            var alb = new ALB1(new BinaryReaderEx(ms));
            Assert.NotNull(alb);

            var output = alb.ToString();
            Assert.Contains("test_tag", output);
            Assert.Contains("hello", output);
        }

        [Fact]
        public void Constructor_WithIntegerEntry_ParsesCorrectly()
        {
            using var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);
            writer.WriteAscii("ALB1", 4);
            writer.Write(new byte[15]);
            writer.Write(1);                // 1 tag
            writer.Write((ushort)5);        // tagID = 5
            var intTagName = "count";
            writer.Write((ushort)intTagName.Length);
            writer.Write(intTagName.ToCharArray());
            writer.Write(new byte[3]);
            writer.Write(0);                // 0 classes
            writer.Write(new byte[6]);

            // 1 entry: tagID=5, type=Integer(5), value=42
            writer.Write((short)5);         // TagID
            writer.Write((byte)5);          // datatype: Integer
            writer.Write(42);               // value

            writer.Flush();
            ms.Position = 0;

            var alb = new ALB1(new BinaryReaderEx(ms));
            var output = alb.ToString();
            Assert.Contains("count", output);
            Assert.Contains("42", output);
        }
    }
}
