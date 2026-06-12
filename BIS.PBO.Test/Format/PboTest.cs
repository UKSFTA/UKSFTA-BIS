using System;
using Xunit;
using BIS.PBO;
using BIS.Core.Streams;
using System.IO;
using System.Text;

namespace BIS.PBO.Test.Format
{
    public class PboTest
    {
        [Fact]
        public void FileEntry_Read_ShouldParseCorrectly()
        {
            var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);

            writer.WriteAsciiz("test_file.txt");
            writer.Write(0);
            writer.Write(1024);
            writer.Write(2048);
            writer.Write(12345);
            writer.Write(1024);

            ms.Position = 0;
            var reader = new BinaryReaderEx(ms);
            var entry = new FileEntry(reader);

            Assert.Equal("test_file.txt", entry.FileName);
            Assert.Equal(0, entry.CompressedMagic);
            Assert.Equal(1024, entry.UncompressedSize);
            Assert.Equal(2048, entry.StartOffset);
            Assert.Equal(12345, entry.TimeStamp);
            Assert.Equal(1024, entry.DataSize);
        }

        [Fact]
        public void PBO_ReadHeader_WithInvalidFileName_ShouldSanitize()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                using (var fs = File.OpenWrite(tempFile))
                {
                    var writer = new BinaryWriterEx(fs);

                    writer.WriteAsciiz("");
                    writer.Write(FileEntry.VersionMagic);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    writer.WriteAsciiz("prefix");
                    writer.WriteAsciiz("test_prefix");
                    writer.Write((byte)0);

                    writer.WriteAsciiz("bad*file.txt");
                    writer.Write(0);
                    writer.Write(10);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(10);

                    writer.WriteAsciiz("..\\secret.txt");
                    writer.Write(0);
                    writer.Write(20);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(20);

                    writer.WriteAsciiz("*?<>");
                    writer.Write(0);
                    writer.Write(4);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(4);

                    writer.WriteAsciiz("");
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    var dummyData = new byte[34];
                    dummyData[0] = 0x00;
                    dummyData[1] = (byte)'r';
                    dummyData[2] = (byte)'a';
                    dummyData[3] = (byte)'S';
                    writer.Write(dummyData);
                }

                var pbo = new PBO(tempFile);
                Assert.Equal(3, pbo.Files.Count);

                Assert.Equal("badfile.txt", pbo.Files[0].FileName);
                Assert.Equal("secret.txt", pbo.Files[1].FileName);

                Assert.Equal("_unknown\\_unknown_file0.paa", pbo.Files[2].FileName);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public void AddFile_WithValidNameAndData_CreatesEntry()
        {
            var pbo = new PBO();
            byte[] data = [1, 2, 3, 4];

            pbo.AddFile("test.bin", data);

            Assert.Single(pbo.Files);
            Assert.Equal("test.bin", pbo.Files[0].FileName);
            Assert.Equal(data, ((PBOFileInMemory)pbo.Files[0]).Data);
        }

        [Fact]
        public void AddFile_MultipleFiles_AllAdded()
        {
            var pbo = new PBO();
            pbo.AddFile("a.txt", [0]);
            pbo.AddFile("b.txt", [1]);

            Assert.Equal(2, pbo.Files.Count);
        }

        [Fact]
        public void AddFile_NullFileName_Throws()
        {
            var pbo = new PBO();

            Assert.Throws<ArgumentNullException>(() => pbo.AddFile(null!, [0]));
        }

        [Fact]
        public void AddFile_NullData_Throws()
        {
            var pbo = new PBO();

            Assert.Throws<ArgumentNullException>(() => pbo.AddFile("a.txt", null!));
        }

        [Fact]
        public void RemoveFile_ByName_RemovesAndReturnsTrue()
        {
            var pbo = new PBO();
            pbo.AddFile("keep.txt", [0]);
            pbo.AddFile("remove.txt", [1]);

            bool result = pbo.RemoveFile("remove.txt");

            Assert.True(result);
            Assert.Single(pbo.Files);
            Assert.Equal("keep.txt", pbo.Files[0].FileName);
        }

