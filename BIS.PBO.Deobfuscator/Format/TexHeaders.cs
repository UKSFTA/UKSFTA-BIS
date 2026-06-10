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

        private static string ReadAsciiz(BinaryReader r)
        {
            var bytes = new List<byte>();
            byte b;
            while ((b = r.ReadByte()) != 0)
                bytes.Add(b);
            return Encoding.ASCII.GetString(bytes.ToArray());
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
