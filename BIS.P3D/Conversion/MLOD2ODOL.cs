using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIS.Core;
using BIS.Core.Math;
using BIS.Core.Streams;
using BIS.P3D.MLOD;
using BIS.P3D.ODOL;

namespace BIS.P3D.Conversion
{
    public static class MLOD2ODOL
    {
        public static ODOL.ODOL Convert(MLOD.MLOD mlod, int version = 73)
        {
            // Preserve original order from MLOD (ODOL uses address-table order, not sorted)
            var lods = mlod.Lods.ToArray();
            var noOfLods = lods.Length;

            using var ms = new MemoryStream();
            var w = new BinaryWriterEx(ms, true);

            if (version >= 44) w.UseLZOCompression = true;
            if (version >= 64) w.UseCompressionFlag = true;

            // ODOL content (mirrors ODOL.WriteContent binary format)
            w.Write(version);

            if (version >= 59) w.Write(0u); // AppID
            if (version >= 58) w.WriteAsciiz(""); // MuzzleFlash

            // Resolutions
            w.WriteArray(lods.Select(l => l.Resolution).ToArray());

            // ModelInfo
            WriteModelInfo(w, version, noOfLods, mlod);

            // Animations flag (version >= 30)
            if (version >= 30) w.Write(false);

            // Address table placeholders
            var addrPos = w.Position;
            for (int i = 0; i < noOfLods; i++) w.Write(0u); // start addresses
            for (int i = 0; i < noOfLods; i++) w.Write(0u); // end addresses
            for (int i = 0; i < noOfLods; i++) w.Write(true); // permanent = true (no loadable info)

            // Write each LOD in descending resolution order
            var startAddr = new uint[noOfLods];
            var endAddr = new uint[noOfLods];
            for (int i = 0; i < noOfLods; i++)
            {
                startAddr[i] = (uint)w.Position;
                WriteLOD(w, lods[i], version);
                endAddr[i] = (uint)w.Position;
            }

            // Extra = empty (nothing written)

            // Patch addresses
            w.Position = addrPos;
            for (int i = 0; i < noOfLods; i++) w.Write(startAddr[i]);
            for (int i = 0; i < noOfLods; i++) w.Write(endAddr[i]);

            // Read back as ODOL
            w.Flush();
            ms.Position = 0;
            var r = new BinaryReaderEx(ms);
            if (version >= 44) r.UseLZOCompression = true;
            if (version >= 64) r.UseCompressionFlag = true;
            r.Version = version;

            var odol = new ODOL.ODOL();
            odol.ReadContent(r);
            return odol;
        }

