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

            // Write sample FileEntry: FileName, Magic, UncompressedSize, StartOffset, TimeStamp, DataSize
            writer.WriteAsciiz("test_file.txt");
            writer.Write(0); // CompressedMagic
            writer.Write(1024); // UncompressedSize
            writer.Write(2048); // StartOffset
            writer.Write(12345); // TimeStamp
            writer.Write(1024); // DataSize

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
                    
                    // Write Version Entry
                    writer.WriteAsciiz("");
                    writer.Write(FileEntry.VersionMagic);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    
                    // Write Properties (prefix)
                    writer.WriteAsciiz("prefix");
                    writer.WriteAsciiz("test_prefix");
                    writer.Write((byte)0);

                    // Write bad entry 1: asterisks
                    writer.WriteAsciiz("bad*file.txt");
                    writer.Write(0);
                    writer.Write(10);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(10);

                    // Write bad entry 2: directory traversal
                    writer.WriteAsciiz("..\\secret.txt");
                    writer.Write(0);
                    writer.Write(20);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(20);

                    // Write end entry
                    writer.WriteAsciiz("");
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    
                    // Write dummy data
                    writer.Write(new byte[30]);
                }

                var pbo = new PBO(tempFile);
                Assert.Equal(2, pbo.Files.Count);
                Assert.Equal("_unknown\\_unknown_file0.bin", pbo.Files[0].FileName);
                Assert.Equal("_unknown\\_unknown_file1.bin", pbo.Files[1].FileName);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
    }
}

