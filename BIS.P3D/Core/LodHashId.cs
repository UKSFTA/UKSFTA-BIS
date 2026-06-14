using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using BIS.Core.Math;

namespace BIS.P3D
{
    public class LodHashId
    {
        public static readonly LodHashId Empty = new LodHashId(null, null, 0);

        public LodHashId(byte[] hash15, byte[] hash8, int v)
        {
            Hash15 = hash15;
            Hash8 = hash8;
            Vertex = v;
        }

        public string Hash15AsString => Convert.ToBase64String(Hash15);

        public byte[] Hash15 { get; }

        public string Hash8AsString => Convert.ToBase64String(Hash8);

        public byte[] Hash8 { get; }
        public int Vertex { get; }

        public static LodHashId Compute(IEnumerable<Vector3P> vectors)
        {
            return Compute(vectors.Select(v => v.Vector3));
        }

        public static LodHashId Compute(IEnumerable<Vector3PCompressed> vectors)
        {
            return Compute(vectors.Select(v => new Vector3(v.X, v.Y, v.Z)));
        }

        private const double Scale15 = short.MaxValue; // 15 bits precision
        private const int Scale15To8 = 4096;

        public static LodHashId Compute(IEnumerable<Vector3> vectors)
        {
            // Single pass: distinct + min/max (7 iterations → 1)
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            var set = new HashSet<Vector3>();
            foreach (var v in vectors)
            {
                if (set.Add(v))
                {
                    if (v.X < minX) minX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.Z < minZ) minZ = v.Z;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y > maxY) maxY = v.Y;
                    if (v.Z > maxZ) maxZ = v.Z;
                }
            }
            if (set.Count == 0)
            {
                return Empty;
            }

            var deltaX = (double)Math.Max(maxX - minX, 0.001);
            var deltaY = (double)Math.Max(maxY - minY, 0.001);
            var deltaZ = (double)Math.Max(maxZ - minZ, 0.001);

            // Normalize from distinct set (no need to revisit original enumerable)
            var normalized = new List<Vector3>(set.Count);
            foreach (var v in set)
            {
                normalized.Add(new Vector3(
                    (float)Math.Round((v.X - minX) / deltaX * Scale15),
                    (float)Math.Round((v.Y - minY) / deltaY * Scale15),
                    (float)Math.Round((v.Z - minZ) / deltaZ * Scale15)));
            }

            // Merge identical points (after rounding) and sort for stable order
            normalized = normalized.Distinct()
                .OrderBy(v => v.X)
                .ThenBy(v => v.Y)
                .ThenBy(v => v.Z).ToList();


            var cube8x = new int[512];
            var all15 = new MemoryStream();
            using (var writer = new BinaryWriter(all15, Encoding.ASCII, true))
            {
                foreach (var vertex in normalized)
                {
                    writer.Write((short)vertex.X);
                    writer.Write((short)vertex.Y);
                    writer.Write((short)vertex.Z);
                    cube8x[(int)(vertex.X / Scale15To8) * 64 + (int)(vertex.Y / Scale15To8) * 8 + (int)(vertex.Z / Scale15To8)]++;
                }
            }

            all15.Position = 0;

            var cube8Bytes = To1Bit(cube8x);

            // Generate a HASH using SHA256 on each dataset
            using (var sha = SHA256.Create())
            {
                return new LodHashId(sha.ComputeHash(all15), sha.ComputeHash(cube8Bytes), normalized.Count);
            }
        }

        private static byte[] To1Bit(int[] cube8x)
        {
            var cube8 = new BitArray(cube8x.Length);
            var avg = cube8x.Average() - 1;
            for (int i = 0; i < cube8x.Length; ++i)
            {
                cube8.Set(i, cube8x[i] > avg);
            }
            var cube8Bytes = new byte[cube8x.Length / 8];
            cube8.CopyTo(cube8Bytes, 0);
            return cube8Bytes;
        }

        public override string ToString()
        {
            if (Hash15 == null)
            {
                return "EMPTY";
            }
            return $"Hash15={Hash15AsString} Hash8={Hash8AsString} V={Vertex}";
        }
    }
}
