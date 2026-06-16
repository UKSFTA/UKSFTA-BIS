using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIS.PBO.Deobfuscator.Format
{
    public class TexHeaders
    {
        public int Version { get; private set; }
        public List<TextureEntry> Textures { get; private set; } = new();

        public static TexHeaders Read(Stream stream)
        {
            var result = new TexHeaders();
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            var magic = new string(reader.ReadBytes(4).Select(b => (char)b).ToArray());
            if (magic != "0DHT")
                throw new InvalidDataException($"Invalid texHeaders magic: expected '0DHT', got '{magic}'");

            result.Version = reader.ReadInt32();
            if (result.Version != 1)
                throw new InvalidDataException($"Unsupported texHeaders version: {result.Version}");

            int count = reader.ReadInt32();
            result.Textures.Capacity = count;

            for (int i = 0; i < count; i++)
            {
                result.Textures.Add(ReadTextureEntry(reader));
            }

            return result;
        }

        private static TextureEntry ReadTextureEntry(BinaryReader r)
        {
            var entry = new TextureEntry();

            entry.ColorPaletteCount = r.ReadUInt32();
            entry.PalettePtr = r.ReadUInt32();

            for (int i = 0; i < 4; i++)
                entry.AverageColorF[i] = r.ReadSingle();

            entry.AverageColor = r.ReadBytes(4);
            entry.MaxColor = r.ReadBytes(4);

            entry.ClampFlags = r.ReadUInt32();
            entry.TransparentColor = r.ReadUInt32();

            entry.HasMaxCtagg = r.ReadByte() != 0;
            entry.IsAlpha = r.ReadByte() != 0;
            entry.IsTransparent = r.ReadByte() != 0;
            entry.IsAlphaNonOpaque = r.ReadByte() != 0;

            entry.MipMapCount = r.ReadUInt32();
            entry.PaxFormat = r.ReadUInt32();

            entry.LittleEndian = r.ReadByte() != 0;
            entry.IsPAA = r.ReadByte() != 0;

            entry.PAAFile = ReadAsciiz(r);

            entry.PaxSuffixType = r.ReadUInt32();

            int mipCount = r.ReadInt32();
            entry.MipMaps = new List<MipMap>(mipCount);
            for (int i = 0; i < mipCount; i++)
            {
                entry.MipMaps.Add(new MipMap
                {
                    Width = r.ReadUInt16(),
                    Height = r.ReadUInt16(),
                    AlwaysZero = r.ReadUInt16(),
                    PaxFormat = r.ReadByte(),
                    AlwaysThree = r.ReadByte(),
                    DataOffset = r.ReadUInt32()
                });
            }

            entry.PaxFileSize = r.ReadUInt32();

            return entry;
        }

        public void Write(Stream stream)
        {
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("0DHT"));
            writer.Write(Version);
            writer.Write(Textures.Count);
            foreach (var entry in Textures)
                WriteTextureEntry(writer, entry);
        }

        /// <summary>
        /// Returns a human-readable text representation of all texture entries.
        /// </summary>
        public void ResolvePaths(Dictionary<string, string> pathMap)
        {
            foreach (var entry in Textures)
            {
                var normalized = entry.PAAFile.Replace('\\', '/');
                if (TryResolvePath(normalized, pathMap, out var resolved))
                {
                    entry.PAAFile = resolved;
                    continue;
                }
                // Try with FixEncoding (Latin-1 → UTF-8) for obfuscated Cyrillic paths
                var fixedPath = FixEncoding(normalized);
                if (fixedPath != normalized && TryResolvePath(fixedPath, pathMap, out resolved))
                    entry.PAAFile = resolved;
            }
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"texHeaders version {Version}");
            sb.AppendLine($"Textures: {Textures.Count}");
            sb.AppendLine();

            for (int i = 0; i < Textures.Count; i++)
            {
                var t = Textures[i];
                var firstMip = t.MipMaps.Count > 0 ? t.MipMaps[0] : null;
                var dims = firstMip != null ? $"{firstMip.Width}x{firstMip.Height}" : "?x?";
                sb.AppendLine($"[{i,4}] {t.PAAFile}");
                sb.AppendLine($"       Format: 0x{t.PaxFormat:X8}  Dimensions: {dims}  Mipmaps: {t.MipMapCount}");
                sb.AppendLine($"       AvgColor: RGBA({t.AverageColor[0]},{t.AverageColor[1]},{t.AverageColor[2]},{t.AverageColor[3]})" +
                              $"  Alpha: {t.IsAlpha}  Transparent: {t.IsTransparent}");
            }

            return sb.ToString();
        }

        private static string FixEncoding(string s)
        {
            var bytes = new byte[s.Length];
            bool needsFix = false;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c > 127)
                    needsFix = true;
                bytes[i] = (byte)c;
            }
            return needsFix ? Encoding.UTF8.GetString(bytes) : s;
        }

        private static bool TryResolvePath(string contentPath, Dictionary<string, string> pathMap, out string resolved)
        {
            if (pathMap.TryGetValue(contentPath, out resolved!))
                return true;

            var contentSlash = contentPath.LastIndexOf('/');
            var contentDir = contentSlash >= 0 ? contentPath.Substring(0, contentSlash) : "";
            var contentFile = contentSlash >= 0 ? contentPath.Substring(contentSlash + 1) : contentPath;

            foreach (var kvp in pathMap)
            {
                var key = kvp.Key;
                var keySlash = key.LastIndexOf('/');
                var keyDir = keySlash >= 0 ? key.Substring(0, keySlash) : "";
                var keyFile = keySlash >= 0 ? key.Substring(keySlash + 1) : key;

                if (!contentFile.EndsWith(keyFile, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!contentDir.EndsWith(keyDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolved = kvp.Value;
                return true;
            }

            resolved = string.Empty;
            return false;
        }

        private static void WriteTextureEntry(BinaryWriter w, TextureEntry entry)
        {
            w.Write(entry.ColorPaletteCount);
            w.Write(entry.PalettePtr);

            for (int i = 0; i < 4; i++)
                w.Write(entry.AverageColorF[i]);

            w.Write(entry.AverageColor, 0, 4);
            w.Write(entry.MaxColor, 0, 4);

            w.Write(entry.ClampFlags);
            w.Write(entry.TransparentColor);

            w.Write(entry.HasMaxCtagg ? (byte)1 : (byte)0);
            w.Write(entry.IsAlpha ? (byte)1 : (byte)0);
            w.Write(entry.IsTransparent ? (byte)1 : (byte)0);
            w.Write(entry.IsAlphaNonOpaque ? (byte)1 : (byte)0);

            w.Write(entry.MipMapCount);
            w.Write(entry.PaxFormat);

            w.Write(entry.LittleEndian ? (byte)1 : (byte)0);
            w.Write(entry.IsPAA ? (byte)1 : (byte)0);

            WriteAsciiz(w, entry.PAAFile);

            w.Write(entry.PaxSuffixType);
            w.Write(entry.MipMaps.Count);
            foreach (var mip in entry.MipMaps)
            {
                w.Write(mip.Width);
                w.Write(mip.Height);
                w.Write(mip.AlwaysZero);
                w.Write(mip.PaxFormat);
                w.Write(mip.AlwaysThree);
                w.Write(mip.DataOffset);
            }
            w.Write(entry.PaxFileSize);
        }

        private static string ReadAsciiz(BinaryReader r)
        {
            var bytes = new List<byte>();
            byte b;
            while ((b = r.ReadByte()) != 0)
                bytes.Add(b);
            return Encoding.Latin1.GetString(bytes.ToArray());
        }

        private static void WriteAsciiz(BinaryWriter w, string s)
        {
            var bytes = Encoding.ASCII.GetBytes(s);
            w.Write(bytes);
            w.Write((byte)0);
        }
    }

    public class TextureEntry
    {
        public uint ColorPaletteCount { get; set; }
        public uint PalettePtr { get; set; }
        public float[] AverageColorF { get; set; } = new float[4];
        public byte[] AverageColor { get; set; } = Array.Empty<byte>();
        public byte[] MaxColor { get; set; } = Array.Empty<byte>();
        public uint ClampFlags { get; set; }
        public uint TransparentColor { get; set; }
        public bool HasMaxCtagg { get; set; }
        public bool IsAlpha { get; set; }
        public bool IsTransparent { get; set; }
        public bool IsAlphaNonOpaque { get; set; }
        public uint MipMapCount { get; set; }
        public uint PaxFormat { get; set; }
        public bool LittleEndian { get; set; }
        public bool IsPAA { get; set; }
        public string PAAFile { get; set; } = "";
        public uint PaxSuffixType { get; set; }
        public List<MipMap> MipMaps { get; set; } = new();
        public uint PaxFileSize { get; set; }
    }

    public class MipMap
    {
        public ushort Width { get; set; }
        public ushort Height { get; set; }
        public ushort AlwaysZero { get; set; }
        public byte PaxFormat { get; set; }
        public byte AlwaysThree { get; set; }
        public uint DataOffset { get; set; }
    }
}