        private static void WriteModelInfo(BinaryWriterEx w, int version, int noOfLods, MLOD.MLOD mlod)
        {
            var info = mlod.ModelInfo;
            var bboxMin = info.BboxMin;
            var bboxMax = info.BboxMax;
            var center = new Vector3P(
                (bboxMin.X + bboxMax.X) / 2f,
                (bboxMin.Y + bboxMax.Y) / 2f,
                (bboxMin.Z + bboxMax.Z) / 2f);

            w.Write(0); // Special
            w.Write(0f); // BoundingSphere
            w.Write(0f); // GeometrySphere
            w.Write(0); // Remarks
            w.Write(0); // AndHints
            w.Write(0); // OrHints
            new Vector3P(0, 0, 0).Write(w); // AimingCenter
            new PackedColor(0u).Write(w); // Color
            new PackedColor(0u).Write(w); // ColorType
            w.Write(10f); // ViewDensity
            bboxMin.Write(w);
            bboxMax.Write(w);
            if (version >= 70) w.Write(1f); // LodDensityCoef
            if (version >= 71) w.Write(1f); // DrawImportance
            if (version >= 52) { bboxMin.Write(w); bboxMax.Write(w); } // BboxMinVisual/BboxMaxVisual
            center.Write(w); // BoundingCenter
            center.Write(w); // GeometryCenter
            center.Write(w); // CenterOfMass
            new Vector3P(1, 0, 0).Write(w);
            new Vector3P(0, 1, 0).Write(w);
            new Vector3P(0, 0, 1).Write(w);
            w.Write(true); // AutoCenter
            w.Write(false); // LockAutoCenter
            w.Write(false); // CanOcclude
            w.Write(false); // CanBeOccluded
            if (version >= 73) w.Write(false); // AICovers

            if ((version >= 42 && version < 10000) || version >= 10042)
            {
                w.Write(0f); w.Write(0f); w.Write(0f); w.Write(0f);
            }
            if ((version >= 43 && version < 10000) || version >= 10043)
            {
                w.Write(0f); w.Write(0f);
            }
            if (version >= 33) w.Write(false);
            if (version >= 37) { w.Write(0); w.Write(false); }
            if (version >= 48) w.Write(0f);
            w.Write(false); // Animated

            w.WriteAsciiz(""); // Skeleton (empty)

            w.Write((byte)MapType.Hide);
            w.WriteCompressedFloatArray([]);
            w.Write(1f); w.Write(1f); w.Write(0f); w.Write(1f); // Mass, InvMass, Armor, InvArmor
            if (version >= 72) w.Write(0f);
            if (version >= 53) w.Write((byte)0);
            if (version >= 54) w.Write((byte)0);
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
            w.Write((sbyte)0);
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
            w.Write((byte)0); w.Write(0u);
            if (version >= 38) w.Write(false);
            w.WriteAsciiz(info.Class ?? "");
            w.WriteAsciiz("");
            w.Write(false);
            if (version >= 31) w.Write(0u);
            if (version >= 57)
            {
                for (int i = 0; i < noOfLods; i++) w.Write(-1);
                for (int i = 0; i < noOfLods; i++) w.Write(-1);
                for (int i = 0; i < noOfLods; i++) w.Write(-1);
            }
        }

