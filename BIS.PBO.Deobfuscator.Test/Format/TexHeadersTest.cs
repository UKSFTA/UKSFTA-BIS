using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using BIS.PBO.Deobfuscator.Format;

namespace BIS.PBO.Deobfuscator.Test.Format
{
    public class TexHeadersTest
    {
        [Fact]
        public void Read_ValidFile_ParsesCorrectly()
        {
            var data = CreateTexHeaders(1, "data\\tex\\wall_co.paa");
            using var ms = new MemoryStream(data);

            var result = TexHeaders.Read(ms);

            Assert.Equal(1, result.Version);
            Assert.Single(result.Textures);
            Assert.Equal("data\\tex\\wall_co.paa", result.Textures[0].PAAFile);
        }

        [Fact]
        public void Read_InvalidMagic_Throws()
        {
            var data = new byte[] { 0x00, 0x00, 0x00, 0x00 }; // wrong magic
            using var ms = new MemoryStream(data);

            Assert.Throws<InvalidDataException>(() => TexHeaders.Read(ms));
        }

        [Fact]
        public void Read_WrongVersion_Throws()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(Encoding.ASCII.GetBytes("0DHT"));
            writer.Write(2); // version 2, unsupported
            writer.Write(0); // count

            ms.Position = 0;
            Assert.Throws<InvalidDataException>(() => TexHeaders.Read(ms));
        }

        [Fact]
        public void Read_ZeroTextures_ReturnsEmptyList()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(Encoding.ASCII.GetBytes("0DHT"));
            writer.Write(1);    // version
            writer.Write(0);    // count (no textures)

            ms.Position = 0;
            var result = TexHeaders.Read(ms);

