using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BIS.PAK.Models;
using Xunit;

namespace BIS.PAK.Test;

public class PakFileTest
{
    private static byte[] CreateMinimalPak()
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        // FORM header
        writer.Write("FORM"u8.ToArray());
        writer.Write(0); // placeholder size

        // PAC1 type
        writer.Write("PAC1"u8.ToArray());

        // HEAD chunk: 32 bytes
        writer.Write("HEAD"u8.ToArray());
        WriteInt32BE(writer, 32);
        writer.Write(new byte[32]); // all zeros

        // Build some file data: compress "Hello World!" with Zlib
        var rawData = "Hello World! This is a test file."u8.ToArray();
        byte[] compressedData;
        using (var compressedMs = new MemoryStream())
        {
            compressedMs.WriteByte(0x78); // Zlib CMF
            compressedMs.WriteByte(0x01); // Zlib FLG
            using (var deflate = new DeflateStream(compressedMs, CompressionMode.Compress, true))
                deflate.Write(rawData);
            compressedData = compressedMs.ToArray();
        }

        // DATA chunk
        writer.Write("DATA"u8.ToArray());
        WriteInt32BE(writer, compressedData.Length);
        var dataOffset = ms.Position;
        writer.Write(compressedData);

        // FILE chunk
        var fileChunkMs = new MemoryStream();
        var fileWriter = new BinaryWriter(fileChunkMs);

        fileWriter.Write(new byte[2]); // null padding
        fileWriter.Write(new byte[4]); // unknown

        // Root entry: a directory "test"
        fileWriter.Write((byte)0); // Directory type
        fileWriter.Write((byte)4); // name length
        fileWriter.Write("test"u8.ToArray());

        // Directory child: "test" has 1 child
        fileWriter.Write(1); // child count

        // File entry: "hello.txt"
        fileWriter.Write((byte)1); // File type
        fileWriter.Write((byte)9); // name length
        fileWriter.Write("hello.txt"u8.ToArray());

        // File metadata (24 bytes)
        fileWriter.Write(0); // binaryOffset (start of DATA)
        fileWriter.Write(compressedData.Length); // compressedSize
        fileWriter.Write(rawData.Length); // originalSize
        fileWriter.Write(0); // null padding
        WriteInt32BE(fileWriter, 0x106); // compressionType: Zlib
        fileWriter.Write(0); // unknown

        writer.Write("FILE"u8.ToArray());
        var fileChunkBytes = fileChunkMs.ToArray();
        WriteInt32BE(writer, fileChunkBytes.Length);
        writer.Write(fileChunkBytes);

        // Update FORM size
        var formSize = (int)(ms.Length - 8);
        ms.Seek(4, SeekOrigin.Begin);
        WriteInt32BE(writer, formSize);

        return ms.ToArray();
    }

    [Fact]
    public void Open_ValidPak_ParsesEntries()
    {
        var data = CreateMinimalPak();
        using var ms = new MemoryStream(data);
        using var pak = PakFile.Open(ms);

        Assert.NotEmpty(pak.Entries);
    }

    [Fact]
    public void Open_ValidPak_HasDirectoryAndFile()
    {
        var data = CreateMinimalPak();
        using var ms = new MemoryStream(data);
        using var pak = PakFile.Open(ms);

        var dirs = pak.Entries.Where(e => e.IsDirectory).ToList();
        var files = pak.Entries.Where(e => !e.IsDirectory).ToList();

        Assert.Single(dirs);
        Assert.Equal("test", dirs[0].FullPath);

        Assert.Single(files);
        Assert.Equal("test\\hello.txt", files[0].FullPath);
    }

    [Fact]
    public void Open_ValidPak_FileMetadataCorrect()
    {
        var data = CreateMinimalPak();
        using var ms = new MemoryStream(data);
        using var pak = PakFile.Open(ms);

        var file = pak.Entries.First(e => !e.IsDirectory);
        Assert.Equal("Hello World! This is a test file."u8.ToArray().Length, file.OriginalSize);
        Assert.Equal(PakCompressionType.Zlib, file.CompressionType);
    }

    [Fact]
    public void ReadEntryData_ReturnsDecompressedData()
    {
        var data = CreateMinimalPak();
        using var ms = new MemoryStream(data);
        using var pak = PakFile.Open(ms);

        var file = pak.Entries.First(e => !e.IsDirectory);
        var result = pak.ReadEntryData(file);

        var expected = "Hello World! This is a test file."u8.ToArray();
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Open_InvalidForm_Throws()
    {
        var data = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        using var ms = new MemoryStream(data);
        Assert.Throws<InvalidDataException>(() => PakFile.Open(ms));
    }

    [Fact]
    public void Open_FileRoundTrip()
    {
        var data = CreateMinimalPak();
        var tempFile = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(tempFile, data);
            using var pak = PakFile.Open(tempFile);

            Assert.Single(pak.Entries, e => !e.IsDirectory);

            var file = pak.Entries.First(e => !e.IsDirectory);
            var result = pak.ReadEntryData(file);
            var expected = "Hello World! This is a test file."u8.ToArray();
            Assert.Equal(expected, result);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Entries_AreSortedByReadOrder()
    {
        var data = CreateMinimalPak();
        using var ms = new MemoryStream(data);
        using var pak = PakFile.Open(ms);

        Assert.Equal(2, pak.Entries.Count);
        Assert.Equal(1, pak.Entries.Count(e => e.IsDirectory));
        Assert.Equal(1, pak.Entries.Count(e => !e.IsDirectory));
        Assert.Equal("test", pak.Entries[0].FullPath);
        Assert.Equal("test\\hello.txt", pak.Entries[1].FullPath);
    }

    [Fact]
    public void ExtractAll_WritesFilesToDisk()
    {
        var data = CreateMinimalPak();
        using var ms = new MemoryStream(data);
        using var pak = PakFile.Open(ms);

        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            pak.ExtractAll(tempDir);

            var extractedPath = Path.Combine(tempDir, "test", "hello.txt");
            Assert.True(File.Exists(extractedPath));

            var content = File.ReadAllText(extractedPath);
            Assert.Equal("Hello World! This is a test file.", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static void WriteInt32BE(BinaryWriter writer, int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        writer.Write(bytes);
    }
}