        private static void WriteLOD(BinaryWriterEx w, P3DM_LOD mlodLod, int version)
        {
            var nVerts = mlodLod.Points.Length;
            Action<string> logPos = (label) => Console.Error.WriteLine($"[MLOD2ODOL] WriteLOD {label}: {w.Position}");

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            foreach (var p in mlodLod.Points)
            {
                if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y; if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y; if (p.Z > maxZ) maxZ = p.Z;
            }
            var bMin = new Vector3P(minX, minY, minZ);
            var bMax = new Vector3P(maxX, maxY, maxZ);
            var bCenter = new Vector3P((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);

            var textures = new List<string>();
            var textureIdxMap = new Dictionary<string, int>();
            var materials = new List<string>();
            var materialIdxMap = new Dictionary<string, int>();
            foreach (var face in mlodLod.Faces)
            {
                var tex = face.Texture ?? "";
                if (!textureIdxMap.ContainsKey(tex)) { textureIdxMap[tex] = textures.Count; textures.Add(tex); }
                var mat = face.Material ?? "";
                if (!materialIdxMap.ContainsKey(mat)) { materialIdxMap[mat] = materials.Count; materials.Add(mat); }
            }
            var texArr = textures.ToArray();

            // 1-3. Proxies, SubSkeletonsToSkeleton, SkeletonToSubSkeleton (empty)
            w.Write(0); w.Write(0); w.Write(0);
            logPos("after_proxies");

            if (version >= 50) w.Write((uint)nVerts);
            else w.WriteCondensedIntArray((int[])[]);

            if (version >= 51) w.Write(0f); // FaceArea
            w.Write(0); w.Write(0); // OrHints, AndHints
            bMin.Write(w); bMax.Write(w); bCenter.Write(w);
            w.Write((float)Math.Sqrt(
                Math.Pow((maxX - minX) / 2f, 2) +
                Math.Pow((maxY - minY) / 2f, 2) +
                Math.Pow((maxZ - minZ) / 2f, 2)));
            logPos("after_bounds");

            // Textures
            w.WriteArray(texArr, (b, t) => b.WriteAsciiz(t));
            logPos("after_textures");

            // Materials
            w.Write(materials.Count);
            foreach (var mn in materials) WriteEmbeddedMaterial(w, mn);
            logPos("after_materials");

            // PointToVertex / VertexToPoint (identity)
            WriteCompressedVertexIndexArray(w, version, Enumerable.Range(0, nVerts).ToArray());
            WriteCompressedVertexIndexArray(w, version, Enumerable.Range(0, nVerts).ToArray());
            logPos("after_p2v_v2p");

            // Polygons
            WritePolygons(w, mlodLod, version);
            logPos("after_polygons");

            // Sections
            WriteSections(w, mlodLod, texArr, textureIdxMap, materialIdxMap, version);
            logPos("after_sections");

            var nsTaggs = mlodLod.Taggs.OfType<NamedSelectionTagg>().ToArray();
            w.Write(nsTaggs.Length);
            foreach (var nst in nsTaggs)
            {
                var selectedVerts = new List<int>();
                for (int i = 0; i < nst.Points.Length; i++)
                    if (nst.Points[i] != 0) selectedVerts.Add(i);

                var selectedFaces = new List<int>();
                for (int i = 0; i < nst.Faces.Length; i++)
                    if (nst.Faces[i] != 0) selectedFaces.Add(i);

                var weights = selectedVerts.Select(v => (byte)255).ToArray();

                w.WriteAsciiz(nst.Name);
                WriteCompressedVertexIndexArray(w, version, selectedFaces.ToArray());
                w.Write(0);
                w.Write(false);
                w.WriteCompressedIntArray((int[])[]);
                WriteCompressedVertexIndexArray(w, version, selectedVerts.ToArray());
                w.Write(weights.Length);
                w.WriteCompressed(weights);
            }
            logPos("after_named_selections");

            // NamedProperties
            var propTaggs = mlodLod.Taggs.OfType<PropertyTagg>().ToArray();
            w.Write(propTaggs.Length);
            foreach (var pt in propTaggs) { w.WriteAsciiz(pt.PropertyName.TrimEnd('\0')); w.WriteAsciiz(pt.Value.TrimEnd('\0')); }
            logPos("after_properties");

            // Frames (empty)
            w.Write(0);
            w.Write(0); w.Write(0); w.Write(0); w.Write(false);

            // Rest data block
            var restPos = w.Position;
            w.Write(0u);
            logPos("after_rest_header");

            if (version >= 50) w.WriteCondensedIntArray((int[])[]); // Clip (empty)

            // UVSet 0
            var nV = mlodLod.Points.Length;
            var uvs = new float[nV][];
            var hasUV = new bool[nV];
            CollectUVs(mlodLod, uvs, hasUV);

            if (version >= 45) WriteUVSetDiscretized(w, uvs, hasUV);
            else WriteUVSetFloat(w, uvs, hasUV);

            w.Write(1); // UvSets count

            // Vertices
            w.WriteCompressedArray(
                mlodLod.Points.Select(p => new Vector3P(p.X, p.Y, p.Z)).ToArray(),
                (b, v) => v.Write(b), 12);

            // Normals
            if (version >= 45)
            {
                w.WriteCondensedArray(
                    mlodLod.Normals.Select(n => new Vector3PCompressed(PackNormal(n))).ToArray(),
                    (b, v) => v.Write(b), 4);
                w.WriteCompressedArray(
                    Array.Empty<Tuple<Vector3PCompressed, Vector3PCompressed>>(),
                    (b, v) => { v.Item1.Write(b); v.Item2.Write(b); }, 8);
            }
            else
            {
                w.WriteCondensedArray(
                    mlodLod.Normals.Select(n => new Vector3P(n.X, n.Y, n.Z)).ToArray(),
                    (b, v) => v.Write(b), 12);
                w.WriteCompressedArray(
                    Array.Empty<Tuple<Vector3P, Vector3P>>(),
                    (b, v) => { v.Item1.Write(b); v.Item2.Write(b); }, 24);
            }

            // Bone refs (empty)
            w.WriteCompressedArray(Array.Empty<AnimationRTWeight>(), (b, v) => v.Write(b), 12);
            w.WriteCompressedArray(Array.Empty<VertexNeighborInfo>(), (b, v) => v.Write(b), 32);

            if (version >= 67) w.Write(0u);

            var endPos = w.Position;
            var restSize = (uint)(endPos - restPos - 4);
            w.Position = restPos;
            w.Write(restSize);
            w.Position = endPos;

            if (version >= 68) w.Write((byte)0);
        }

        private static void WriteEmbeddedMaterial(BinaryWriterEx w, string name)
        {
            w.WriteAsciiz(name);
            w.Write(6u); // Version
            new ColorP(0.5f, 0.5f, 0.5f, 1f).Write(w);
            new ColorP(0.5f, 0.5f, 0.5f, 1f).Write(w);
            new ColorP(1f, 1f, 1f, 1f).Write(w);
            new ColorP(0f, 0f, 0f, 1f).Write(w);
            new ColorP(0f, 0f, 0f, 0f).Write(w);
            new ColorP(0f, 0f, 0f, 0f).Write(w);
            w.Write(0f); w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);
            w.WriteAsciiz(""); // SurfaceFile
            w.Write(0u); w.Write(0u); // NRenderFlags, RenderFlags
        }

