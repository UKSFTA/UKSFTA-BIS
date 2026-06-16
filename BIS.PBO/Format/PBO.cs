using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using BIS.Core;
using BIS.Core.Compression;
using BIS.Core.Streams;

namespace BIS.PBO
{
    public class PBO : IDisposable
    {
        public static DateTime Epoch { get; } = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        private static FileEntry VersionEntry;
        private static FileEntry EmptyEntry;

        private FileStream pboFileStream;

        public string PBOFilePath { get; private set; }

        public FileStream PBOFileStream
        {
            get
            {
                pboFileStream = pboFileStream ?? File.OpenRead(PBOFilePath);
                return pboFileStream;
            }
        }
        public List<IPBOFileEntry> Files { get; } = new List<IPBOFileEntry>();

        [Obsolete]
        public LinkedList<FileEntry> FileEntries { get; } = new LinkedList<FileEntry>();

        [Obsolete]
        public LinkedList<string> Properties { get; } = new LinkedList<string>();
        public List<KeyValuePair<string, string>> PropertiesPairs { get; } = new List<KeyValuePair<string, string>>();
        public int DataOffset { get; protected set; }
        public string Prefix { get; protected set; }
        public string FileName => Path.GetFileName(PBOFilePath);

        static PBO()
        {
            VersionEntry = new FileEntry
            {
                CompressedMagic = FileEntry.VersionMagic,
                FileName = ""
            };

            EmptyEntry = new FileEntry();
        }

        public PBO(string fileName, bool keepStreamOpen = false)
        {
            PBOFilePath = fileName;
            var input = new BinaryReaderEx(PBOFileStream);
            ReadHeader(input);
            if (!keepStreamOpen)
            {
                pboFileStream.Close();
                pboFileStream = null;
            }
        }

        public PBO()
        {

        }

        private void ReadHeader(BinaryReaderEx input)
        {
            int curOffset = 0;
            int unknownCounter = 0;
            FileEntry pboEntry;
#pragma warning disable CS0612 // Le type ou le membre est obsolète
            do
            {
                pboEntry = new FileEntry(input)
                {
                    StartOffset = curOffset
                };

                curOffset += pboEntry.DataSize;

                if (pboEntry.IsVersion)
                {
                    string name;
                    string value;
                    do
                    {
                        name = input.ReadAsciiz();
                        if (name == "") break;
                        Properties.AddLast(name);

                        value = input.ReadAsciiz();
                        Properties.AddLast(value);

                        PropertiesPairs.Add(new KeyValuePair<string, string>(name, value));

                        if (name == "prefix")
                            Prefix = value;
                    }
                    while (name != "");

                    if (Properties.Count % 2 != 0)
                        throw new Exception("metaData count is not even.");
                }
                else if (pboEntry.FileName != "")
                {
                    pboEntry.FileName = SanitizeFileName(pboEntry.FileName, ref unknownCounter);
                    FileEntries.AddLast(pboEntry);
                    Files.Add(new PBOFileExisting(pboEntry, this));
                }
            }
            while (pboEntry.FileName != "" || Files.Count == 0);
#pragma warning restore CS0612 // Le type ou le membre est obsolète

            DataOffset = (int)input.Position;

            if (Prefix == null)
            {
                Prefix = Path.GetFileNameWithoutExtension(PBOFilePath);
            }

            // Post-process to guess extensions for completely unknown files
#pragma warning disable CS0612 // FileEntries is obsolete but needed for mutable FileName access
            foreach (var entry in FileEntries)
            {
                if (entry.FileName.StartsWith("_unknown\\_unknown_file") && !entry.FileName.Contains("."))
                {
                    entry.FileName += GuessExtension(entry);
                }
            }
#pragma warning restore CS0612
        }

