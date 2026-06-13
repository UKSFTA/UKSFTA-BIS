using System;
using System.IO;
using BIS.Core.Streams;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BIS.PAA.Conversion
{
    public static class PaaConverter
    {
        public static void PaaToPng(Stream paaStream, Stream pngStream)
        {
            paaStream.Position = 0;
            var pixelData = PAA.GetARGB32PixelData(paaStream);
            paaStream.Position = 0;
            var paa = new PAA(paaStream, false);
            using var image = Image.LoadPixelData<Bgra32>(pixelData, paa.Width, paa.Height);
            image.SaveAsPng(pngStream);
        }

        public static void PngToPaa(Stream pngStream, Stream paaStream)
        {
            using var image = Image.Load<Bgra32>(pngStream);
            int w = image.Width;
            int h = image.Height;

            var pixels = new byte[w * h * 4];
            image.CopyPixelDataTo(pixels);

            WriteDxt1Paa(paaStream, pixels, w, h);
        }

        /// <summary>
        /// Writes a DXT1 PAA with a single mipmap.
        /// Binary layout: magic(2) + GGAT taggs + palette(2) + mipmap(header+data) + terminator(6).
        /// The GGAT taggs are a PAA-specific chunk system: 4-byte 'GGAT' + 4-byte type reversed + 4-byte size + payload.
        /// </summary>
        private static void WriteDxt1Paa(Stream stream, byte[] bgraPixels, int width, int height)
        {
            var writer = new BinaryWriterEx(stream);

            writer.Write((ushort)0x01ff);

            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int dxtSize = blocksX * blocksY * 8;
            byte[] dxtData = EncodeDxt1(bgraPixels, width, height);

            const int headerSize = 96;
            int mipOffset0 = headerSize;

            WriteTagg(writer, "FLAG", 4);
            writer.Write(0);

            WriteTagg(writer, "OFFS", 16 * 4);
            writer.Write(mipOffset0);
            for (int i = 1; i < 16; i++)
                writer.Write(0);

            writer.Write((ushort)0); // no palette colors

            int dataStart = (int)writer.Position;
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.WriteUInt24((uint)dxtSize);
            writer.Write(dxtData);

            writer.Write(0u);
            writer.Write((ushort)0);
        }

        private static void WriteTagg(BinaryWriterEx writer, string name, int size)
        {
            writer.WriteAsciiz("GGAT");
            for (int i = 3; i >= 0; i--)
                writer.Write((byte)name[i]);
            writer.Write(size);
        }

        private static byte[] EncodeDxt1(byte[] bgra, int w, int h)
        {
            int blocksX = (w + 3) / 4;
            int blocksY = (h + 3) / 4;
            var output = new byte[blocksX * blocksY * 8];

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    int rSum = 0, gSum = 0, bSum = 0, count = 0;
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px, y = by * 4 + py;
                            if (x < w && y < h)
                            {
                                int idx = (y * w + x) * 4;
                                bSum += bgra[idx];
                                gSum += bgra[idx + 1];
                                rSum += bgra[idx + 2];
                                count++;
                            }
                        }

                    if (count == 0) count = 1;
                    byte avgR = (byte)(rSum / count);
                    byte avgG = (byte)(gSum / count);
                    byte avgB = (byte)(bSum / count);

                    ushort c0 = RgbTo565(avgR, avgG, avgB);
                    ushort c1 = RgbTo565(
                        (byte)Math.Clamp(avgR * 2 / 3, 0, 255),
                        (byte)Math.Clamp(avgG * 2 / 3, 0, 255),
                        (byte)Math.Clamp(avgB * 2 / 3, 0, 255));

                    int blockOff = (by * blocksX + bx) * 8;
                    BitConverter.GetBytes(c0).CopyTo(output, blockOff);
                    BitConverter.GetBytes(c1).CopyTo(output, blockOff + 2);

                    byte r0 = (byte)((c0 >> 11) << 3);
                    byte g0 = (byte)(((c0 >> 5) & 0x3F) << 2);
                    byte b0 = (byte)((c0 & 0x1F) << 3);
                    byte r1 = (byte)((c1 >> 11) << 3);
                    byte g1 = (byte)(((c1 >> 5) & 0x3F) << 2);
                    byte b1 = (byte)((c1 & 0x1F) << 3);

                    uint indices = 0;
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px, y = by * 4 + py;
                            int bitIdx = py * 4 + px;
                            if (x >= w || y >= h) continue;
                            int idx = (y * w + x) * 4;
                            int pr = bgra[idx + 2], pg = bgra[idx + 1], pb = bgra[idx];
                            int d0 = (pr - r0) * (pr - r0) + (pg - g0) * (pg - g0) + (pb - b0) * (pb - b0);
                            int d1 = (pr - r1) * (pr - r1) + (pg - g1) * (pg - g1) + (pb - b1) * (pb - b1);
                            indices |= (d0 <= d1 ? 0u : 1u) << (bitIdx * 2);
                        }

                    BitConverter.GetBytes(indices).CopyTo(output, blockOff + 4);
                }
            }
            return output;
        }

        private static ushort RgbTo565(byte r, byte g, byte b)
        {
            return (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
        }
    }
}
