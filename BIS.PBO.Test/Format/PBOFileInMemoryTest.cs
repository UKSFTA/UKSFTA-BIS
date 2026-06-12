using System;
using System.IO;
using System.Text;
using Xunit;
using BIS.Core.Streams;

namespace BIS.PBO.Test.Format
{
    public class PBOFileInMemoryTest
    {
        [Fact]
        public void Constructor_ShouldSetProperties()
        {
            var data = Encoding.ASCII.GetBytes("hello");
            var entry = new PBOFileInMemory("test.txt", data);

            Assert.Equal("test.txt", entry.FileName);
            Assert.Equal("test.txt", entry.RawFileName);
            Assert.Equal(5, entry.Size);
            Assert.Equal(5, entry.DiskSize);
            Assert.False(entry.IsCompressed);
        }

        [Fact]
        public void Constructor_ShouldThrowOnNullFileName()
        {
            Assert.Throws<ArgumentNullException>(() => new PBOFileInMemory(null, new byte[1]));
        }

        [Fact]
        public void Constructor_ShouldThrowOnNullData()
        {
            Assert.Throws<ArgumentNullException>(() => new PBOFileInMemory("test.txt", null));
        }

        [Fact]
        public void OpenRead_ShouldReturnContent()
        {
            var data = Encoding.ASCII.GetBytes("hello world");
            var entry = new PBOFileInMemory("test.txt", data);

            using var stream = entry.OpenRead();
            var reader = new StreamReader(stream);
            Assert.Equal("hello world", reader.ReadToEnd());
        }

        [Fact]
        public void TimeStamp_ShouldBeReasonable()
        {
            var entry = new PBOFileInMemory("test.txt", new byte[1]);
            // PBO epoch is 1970-01-01, so timestamp should be large (years of seconds)
            Assert.True(entry.TimeStamp > 1_500_000_000,
                $"Timestamp {entry.TimeStamp} seems unreasonably small");
        }

        [Fact]
        public void SaveTo_Roundtrip_WithPBOFileInMemory()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                // ── Create PBO with PBOFileInMemory entries ──
                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "testmod"));

                var file1 = new PBOFileInMemory(@"config.cpp", Encoding.ASCII.GetBytes("class CfgPatches {};"));
                var file2 = new PBOFileInMemory(@"data\texture.paa", new byte[] { 0x00, (byte)'r', (byte)'a', (byte)'S' });
                var file3 = new PBOFileInMemory(@"model\mesh.p3d", Encoding.ASCII.GetBytes("ODOLformat"));

                pbo.Files.Add(file1);
                pbo.Files.Add(file2);
                pbo.Files.Add(file3);

                // ── Save ──
                pbo.SaveTo(tempFile);
                Assert.True(File.Exists(tempFile));
                var savedSize = new FileInfo(tempFile).Length;
                Assert.True(savedSize > 100, $"Saved PBO too small: {savedSize} bytes");

                // ── Re-open and verify ──
                var reopened = new PBO(tempFile);
                Assert.Equal("testmod", reopened.Prefix);
                Assert.Equal(3, reopened.Files.Count);

                // Verify file order preserved
                Assert.Equal(@"config.cpp", reopened.Files[0].FileName);
                Assert.Equal(@"data\texture.paa", reopened.Files[1].FileName);
                Assert.Equal(@"model\mesh.p3d", reopened.Files[2].FileName);

                // Verify content
                using (var stream = reopened.Files[0].OpenRead())
                using (var reader = new StreamReader(stream))
                {
                    Assert.Equal("class CfgPatches {};", reader.ReadToEnd());
                }

                using (var stream = reopened.Files[1].OpenRead())
                {
                    byte[] buf = new byte[4];
                    stream.ReadExactly(buf, 0, 4);
                    Assert.Equal(new byte[] { 0x00, (byte)'r', (byte)'a', (byte)'S' }, buf);
                }

                using (var stream = reopened.Files[2].OpenRead())
                using (var reader = new StreamReader(stream))
                {
                    Assert.Equal("ODOLformat", reader.ReadToEnd());
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public void SaveTo_Roundtrip_MixedEntries()
        {
            // Test that PBOFileInMemory works alongside PBOFileToAdd in the same PBO
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var tempFile = Path.GetTempFileName();
            try
            {
                Directory.CreateDirectory(dir);
                var diskFilePath = Path.Combine(dir, "disk_file.bin");
                File.WriteAllBytes(diskFilePath, Encoding.ASCII.GetBytes("from disk"));

                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "mixed"));

                // Add via PBOFileToAdd (disk file)
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(diskFilePath), @"data\disk_file.bin"));

                // Add via PBOFileInMemory (in-memory)
                pbo.Files.Add(new PBOFileInMemory(@"data\mem_file.bin", Encoding.ASCII.GetBytes("from memory")));

                pbo.SaveTo(tempFile);

                var reopened = new PBO(tempFile);
                Assert.Equal(2, reopened.Files.Count);

                using (var stream = reopened.Files[0].OpenRead())
                using (var reader = new StreamReader(stream))
                    Assert.Equal("from disk", reader.ReadToEnd());

                using (var stream = reopened.Files[1].OpenRead())
                using (var reader = new StreamReader(stream))
                    Assert.Equal("from memory", reader.ReadToEnd());
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void SaveTo_ReplaceEntry_ThenSave()
        {
            // Simulate replacing a file inside a PBO
            var tempFile = Path.GetTempFileName();
            try
            {
                // Create initial PBO
                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test"));
                pbo.Files.Add(new PBOFileInMemory(@"data\file.bin", Encoding.ASCII.GetBytes("original")));
                pbo.SaveTo(tempFile);

                // Re-open and modify
                var reopened = new PBO(tempFile);
                Assert.Single(reopened.Files);
                Assert.Equal("original", ReadAllText(reopened.Files[0]));

                // Replace with PBOFileInMemory
                reopened.Files[0] = new PBOFileInMemory(
                    reopened.Files[0].FileName,
                    Encoding.ASCII.GetBytes("modified"));

                reopened.SaveTo(tempFile);

                // Re-open again and verify
                var modified = new PBO(tempFile);
                Assert.Single(modified.Files);
                Assert.Equal("modified", ReadAllText(modified.Files[0]));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private static string ReadAllText(IPBOFileEntry entry)
        {
            using var stream = entry.OpenRead();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
