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
    }
}

