using System;
using System.IO;

namespace BIS.PBO
{
    /// <summary>
    /// Represents a file entry in a PBO whose content is held entirely in memory.
    /// Used when modifying existing files or adding new files to a PBO before saving.
    /// </summary>
    public class PBOFileInMemory : IPBOFileEntry
    {
        public string FileName { get; set; }
        public byte[] Data { get; set; }

        public string RawFileName => FileName;

        public int Size => Data.Length;

        public int TimeStamp =>
            (int)DateTime.UtcNow.Subtract(PBO.Epoch).TotalSeconds;

        public bool IsCompressed => false;

        public int DiskSize => Data.Length;

        public PBOFileInMemory(string fileName, byte[] data)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public Stream OpenRead() => new MemoryStream(Data, false);
    }
}