        private string GuessExtension(FileEntry entry)
        {
            if (entry.DataSize < 4 || pboFileStream == null) return ".bin";

            long oldPos = pboFileStream.Position;
            try
            {
                pboFileStream.Position = DataOffset + entry.StartOffset;
                byte[] magic = new byte[4];
                pboFileStream.ReadExactly(magic, 0, 4);

                // raP\0 = binarized config/rvmat
                if (magic[0] == 'r' && magic[1] == 'a' && magic[2] == 'P' && magic[3] == '\0')
                    return ".bin";

                // PAA magic varies, but typically starts with a short (e.g. 0xFF01 or \x00raS)
                if (magic[0] == 0x00 && magic[1] == 'r' && magic[2] == 'a' && magic[3] == 'S')
                    return ".paa";
                if (magic[1] == 0x00 && magic[2] == 0x00 && magic[3] == 0x00) // Generic header hint
                    return ".paa";

                // SQF / text files usually start with printable characters
                bool isText = true;
                for (int i = 0; i < 4; i++)
                {
                    if (magic[i] == 0 || magic[i] > 127) isText = false;
                }

                if (isText) return ".sqf";

                return ".bin";
            }
            catch
            {
                return ".bin";
            }
            finally
            {
                pboFileStream.Position = oldPos;
            }
        }

        private static string SanitizeFileName(string fileName, ref int unknownCounter)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return $"_unknown\\_unknown_file{unknownCounter++}";

            var clean = new System.Text.StringBuilder(fileName.Length);

            foreach (char c in fileName)
            {
                // Strip control characters, forbidden Windows path characters, and non-ASCII characters (often used for obfuscation)
                if (!char.IsControl(c) && c <= 127 && c != '*' && c != '?' && c != '<' && c != '>' && c != '|' && c != ':' && c != '"')
                {
                    clean.Append(c);
                }
            }

            string recovered = clean.ToString().Replace("..", "");

            // Normalize slashes
            recovered = recovered.Replace("/", "\\");

            // Collapse multiple spaces around slashes and duplicate slashes
            string oldRecovered;
            do
            {
                oldRecovered = recovered;
                recovered = recovered.Replace("\\\\", "\\").Replace(" \\", "\\").Replace("\\ ", "\\");
            } while (oldRecovered != recovered);

            // Trim leading/trailing slashes, spaces, and stray dots
            recovered = recovered.Trim(' ', '\\', '.');

            if (string.IsNullOrWhiteSpace(recovered))
            {
                return $"_unknown\\_unknown_file{unknownCounter++}";
            }

            return recovered;
        }

        protected virtual byte[] GetFileData(FileEntry entry)
        {
            byte[] bytes;
            lock (this)
            {
                PBOFileStream.Position = DataOffset + entry.StartOffset;
                if (entry.CompressedMagic == 0)
                {
                    bytes = new byte[entry.DataSize];
                    PBOFileStream.ReadExactly(bytes, 0, entry.DataSize);
                }
                else if (entry.IsCompressed)
                {
                    var br = new BinaryReaderEx(PBOFileStream);
                    bytes = br.ReadLZSS((uint)entry.UncompressedSize);
                }
                else if (entry.CompressedMagic == FileEntry.EncryptionMagic)
                {
                    bytes = new byte[entry.DataSize];
                    PBOFileStream.ReadExactly(bytes, 0, entry.DataSize);
                }
                else
                {
                    throw new Exception($"Unexpected packingMethod: 0x{entry.CompressedMagic:X8}");
                }
            }

            return bytes;
        }

        [Obsolete]
        public void ExtractFile(FileEntry entry, string dst)
        {
            ExtractFiles(Methods.Yield(entry), dst);
        }