        private static void WritePolygons(BinaryWriterEx w, P3DM_LOD mlodLod, int version)
        {
            var faces = mlodLod.Faces;
            w.Write(faces.Length);
            w.Write(0u); w.Write((ushort)0);

            foreach (var face in faces)
            {
                var verts = face.Vertices;
                var count = Math.Min(face.VertexCount, verts.Length); // clamp: broken MLODs may have count > actual verts
                w.Write((byte)count);
                for (int i = count - 1; i >= 0; i--) // reverse winding
                    if (version >= 69) w.Write(verts[i].PointIndex);
                    else w.Write((ushort)verts[i].PointIndex);
            }
        }

        private static void WriteSections(BinaryWriterEx w, P3DM_LOD mlodLod,
            string[] textures, Dictionary<string, int> texIdx,
            Dictionary<string, int> matIdx, int version)
        {
            var faces = mlodLod.Faces;
            var groups = new List<(int ti, int mi, string im, int start, int cnt)>();
            int pos = 0;
            while (pos < faces.Length)
            {
                var t = faces[pos].Texture ?? "";
                var m = faces[pos].Material ?? "";
                var ti = texIdx[t]; var mi = matIdx[m];
                var im = mi >= 0 ? null : m;
                int end = pos + 1;
                while (end < faces.Length && (faces[end].Texture ?? "") == t && (faces[end].Material ?? "") == m) end++;
                groups.Add((ti, mi, im, pos, end - pos));
                pos = end;
            }

            bool isShort = version < 69;
            uint fsize = isShort ? 8u : 16u;
            uint fpad = isShort ? 2u : 4u;

            uint run = 0;
            w.Write(groups.Count);
            foreach (var (ti, mi, im, start, cnt) in groups)
            {
                uint lo = run;
                for (int i = start; i < start + cnt; i++) { run += fsize; if (faces[i].VertexCount == 4) run += fpad; }
                w.Write((int)lo); w.Write((int)run); w.Write(0); w.Write(0); w.Write(0u);
                w.Write((short)ti); w.Write(0u); w.Write(mi);
                if (mi == -1) w.WriteAsciiz(im ?? "");
                if (version >= 36) { w.WriteArray(Array.Empty<float>()); if (version >= 67) w.Write(0); }
                else w.Write(1f);
            }
        }