        [Fact]
        public void RemoveFile_ByName_CaseInsensitive()
        {
            var pbo = new PBO();
            pbo.AddFile("Test.Bin", [0]);

            Assert.True(pbo.RemoveFile("test.bin"));
            Assert.Empty(pbo.Files);
        }

        [Fact]
        public void RemoveFile_ByName_NormalizesForwardSlashes()
        {
            var pbo = new PBO();
            pbo.AddFile(@"folder\file.txt", [0]);

            Assert.True(pbo.RemoveFile("folder/file.txt"));
            Assert.Empty(pbo.Files);
        }

        [Fact]
        public void RemoveFile_ByName_NotFound_ReturnsFalse()
        {
            var pbo = new PBO();
            pbo.AddFile("a.txt", [0]);

            Assert.False(pbo.RemoveFile("nonexistent.txt"));
            Assert.Single(pbo.Files);
        }

        [Fact]
        public void RemoveFile_ByIndex_RemovesAtPosition()
        {
            var pbo = new PBO();
            pbo.AddFile("a.txt", [0]);
            pbo.AddFile("b.txt", [1]);

            pbo.RemoveFile(0);

            Assert.Single(pbo.Files);
            Assert.Equal("b.txt", pbo.Files[0].FileName);
        }

        [Fact]
        public void RemoveFile_ByIndex_OutOfRange_Throws()
        {
            var pbo = new PBO();

            Assert.Throws<ArgumentOutOfRangeException>(() => pbo.RemoveFile(0));
        }

        [Fact]
        public void FindFile_ReturnsMatchingEntry()
        {
            var pbo = new PBO();
            pbo.AddFile("target.dll", [0xBB]);

            var result = pbo.FindFile("target.dll");

            Assert.NotNull(result);
            Assert.Equal("target.dll", result.FileName);
        }

        [Fact]
        public void FindFile_CaseInsensitive()
        {
            var pbo = new PBO();
            pbo.AddFile("UpperCase.Bin", [0]);

            Assert.NotNull(pbo.FindFile("uppercase.bin"));
        }

        [Fact]
        public void FindFile_NormalizesForwardSlashes()
        {
            var pbo = new PBO();
            pbo.AddFile(@"a\b\c.paa", [0]);

            Assert.NotNull(pbo.FindFile("a/b/c.paa"));
        }

        [Fact]
        public void FindFile_NotFound_ReturnsNull()
        {
            var pbo = new PBO();
            pbo.AddFile("a.txt", [0]);

            Assert.Null(pbo.FindFile("missing.txt"));
        }

        [Fact]
        public void FindFile_EmptyPBO_ReturnsNull()
        {
            var pbo = new PBO();

            Assert.Null(pbo.FindFile("anything.paa"));
        }

        [Fact]
        public void GetFile_ReturnsMatchingEntry()
        {
            var pbo = new PBO();
            pbo.AddFile("config.bin", [0]);

            var result = pbo.GetFile("config.bin");

            Assert.NotNull(result);
            Assert.Equal("config.bin", result.FileName);
        }

        [Fact]
        public void GetFile_NotFound_ThrowsKeyNotFound()
        {
            var pbo = new PBO();

            Assert.Throws<KeyNotFoundException>(() => pbo.GetFile("missing.bin"));
        }

        [Fact]
        public void AddFileAndSave_Roundtrip_PreservesData()
        {
            var pbo = new PBO();
            byte[] originalData = [0xDE, 0xAD, 0xBE, 0xEF];
            pbo.AddFile("payload.dll", originalData);

            var tempFile = Path.GetTempFileName();
            try
            {
                pbo.SaveTo(tempFile);

                var loaded = new PBO(tempFile);
                var entry = loaded.FindFile("payload.dll");
                Assert.NotNull(entry);
                Assert.Equal(originalData.Length, entry.Size);

                using var stream = entry.OpenRead();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                Assert.Equal(originalData, ms.ToArray());
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
    }
}
