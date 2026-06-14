using System.IO;

namespace BIS.PAK.Models;

public class PakEntry
{
    public string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long BinaryOffset { get; init; }
    public int CompressedSize { get; init; }
    public int OriginalSize { get; init; }
    public PakCompressionType CompressionType { get; init; }
    public long DataChunkOffset { get; set; }

    public string Name => Path.GetFileName(FullPath);
    public string DirectoryName => Path.GetDirectoryName(FullPath)?.Replace('\\', '/') ?? "";

    public override string ToString() =>
        IsDirectory
            ? $"[DIR]  {FullPath}"
            : $"[FILE] {FullPath}  ({OriginalSize} bytes, compressed: {CompressedSize}, type: {CompressionType})";
}