        [Obsolete]
        public void ExtractFiles(IEnumerable<FileEntry> entries, string dst, bool keepStreamOpen = false)
        {
            foreach (var entry in entries.OrderBy(e => e.StartOffset))
            {
                if (entry.DataSize <= 0) continue;
                string path = Path.Combine(dst, entry.FileName.Replace('\\', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, GetFileData(entry));
            }

            if (!keepStreamOpen)
            {
                pboFileStream.Close();
                pboFileStream = null;
            }
        }

        public void ExtractFiles(IEnumerable<IPBOFileEntry> entries, string target)
        {
            foreach (var entry in entries)
            {
                var path = Path.Combine(target, entry.FileName.Replace('\\', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(Path.GetDirectoryName(path));

                using (var targetFile = File.Create(path))
                {
                    using (var source = entry.OpenRead())
                    {
                        source.CopyTo(targetFile);
                    }
                }
            }
        }

        public void ExtractAllFiles(string directory)
        {
            ExtractFiles(Files, Path.Combine(directory, Prefix));
        }

        public MemoryStream GetFileEntryStream(FileEntry entry)
        {
            return new MemoryStream(GetFileData(entry), false);
        }

        public IEnumerable<MemoryStream> GetFileEntryStreams(IEnumerable<FileEntry> entries, bool keepStreamOpen = false)
        {
            foreach (var entry in entries)
            {
                if (entry.DataSize <= 0) continue;
                yield return new MemoryStream(GetFileData(entry), false);
            }

            if (!keepStreamOpen)
            {
                pboFileStream.Close();
                pboFileStream = null;
            }
        }

        private static void WriteBasicHeader(BinaryWriterEx output, IEnumerable<FileEntry> fileEntries)
        {
            foreach (var entry in fileEntries)
            {
                entry.Write(output);
            }

            EmptyEntry.Write(output);
        }

        private static void WriteProperties(BinaryWriterEx output, IEnumerable<string> properties)
        {
            //create starting entry
            VersionEntry.Write(output);

            foreach (var e in properties)
            {
                output.WriteAsciiz(e);
            }
            output.Write((byte)0); //empty string
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(PBOFilePath))
            {
                throw new InvalidOperationException("PBO is not bound to a file, please use SaveTo() instead.");
            }
            SaveTo(PBOFilePath);
        }

        public void SaveTo(string targetFile, bool compress = false)
        {
            if (PBOFilePath == null)
            {
                SaveToInternal(targetFile, compress, true);
                PBOFilePath = targetFile;
                return;
            }
            if (string.Equals(Path.GetFullPath(targetFile), Path.GetFullPath(PBOFilePath), StringComparison.OrdinalIgnoreCase))
            {
                var temp = Path.GetTempFileName();
                SaveToInternal(temp, compress, true);
                if (pboFileStream != null)
                {
                    pboFileStream.Close();
                    pboFileStream = null;
                }
                File.Copy(temp, targetFile, true);
                return;
            }
            SaveToInternal(targetFile, compress, false);
        }

        private static byte[] CompressLZSS(byte[] rawData)
        {
            // LZSS compression threshold used by PBO format: skip files under 1024 bytes
            if (rawData.Length < 1024)
                return rawData;

            using (var ms = new MemoryStream())
            {
                using (var lzss = new LzssStream(ms, CompressionMode.Compress, true))
                {
                    lzss.Write(rawData, 0, rawData.Length);
                }
                // Append unsigned byte checksum (matching LZSS.ReadLZSS)
                var csum = rawData.Sum(b => (int)(uint)b);
                ms.Write(BitConverter.GetBytes(csum), 0, 4);
                return ms.ToArray();
            }
        }

        private void SaveToInternal(string targetFile, bool compress, bool isReplaceSelf)
        {
            var fileData = new List<(byte[] Data, int UncompressedSize)>();
            var entries = new List<FileEntry>();

            foreach (var file in Files)
            {
                byte[] rawData;
                using (var stream = file.OpenRead())
                {
                    if (stream.CanSeek)
                    {
                        int len = (int)stream.Length;
                        rawData = new byte[len];
                        stream.ReadExactly(rawData, 0, len);
                    }
                    else
                    {
                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            rawData = ms.ToArray();
                        }
                    }
                }

                byte[] storedData;
                int uncompressedSize;
                int compressedMagic;

                if (compress && rawData.Length >= 1024)
                {
                    storedData = CompressLZSS(rawData);
                    uncompressedSize = rawData.Length;
                    compressedMagic = FileEntry.CompressionMagic;
                }
                else
                {
                    storedData = rawData;
                    uncompressedSize = 0;
                    compressedMagic = 0;
                }

                fileData.Add((storedData, uncompressedSize));

                entries.Add(new FileEntry()
                {
                    FileName = file.FileName,
                    TimeStamp = file.TimeStamp,
                    DataSize = storedData.Length,
                    UncompressedSize = uncompressedSize,
                    CompressedMagic = compressedMagic
                });
            }

            var offset = 0;
            foreach (var entry in entries)
            {
                entry.StartOffset = offset;
                offset += entry.DataSize;
            }


            using (var target = File.Create(targetFile))
            {
                using (var output = new BinaryWriterEx(target, true))
                {
                    WriteProperties(output, PropertiesPairs.SelectMany(p => new[] { p.Key, p.Value }));
                    WriteBasicHeader(output, entries);
                }
                foreach (var (data, _) in fileData)
                {
                    target.Write(data, 0, data.Length);
                }

                target.Position = 0;
                byte[] hash;
                using (var sha1 = SHA1.Create())
                {
                    hash = sha1.ComputeHash(target);
                }
                target.WriteByte(0x0);
                target.Write(hash, 0, 20);
            }

            if (isReplaceSelf)
            {
                Files.Clear();
                Files.AddRange(entries.Select(e => new PBOFileExisting(e, this)));

#pragma warning disable CS0612 // Le type ou le membre est obsolète
                FileEntries.Clear();
                foreach (var entry in entries)
                {
                    FileEntries.AddLast(entry);
                }
#pragma warning restore CS0612 // Le type ou le membre est obsolète
            }
        }

        /// <summary>
        /// Adds a new in-memory file entry to the PBO.
        /// Validates inputs before adding.
        /// </summary>
        public void AddFile(string fileName, byte[] data)
        {
            Files.Add(new PBOFileInMemory(fileName, data));
        }

        /// <summary>
        /// Removes the first file entry with the given name (case-insensitive).
        /// Returns true if a file was removed.
        /// </summary>
        public bool RemoveFile(string fileName)
        {
            var normalized = fileName.Replace('/', '\\');
            for (int i = 0; i < Files.Count; i++)
            {
                if (string.Equals(Files[i].FileName, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    Files.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes the file entry at the given index.
        /// </summary>
        public void RemoveFile(int index)
        {
            Files.RemoveAt(index);
        }

        /// <summary>
        /// Finds the first file entry with the given name (case-insensitive).
        /// Returns null if not found.
        /// </summary>
#nullable enable
        public IPBOFileEntry? FindFile(string fileName)
#nullable restore
        {
            var normalized = fileName.Replace('/', '\\');
            for (int i = 0; i < Files.Count; i++)
            {
                if (string.Equals(Files[i].FileName, normalized, StringComparison.OrdinalIgnoreCase))
                    return Files[i];
            }
            return null;
        }

        /// <summary>
        /// Finds the first file entry with the given name (case-insensitive).
        /// Throws KeyNotFoundException if not found.
        /// </summary>
        public IPBOFileEntry GetFile(string fileName)
        {
            return FindFile(fileName) ?? throw new KeyNotFoundException($"File '{fileName}' not found in PBO.");
        }

        [Obsolete]
        public static IEnumerable<KeyValuePair<FileEntry, PBO>> GetAllNonEmptyFileEntries(string path)
        {
            var allPBOs = Directory.GetFiles(path, "*.pbo", SearchOption.AllDirectories);

            foreach (var pboPath in allPBOs)
            {
                var pbo = new PBO(pboPath);
                foreach (var entry in pbo.FileEntries)
                {
                    if (entry.DataSize > 0)
                        yield return new KeyValuePair<FileEntry, PBO>(entry, pbo);
                }
            }
        }

        public void Dispose()
        {
            if (pboFileStream != null)
            {
                pboFileStream.Close();
                pboFileStream = null;
            }
        }
    }
}
