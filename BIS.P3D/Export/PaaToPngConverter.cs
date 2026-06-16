using System;
using System.IO;
using System.IO.Compression;

namespace BIS.P3D.Export
{
    public static class PaaToPngConverter
    {
        // CRC-32 table (polynomial 0xEDB88320)
        private static readonly uint[] CrcTable = GenerateCrcTable();

        private static uint[] GenerateCrcTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                table[i] = crc;
            }
            return table;
        }

        private static uint ComputeCrc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        // Adler-32 checksum
        private static uint ComputeAdler32(byte[] data)
        {
            uint a = 1, b = 0;
            const uint mod = 65521;
            foreach (byte d in data)
            {
                a = (a + d) % mod;
                b = (b + a) % mod;
            }
            return (b << 16) | a;
        }

        /// <summary>
        /// Converts a PAA texture stream to a PNG and writes it to the output stream.
        /// </summary>
        public static void ConvertToPng(Stream paaStream, Stream pngOutput)
        {
            byte[] pngData = ConvertToPng(paaStream);
            pngOutput.Write(pngData, 0, pngData.Length);
        }

        /// <summary>
        /// Converts a PAA texture stream to a PNG byte array.
        /// </summary>
        public static byte[] ConvertToPng(Stream paaStream)
        {
            var paa = new BIS.PAA.PAA(paaStream);
            int width = paa.Width;
            int height = paa.Height;
            byte[] argbPixels = BIS.PAA.PAA.GetARGB32PixelData(paa, paaStream);

            // Build uncompressed IDAT data: for each row, filter byte 0 + RGBA pixels
            int rowSize = width * 4; // RGBA = 4 bytes per pixel
            int rawDataSize = height * (1 + rowSize);
            byte[] rawIdat = new byte[rawDataSize];

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * (1 + rowSize);
                // Filter byte: 0 (None)
                rawIdat[rowStart] = 0;

                // Pixel layout from SetColor is BGRA [B, G, R, A], convert to PNG RGBA
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * 4;
                    int dstIdx = rowStart + 1 + x * 4;
                    rawIdat[dstIdx + 0] = argbPixels[srcIdx + 2]; // R
                    rawIdat[dstIdx + 1] = argbPixels[srcIdx + 1]; // G
                    rawIdat[dstIdx + 2] = argbPixels[srcIdx + 0]; // B
                    rawIdat[dstIdx + 3] = argbPixels[srcIdx + 3]; // A
                }
            }

            // Zlib-compress the IDAT data
            byte[] compressedIdat;
            using (var ms = new MemoryStream())
            {
                // Zlib header: CMF=0x78 (deflate, 32K window), FLG=0x01 (no dict, check ok)
                ms.WriteByte(0x78);
                ms.WriteByte(0x01);

                using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, true))
                {
                    deflate.Write(rawIdat, 0, rawIdat.Length);
                }

                // Adler-32 checksum (big-endian)
                uint adler = ComputeAdler32(rawIdat);
                ms.WriteByte((byte)((adler >> 24) & 0xFF));
                ms.WriteByte((byte)((adler >> 16) & 0xFF));
                ms.WriteByte((byte)((adler >> 8) & 0xFF));
                ms.WriteByte((byte)(adler & 0xFF));

                compressedIdat = ms.ToArray();
            }

            // Build PNG
            using (var ms = new MemoryStream())
            {
                // PNG signature
                ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

                // IHDR chunk
                byte[] ihdrData = new byte[13];
                WriteBigEndian(ihdrData, 0, (uint)width);
                WriteBigEndian(ihdrData, 4, (uint)height);
                ihdrData[8] = 8;  // bit depth
                ihdrData[9] = 6;  // color type: RGBA
                ihdrData[10] = 0; // compression
                ihdrData[11] = 0; // filter
                ihdrData[12] = 0; // interlace
                WriteChunk(ms, "IHDR", ihdrData);

                // IDAT chunk
                WriteChunk(ms, "IDAT", compressedIdat);

                // IEND chunk
                WriteChunk(ms, "IEND", Array.Empty<byte>());

                return ms.ToArray();
            }
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            // Length (4 bytes big-endian)
            byte[] lenBytes = new byte[4];
            WriteBigEndian(lenBytes, 0, (uint)data.Length);
            stream.Write(lenBytes, 0, 4);

            // Type (4 bytes ASCII)
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes, 0, 4);

            // Data
            stream.Write(data, 0, data.Length);

            // CRC-32 of type + data
            byte[] crcInput = new byte[4 + data.Length];
            Buffer.BlockCopy(typeBytes, 0, crcInput, 0, 4);
            Buffer.BlockCopy(data, 0, crcInput, 4, data.Length);
            uint crc = ComputeCrc32(crcInput);
            byte[] crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, crc);
            stream.Write(crcBytes, 0, 4);
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset + 0] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }
    }
}