            Assert.Empty(result.Textures);
        }

        [Fact]
        public void Read_MultipleTextures_AllParsed()
        {
            var data = CreateTexHeaders(3,
                "data\\tex\\a.paa",
                "data\\tex\\b.paa",
                "data\\tex\\c.paa");
            using var ms = new MemoryStream(data);

            var result = TexHeaders.Read(ms);

            Assert.Equal(3, result.Textures.Count);
            Assert.Equal("data\\tex\\a.paa", result.Textures[0].PAAFile);
            Assert.Equal("data\\tex\\b.paa", result.Textures[1].PAAFile);
            Assert.Equal("data\\tex\\c.paa", result.Textures[2].PAAFile);
        }

        [Fact]
        public void Read_TextureWithMipMaps_Parsed()
        {
            var data = BuildTexHeadersWithMips(1, "tex.paa", 2);
            using var ms = new MemoryStream(data);

            var result = TexHeaders.Read(ms);

            var tex = result.Textures[0];
            Assert.Equal(2, tex.MipMaps.Count);
            Assert.Equal((ushort)256, tex.MipMaps[0].Width);
            Assert.Equal((ushort)128, tex.MipMaps[1].Width);
        }

        [Fact]
        public void Read_TextureWithAllFields_SetCorrectly()
        {
            var data = BuildTexHeaderWithMip("test.paa", 1);
            using var ms = new MemoryStream(data);

            var result = TexHeaders.Read(ms);
            var tex = result.Textures[0];

            Assert.Equal(42u, tex.ColorPaletteCount);
            Assert.Equal(100u, tex.PalettePtr);
            Assert.True(tex.HasMaxCtagg);
            Assert.True(tex.IsAlpha);
            Assert.True(tex.IsTransparent);
            Assert.True(tex.IsAlphaNonOpaque);
            Assert.Equal(8u, tex.MipMapCount);
            Assert.Equal("test.paa", tex.PAAFile);
        }

        [Fact]
        public void TextureEntry_DefaultValues_AreSensible()
        {
            var entry = new TextureEntry();
            Assert.NotNull(entry.AverageColorF);
            Assert.Equal(4, entry.AverageColorF.Length);
            Assert.Empty(entry.AverageColor);
            Assert.Empty(entry.MaxColor);
            Assert.Empty(entry.MipMaps);
            Assert.Empty(entry.PAAFile);
        }

        [Fact]
        public void MipMap_DefaultValues_AreSensible()
        {
            var mip = new MipMap();
            Assert.Equal(0u, mip.DataOffset);
        }

        // ─── Helpers ───

        private static byte[] CreateTexHeaders(int count, params string[] paths)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(Encoding.ASCII.GetBytes("0DHT"));
            writer.Write(1); // version
            writer.Write(count);

            for (int i = 0; i < count; i++)
            {
                var path = i < paths.Length ? paths[i] : $"tex_{i}.paa";
                WriteTextureEntry(writer, path, mipCount: 0);
            }

            return ms.ToArray();
        }

        private static byte[] BuildTexHeaderWithMip(string path, int mipCount)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(Encoding.ASCII.GetBytes("0DHT"));
            writer.Write(1); // version
            writer.Write(1); // count

            // Full texture entry
            writer.Write(42u);  // ColorPaletteCount
            writer.Write(100u); // PalettePtr
            writer.Write(1.0f); writer.Write(0.5f); writer.Write(0.0f); writer.Write(1.0f); // AverageColorF[4]
            writer.Write(new byte[] { 128, 129, 130, 131 }); // AverageColor
            writer.Write(new byte[] { 255, 255, 255, 255 }); // MaxColor
            writer.Write(0u);   // ClampFlags
            writer.Write(0u);   // TransparentColor
            writer.Write((byte)1); // HasMaxCtagg
            writer.Write((byte)1); // IsAlpha
            writer.Write((byte)1); // IsTransparent
            writer.Write((byte)1); // IsAlphaNonOpaque
            writer.Write(8u);   // MipMapCount
            writer.Write(0u);   // PaxFormat
            writer.Write((byte)0); // LittleEndian
            writer.Write((byte)1); // IsPAA
            writer.Write(Encoding.ASCII.GetBytes(path));
            writer.Write((byte)0); // null terminator
            writer.Write(0u);   // PaxSuffixType
            writer.Write(0);    // mipCount (no additional mips beyond what's counted)

            return ms.ToArray();
        }

        private static byte[] BuildTexHeadersWithMips(int count, string path, int mipCount)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(Encoding.ASCII.GetBytes("0DHT"));
            writer.Write(1); // version
            writer.Write(count);

            for (int i = 0; i < count; i++)
                WriteTextureEntry(writer, path, mipCount);

            return ms.ToArray();
        }

        private static void WriteTextureEntry(BinaryWriter w, string paaPath, int mipCount)
        {
            w.Write(0u); // ColorPaletteCount
            w.Write(0u); // PalettePtr
            w.Write(0f); w.Write(0f); w.Write(0f); w.Write(0f); // AverageColorF
            w.Write(new byte[4]); // AverageColor
            w.Write(new byte[4]); // MaxColor
            w.Write(0u); // ClampFlags
            w.Write(0u); // TransparentColor
            w.Write((byte)0); // HasMaxCtagg
            w.Write((byte)0); // IsAlpha
            w.Write((byte)0); // IsTransparent
            w.Write((byte)0); // IsAlphaNonOpaque
            w.Write(0u); // MipMapCount
            w.Write(0u); // PaxFormat
            w.Write((byte)0); // LittleEndian
            w.Write((byte)0); // IsPAA
            w.Write(Encoding.ASCII.GetBytes(paaPath));
            w.Write((byte)0); // null terminator
            w.Write(0u); // PaxSuffixType
            w.Write(mipCount); // mip entry count

            for (int m = 0; m < mipCount; m++)
            {
                w.Write((ushort)(256 / (m + 1))); // Width
                w.Write((ushort)(128 / (m + 1))); // Height
                w.Write((ushort)0); // AlwaysZero
                w.Write((byte)0);   // PaxFormat
                w.Write((byte)3);   // AlwaysThree
                w.Write((uint)(m * 1024)); // DataOffset
            }

            w.Write(0u); // PaxFileSize
        }
    }
}