        private static void CollectUVs(P3DM_LOD lod, float[][] uvs, bool[] hasUV)
        {
            var nVerts = uvs.Length;
            var uvTaggs = lod.Taggs.OfType<UVSetTagg>().ToArray();
            if (uvTaggs.Length > 0)
            {
                var fuvs = uvTaggs[0].FaceUVs;
                for (int f = 0; f < fuvs.Length && f < lod.Faces.Length; f++)
                {
                    var face = lod.Faces[f];
                    int count = Math.Min(face.VertexCount, face.Vertices.Length);
                    for (int v = 0; v < count; v++)
                    {
                        int pi = face.Vertices[v].PointIndex;
                        if (pi >= 0 && pi < nVerts && !hasUV[pi])
                        {
                            int rv = face.VertexCount - 1 - v;
                            uvs[pi] = [fuvs[f][rv, 0], fuvs[f][rv, 1]];
                            hasUV[pi] = true;
                        }
                    }
                }
            }
            else
            {
                foreach (var face in lod.Faces)
                {
                    int count = Math.Min(face.VertexCount, face.Vertices.Length);
                    for (int v = 0; v < count; v++)
                    {
                        int pi = face.Vertices[v].PointIndex;
                        if (pi >= 0 && pi < nVerts && !hasUV[pi])
                        {
                            uvs[pi] = [face.Vertices[v].U, face.Vertices[v].V];
                            hasUV[pi] = true;
                        }
                    }
                }
            }
        }

        private static void WriteUVSetDiscretized(BinaryWriterEx w, float[][] uvs, bool[] hasUV)
        {
            int n = uvs.Length;
            float minU = 0, minV = 0, maxU = 1, maxV = 1;
            bool any = false;
            for (int i = 0; i < n; i++) if (hasUV[i])
            {
                if (!any) { minU = maxU = uvs[i][0]; minV = maxV = uvs[i][1]; any = true; }
                else { if (uvs[i][0] < minU) minU = uvs[i][0]; if (uvs[i][1] < minV) minV = uvs[i][1];
                       if (uvs[i][0] > maxU) maxU = uvs[i][0]; if (uvs[i][1] > maxV) maxV = uvs[i][1]; }
            }
            w.Write(minU); w.Write(minV); w.Write(maxU); w.Write(maxV);
            w.Write((uint)n); w.Write(false);
            var b = new byte[n * 4];
            for (int i = 0; i < n; i++)
            {
                short su = hasUV[i] ? PackUV(uvs[i][0], minU, maxU) : (short)0;
                short sv = hasUV[i] ? PackUV(uvs[i][1], minV, maxV) : (short)0;
                BitConverter.GetBytes(su).CopyTo(b, i * 4);
                BitConverter.GetBytes(sv).CopyTo(b, i * 4 + 2);
            }
            w.WriteCompressed(b);
        }

        private static void WriteUVSetFloat(BinaryWriterEx w, float[][] uvs, bool[] hasUV)
        {
            int n = uvs.Length;
            w.Write((uint)n); w.Write(false);
            var b = new byte[n * 8];
            for (int i = 0; i < n; i++)
            {
                BitConverter.GetBytes(hasUV[i] ? uvs[i][0] : 0f).CopyTo(b, i * 8);
                BitConverter.GetBytes(hasUV[i] ? uvs[i][1] : 0f).CopyTo(b, i * 8 + 4);
            }
            w.WriteCompressed(b);
        }

        private static short PackUV(float value, float min, float max)
        {
            var range = max - min;
            if (range < 0.0001f) return 0;
            int s = (int)(((value - min) / range) * 65535f) - 32768;
            if (s < -32768) s = -32768; if (s > 32767) s = 32767;
            return (short)s;
        }

        private static int PackNormal(Vector3P normal)
        {
            float s = -511f;
            int x = (int)(normal.X * s), y = (int)(normal.Y * s), z = (int)(normal.Z * s);
            if (x > 511) x = 511; if (x < -512) x = -512;
            if (y > 511) y = 511; if (y < -512) y = -512;
            if (z > 511) z = 511; if (z < -512) z = -512;
            if (x < 0) x += 1024; if (y < 0) y += 1024; if (z < 0) z += 1024;
            return x | (y << 10) | (z << 20);
        }

        private static void WriteCompressedVertexIndexArray(BinaryWriterEx w, int version, int[] values)
        {
            if (version >= 69) { w.WriteCompressedIntArray(values); return; }
            using var mem = new MemoryStream();
            using (var ww = new BinaryWriterEx(mem))
                ww.WriteArrayBase(values, (b, v) => b.Write((ushort)v));
            w.Write(values.Length);
            if (values.Length > 0) w.WriteCompressed(mem.ToArray());
        }
    }
}
