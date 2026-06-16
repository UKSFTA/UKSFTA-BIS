using System;
using System.IO;
using System.Linq;
using BIS.Core.Math;
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

        [Fact]
        public void WriteToFile_Roundtrip_PreservesData()
        {
            // Build a minimal RTM with 2 bones and 1 frame
            using var ms = new MemoryStream();
            var writer = new BIS.Core.Streams.BinaryWriterEx(ms);
            writer.WriteAscii("RTM_0101", 8);

            // Displacement (Vector3P = 3 floats)
            writer.Write(1.0f); // x
            writer.Write(2.0f); // y
            writer.Write(3.0f); // z

            writer.Write(1);    // 1 frame
            writer.Write(2);    // 2 bones

            // Bone names (32 bytes each, null-padded)
            writer.WriteAscii("bone_a", 32);
            writer.WriteAscii("bone_b", 32);

            // Frame 0
            writer.Write(0.0f); // frame time

            // Frame 0, bone_a: 4x4 transform matrix (16 floats)
            for (int i = 0; i < 16; i++)
                writer.Write((float)i);
            writer.WriteAscii("bone_a", 32); // redundant bone name

            // Frame 0, bone_b: 4x4 transform matrix (16 floats)
            for (int i = 0; i < 16; i++)
                writer.Write((float)(i + 16));
            writer.WriteAscii("bone_b", 32); // redundant bone name

            writer.Flush();

            // Read RTM from constructed binary
            ms.Position = 0;
            var rtm = new RTM(ms);
            Assert.NotNull(rtm);
            Assert.Equal(2, rtm.BoneNames.Length);
            Assert.Equal("bone_a", rtm.BoneNames[0].TrimEnd('\0'));
            Assert.Equal("bone_b", rtm.BoneNames[1].TrimEnd('\0'));
            Assert.Single(rtm.FrameTimes);
            Assert.Equal(1, rtm.FrameTransforms.GetLength(0));
            Assert.Equal(2, rtm.FrameTransforms.GetLength(1));

            // Write to temp file
            var tempPath = Path.GetTempFileName() + ".rtm";
            try
            {
                rtm.WriteToFile(tempPath);
                Assert.True(File.Exists(tempPath));

                // Read it back
                var rtm2 = new RTM(tempPath);
                Assert.Equal(rtm.BoneNames.Length, rtm2.BoneNames.Length);
                Assert.Equal(rtm.BoneNames[0].TrimEnd('\0'), rtm2.BoneNames[0].TrimEnd('\0'));
                Assert.Equal(rtm.BoneNames[1].TrimEnd('\0'), rtm2.BoneNames[1].TrimEnd('\0'));
                Assert.Equal(rtm.FrameTimes.Length, rtm2.FrameTimes.Length);
                Assert.Equal(rtm.FrameTimes[0], rtm2.FrameTimes[0]);
                Assert.Equal(rtm.Displacement.X, rtm2.Displacement.X);
                Assert.Equal(rtm.Displacement.Y, rtm2.Displacement.Y);
                Assert.Equal(rtm.Displacement.Z, rtm2.Displacement.Z);

                // Verify frame transform data
                for (int b = 0; b < 2; b++)
                {
                    var expected = rtm.FrameTransforms[0, b].Matrix;
                    var actual = rtm2.FrameTransforms[0, b].Matrix;
                    Assert.Equal(expected, actual);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        [Fact]
        public void WriteToFile_EmptyAnimation_Roundtrips()
        {
            using var ms = new MemoryStream();
            var writer = new BIS.Core.Streams.BinaryWriterEx(ms);
            writer.WriteAscii("RTM_0101", 8);
            writer.Write(0.0f); writer.Write(0.0f); writer.Write(0.0f); // displacement
            writer.Write(0); // 0 frames
            writer.Write(0); // 0 bones
            writer.Flush();
            ms.Position = 0;

            var rtm = new RTM(ms);
            var tempPath = Path.GetTempFileName() + ".rtm";
            try
            {
                rtm.WriteToFile(tempPath);
                var rtm2 = new RTM(tempPath);
                Assert.Empty(rtm2.BoneNames);
                Assert.Empty(rtm2.FrameTimes);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }
}
