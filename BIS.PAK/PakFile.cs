using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BIS.PAK.Models;

namespace BIS.PAK;

public class PakFile : IDisposable
{
    private readonly Stream _stream;
    private bool _disposed;

    public List<PakEntry> Entries { get; } = [];
    public long FileSize { get; }
    public int Version { get; private set; }

    private PakFile(Stream stream, long fileSize)
    {
        _stream = stream;
        FileSize = fileSize;
    }

    public static PakFile Open(string path)
    {
        var stream = File.OpenRead(path);
        try
        {
            var pak = new PakFile(stream, stream.Length);
            pak.Read();
            return pak;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static PakFile Open(Stream stream)
    {
        var pak = new PakFile(stream, stream.Length);
        pak.Read();
        return pak;
    }

    private void Read()
    {
        var reader = new BinaryReader(_stream, Encoding.ASCII, true);

        // FORM header
        var form = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (form != "FORM")
            throw new InvalidDataException($"Expected FORM header, got '{form}'");

        var formSize = ReadInt32BE(reader);

        var pac1 = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (pac1 != "PAC1")
            throw new InvalidDataException($"Expected PAC1 type, got '{pac1}'");

        // Read chunks
        long dataChunkOffset = 0;
        byte[] fileChunkData = null;

        while (_stream.Position < _stream.Length)
        {
            var chunkType = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkSize = ReadInt32BE(reader);

            switch (chunkType)
            {
                case "HEAD":
                    ReadHeadChunk(reader, chunkSize);
                    break;

                case "DATA":
                    dataChunkOffset = _stream.Position;
                    _stream.Seek(chunkSize, SeekOrigin.Current);
                    break;

                case "FILE":
                    fileChunkData = reader.ReadBytes(chunkSize);
                    break;

                default:
                    // Unknown chunk — skip
                    _stream.Seek(chunkSize, SeekOrigin.Current);
                    break;
            }
        }

        if (fileChunkData == null)
            throw new InvalidDataException("PAK file is missing FILE chunk.");

        if (dataChunkOffset == 0)
            throw new InvalidDataException("PAK file is missing DATA chunk.");

        ParseFileChunk(fileChunkData, dataChunkOffset);
    }

    private void ReadHeadChunk(BinaryReader reader, int chunkSize)
    {
        if (chunkSize < 28)
        {
            _stream.Seek(chunkSize, SeekOrigin.Current);
            return;
        }

        var readVersion = reader.ReadUInt32();
        _stream.Seek(-4, SeekOrigin.Current);

        if (readVersion == 0x00010003 || readVersion == 0x03000100)
        {
            Version = reader.ReadInt32();
            _stream.Seek(24, SeekOrigin.Current);
        }
        else
        {
            _stream.Seek(chunkSize, SeekOrigin.Current);
        }
    }

    private void ParseFileChunk(byte[] fileChunkData, long dataChunkOffset)
    {
        using var ms = new MemoryStream(fileChunkData);
        using var reader = new BinaryReader(ms);

        reader.ReadBytes(2); // skip 2 null bytes
        reader.ReadBytes(4); // skip 4 unknown bytes

        var entriesSize = fileChunkData.Length - ms.Position;
        var posEntries = ms.Position;

        while (ms.Position - posEntries < entriesSize)
        {
            var entryType = (PakEntryType)reader.ReadByte();
            var nameLength = reader.ReadByte();
            var nameBytes = reader.ReadBytes(nameLength);
            var name = Encoding.ASCII.GetString(nameBytes);

            if (entryType == PakEntryType.Directory)
            {
                ReadDirectoryEntries(reader, name, dataChunkOffset);
            }
            else
            {
                ReadFileEntry(reader, name, dataChunkOffset);
            }
        }
    }

    private void ReadDirectoryEntries(BinaryReader reader, string dirName, long dataChunkOffset)
    {
        Entries.Add(new PakEntry
        {
            FullPath = dirName,
            IsDirectory = true,
        });

        var stream = reader.BaseStream;
        if (stream.Position + 4 > stream.Length)
            throw new InvalidDataException($"Cannot read childCount for '{dirName}': stream position {stream.Position}, length {stream.Length}");

        var childCount = reader.ReadInt32();
        for (var i = 0; i < childCount; i++)
        {
            var entryType = (PakEntryType)reader.ReadByte();
            var nameLength = reader.ReadByte();
            var nameBytes = reader.ReadBytes(nameLength);
            var name = Encoding.ASCII.GetString(nameBytes);
            var fullPath = $"{dirName}\\{name}";

            if (entryType == PakEntryType.File)
            {
                ReadFileEntry(reader, fullPath, dataChunkOffset);
            }
            else
            {
                // Directory: add as entry and recurse
                Entries.Add(new PakEntry
                {
                    FullPath = fullPath,
                    IsDirectory = true,
                });
                ReadDirectoryEntries(reader, fullPath, dataChunkOffset);
            }
        }
    }

    private void ReadFileEntry(BinaryReader reader, string fullPath, long dataChunkOffset)
    {
        var binaryOffset = reader.ReadInt32();
        var compressedSize = reader.ReadInt32();
        var originalSize = reader.ReadInt32();
        reader.ReadBytes(4); // null padding
        var compressionTypeRaw = ReadInt32BE(reader);
        reader.ReadBytes(4); // unknown

        Entries.Add(new PakEntry
        {
            FullPath = fullPath,
            IsDirectory = false,
            BinaryOffset = binaryOffset,
            CompressedSize = compressedSize,
            OriginalSize = originalSize,
            CompressionType = (PakCompressionType)compressionTypeRaw,
            DataChunkOffset = dataChunkOffset,
        });
    }

    public byte[] ReadEntryData(PakEntry entry)
    {
        if (entry.IsDirectory)
            throw new InvalidOperationException("Cannot read data from a directory entry.");

        var buffer = new byte[entry.CompressedSize];
        lock (_stream)
        {
            _stream.Seek(entry.DataChunkOffset + entry.BinaryOffset, SeekOrigin.Begin);
            var read = 0;
            while (read < entry.CompressedSize)
            {
                var n = _stream.Read(buffer, read, entry.CompressedSize - read);
                if (n == 0)
                    throw new EndOfStreamException("Unexpected end of PAK data chunk.");
                read += n;
            }
        }

        if (entry.CompressionType == PakCompressionType.None)
            return buffer;

        if (entry.CompressionType == PakCompressionType.Zlib)
        {
            using var compressed = new MemoryStream(buffer, 2, buffer.Length - 2);
            using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
            using var result = new MemoryStream(entry.OriginalSize);
            deflate.CopyTo(result);
            return result.ToArray();
        }

        throw new NotSupportedException($"Unsupported compression type: {entry.CompressionType}");
    }

    public void ExtractAll(string outputDir, IProgress<double> progress = null)
    {
        var fileEntries = Entries.Where(e => !e.IsDirectory).ToList();
        var count = fileEntries.Count;
        var completed = 0;

        foreach (var entry in fileEntries)
        {
            var path = Path.Combine(outputDir, entry.FullPath.Replace('\\', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var data = ReadEntryData(entry);
            File.WriteAllBytes(path, data);

            completed++;
            progress?.Report((double)completed / count);
        }
    }

    private static int ReadInt32BE(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToInt32(bytes);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _stream.Dispose();
        }
    }
}
