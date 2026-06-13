using System;
using System.IO;
using Xunit;

namespace BIS.RTM.Test
{
    public class RtmTest
    {
        [Fact]
        public void Constructor_InvalidSignature_ThrowsFormatException()
        {
            using var ms = new MemoryStream();
            var writer = new BIS.Core.Streams.BinaryWriterEx(ms);
            writer.WriteAscii("INVALID!", 8);
            writer.Flush();
            ms.Position = 0;

            Assert.Throws<FormatException>(() => new RTM(ms));
        }

        [Fact]
        public void Constructor_EmptyStream_Throws()
        {
            using var ms = new MemoryStream();
            Assert.ThrowsAny<Exception>(() => new RTM(ms));
        }

        [Fact]
        public void Constructor_PartialSignature_Throws()
        {
            using var ms = new MemoryStream();
            var writer = new System.IO.BinaryWriter(ms);
            writer.Write("RTM_"u8);
            writer.Flush();
            ms.Position = 0;

            Assert.ThrowsAny<Exception>(() => new RTM(ms));
        }

        [Fact]
        public void Constructor_ValidMinimal_CreatesInstance()
        {
            using var ms = new MemoryStream();
            var writer = new BIS.Core.Streams.BinaryWriterEx(ms);
            writer.WriteAscii("RTM_0101", 8);
            writer.Write(0.0f); // displacement.x
            writer.Write(0.0f); // displacement.y
            writer.Write(0.0f); // displacement.z
            writer.Write(0);    // 0 frames
            writer.Write(0);    // 0 bones
            writer.Flush();
            ms.Position = 0;

            var rtm = new RTM(ms);
            Assert.NotNull(rtm);
            Assert.Empty(rtm.BoneNames);
            Assert.Empty(rtm.FrameTimes);
        }
    }
}
