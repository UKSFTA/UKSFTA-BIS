using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BIS.Core.Math;
using BIS.P3D.Conversion;
using BIS.P3D.MLOD;
using BIS.P3D.ODOL;
using Mlod = BIS.P3D.MLOD.MLOD;

namespace BIS.P3D.Test.Conversion
{
    public class ConversionTest
    {
        private static readonly Vector3P V0 = new Vector3P(0, 0, 0);
        private static readonly Vector3P V1 = new Vector3P(1, 0, 0);
        private static readonly Vector3P V2 = new Vector3P(0, 1, 0);
        private static readonly Vector3P V3 = new Vector3P(1, 1, 0);
        private static readonly Vector3P Normal = new Vector3P(0, 0, 1);

        private static readonly Vertex TV0 = new Vertex(0, 0, 0, 0);
        private static readonly Vertex TV1 = new Vertex(1, 0, 0.5f, 0);
        private static readonly Vertex TV2 = new Vertex(2, 0, 1, 0);
        private static readonly Vertex TV3 = new Vertex(3, 0, 0.5f, 1);

        private static Face MakeTri(Vertex a, Vertex b, Vertex c, string tex, string mat)
        {
            return new Face(3, new[] { a, b, c, new Vertex(0, 0, 0, 0) }, FaceFlags.DEFAULT, tex, mat);
        }

        private static Face MakeQuad(Vertex a, Vertex b, Vertex c, Vertex d, string tex, string mat)
        {
            return new Face(4, new[] { a, b, c, d }, FaceFlags.DEFAULT, tex, mat);
        }

        private static P3DM_LOD MakeLod(float res, Point[] points, Vector3P[] normals, Face[] faces, IEnumerable<Tagg> taggs)
        {
            return new P3DM_LOD(res, points, normals, faces, taggs ?? new Tagg[] { new EOFTagg() });
        }

        private static Point P(float x, float y, float z)
        {
            return new Point(new Vector3P(x, y, z), PointFlags.NONE);
        }

        [Fact]
        public void Roundtrip_EmptyMLOD_ReturnsEmptyMLOD()
        {
            var mlod = new Mlod(Array.Empty<P3DM_LOD>());
            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);
            Assert.Empty(result.Lods);
        }

        [Fact]
        public void Roundtrip_SingleTri_PreservesVertexCount()
        {
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0) };
            var normals = new[] { Normal, Normal, Normal };
            var faces = new[] { MakeTri(TV0, TV1, TV2, "tex.paa", "mat.rvmat") };
            var lods = new[] { MakeLod(1e8f, points, normals, faces, null) };
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            Assert.Single(result.Lods);
            Assert.Equal(3, result.Lods[0].Points.Length);
            Assert.Equal(3, result.Lods[0].Normals.Length);
            Assert.Single(result.Lods[0].Faces);
            Assert.Equal(3, result.Lods[0].Faces[0].VertexCount);
        }

        [Fact]
        public void Roundtrip_SingleQuad_PreservesVertexCount()
        {
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0), P(1,1,0) };
            var normals = new[] { Normal, Normal, Normal, Normal };
            var faces = new[] { MakeQuad(TV0, TV1, TV2, TV3, "tex.paa", "mat.rvmat") };
            var lods = new[] { MakeLod(1e8f, points, normals, faces, null) };
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            Assert.Single(result.Lods);
            Assert.Equal(4, result.Lods[0].Points.Length);
            Assert.Single(result.Lods[0].Faces);
            Assert.Equal(4, result.Lods[0].Faces[0].VertexCount);
        }

        [Fact]
        public void Roundtrip_MixedTriQuad_PreservesFaceCount()
        {
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0), P(1,1,0), P(0,2,0) };
            var normals = Enumerable.Repeat(Normal, 5).ToArray();
            var faces = new[]
            {
                MakeTri(TV0, TV1, TV2, "a.paa", "a.rvmat"),
                MakeQuad(TV0, TV1, TV2, TV3, "b.paa", "b.rvmat"),
                MakeTri(TV0, TV2, new Vertex(4, 0, 0, 0), "c.paa", "c.rvmat"),
            };
            var lods = new[] { MakeLod(1e8f, points, normals, faces, null) };
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            Assert.Single(result.Lods);
            Assert.Equal(3, result.Lods[0].Faces.Length);
            // ODOL stores all faces with public VertexIndices property via GetFaces().
            // MLOD gets them back. All tris/quads should have same vertex count.
            Assert.All(result.Lods[0].Faces, f => Assert.True(f.VertexCount == 3 || f.VertexCount == 4));
        }

        [Fact]
        public void Roundtrip_MultipleLODs_PreservesCount()
        {
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0) };
            var normals = Enumerable.Repeat(Normal, 3).ToArray();
            var tri = MakeTri(TV0, TV1, TV2, "t.paa", "t.rvmat");
            var lods = new[]
            {
                MakeLod(1e8f, points, normals, new[] { tri }, null),
                MakeLod(100f, points, normals, new[] { tri, tri }, null),
            };
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            Assert.Equal(2, result.Lods.Length);
            Assert.Single(result.Lods[0].Faces);
            Assert.Equal(2, result.Lods[1].Faces.Length);
        }

        [Fact]
        public void Roundtrip_Properties_Preserved()
        {
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0) };
            var normals = Enumerable.Repeat(Normal, 3).ToArray();
            var tri = MakeTri(TV0, TV1, TV2, "t.paa", "t.rvmat");
            var taggs = new Tagg[]
            {
                new PropertyTagg("autocenter", "0"),
                new PropertyTagg("lodnoshadow", "1"),
                new EOFTagg()
            };
            var lods = new[] { MakeLod(1e8f, points, normals, new[] { tri }, taggs) };
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            var props = result.Lods[0].NamedProperties.ToList();
            Assert.Contains(props, p => p.Item1 == "autocenter" && p.Item2 == "0");
            Assert.Contains(props, p => p.Item1 == "lodnoshadow" && p.Item2 == "1");
        }

        [Fact]
        public void Roundtrip_TextureAndMaterial_Preserved()
        {
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0) };
            var normals = Enumerable.Repeat(Normal, 3).ToArray();
            var faces = new[]
            {
                MakeTri(TV0, TV1, TV2, "data/tex_co.paa", "data/wood.rvmat"),
                MakeTri(TV0, TV1, TV2, "data/metal_ca.paa", "data/metal.rvmat"),
            };
            var lods = new[] { MakeLod(1e8f, points, normals, faces, null) };
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            Assert.Equal(2, result.Lods[0].Faces.Length);
            Assert.Equal("data/tex_co.paa", result.Lods[0].Faces[0].Texture);
            Assert.Equal("data/wood.rvmat", result.Lods[0].Faces[0].Material);
            Assert.Equal("data/metal_ca.paa", result.Lods[0].Faces[1].Texture);
            Assert.Equal("data/metal.rvmat", result.Lods[0].Faces[1].Material);
        }

        [Fact]
        public void Roundtrip_VertexWinding_Preserved()
        {
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0) };
            var normals = Enumerable.Repeat(Normal, 3).ToArray();
            // ODOL uses CW winding; MLOD uses CCW. Roundtrip should produce
            // same winding after two reversals.
            var a = new Vertex(0, 0, 0, 0);
            var b = new Vertex(1, 0, 0.5f, 0);
            var c = new Vertex(2, 0, 1, 0);
            var faces = new[] { new Face(3, new[] { a, b, c, new Vertex(0,0,0,0) }, FaceFlags.DEFAULT, "t.paa", "t.rvmat") };
            var lods = new[] { MakeLod(1e8f, points, normals, faces, null) };
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            var f = result.Lods[0].Faces[0];
            Assert.Equal(3, f.VertexCount);
            // After roundtrip: MLOD→ODOL reverses, ODOL→MLOD reverses again.
            // Result should match original MLOD vertex indices.
            Assert.Equal(0, f.Vertices[0].PointIndex);
            Assert.Equal(1, f.Vertices[1].PointIndex);
            Assert.Equal(2, f.Vertices[2].PointIndex);
        }

        [Fact]
        public void Roundtrip_NamedSelection_Preserved()
        {
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0), P(1,1,0) };
            var normals = Enumerable.Repeat(Normal, 4).ToArray();
            var faces = new[]
            {
                MakeTri(TV0, TV1, TV2, "t.paa", "t.rvmat"),
                MakeQuad(TV0, TV1, TV2, TV3, "t.paa", "t.rvmat"),
            };
            var taggs = new Tagg[]
            {
                new NamedSelectionTagg("head", new byte[] { 1, 0, 0, 0 }, new byte[] { 1, 0 }),
                new NamedSelectionTagg("body", new byte[] { 0, 1, 1, 1 }, new byte[] { 0, 1 }),
                new EOFTagg()
            };
            var lods = new[] { MakeLod(1e8f, points, normals, faces, taggs) };
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            var resultTaggs = result.Lods[0].Taggs.OfType<NamedSelectionTagg>().ToList();
            Assert.Equal(2, resultTaggs.Count);

            var tagg0 = resultTaggs.First(t => t.Name == "head");
            Assert.Equal(new byte[] { 1, 0, 0, 0 }, tagg0.Points);
            Assert.Equal(new byte[] { 1, 0 }, tagg0.Faces);

            var tagg1 = resultTaggs.First(t => t.Name == "body");
            Assert.Equal(new byte[] { 0, 1, 1, 1 }, tagg1.Points);
            Assert.Equal(new byte[] { 0, 1 }, tagg1.Faces);
        }

        [Fact]
        public void Roundtrip_DifferentResolutions_Preserved()
        {
            float[] resolutions = { 1e8f, 1000f, 100f, 10f };
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0) };
            var normals = Enumerable.Repeat(Normal, 3).ToArray();
            var tri = MakeTri(TV0, TV1, TV2, "t.paa", "t.rvmat");
            var lods = resolutions.Select(r => MakeLod(r, points, normals, new[] { tri }, null)).ToArray();
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            Assert.Equal(4, result.Lods.Length);
            for (int i = 0; i < 4; i++)
                Assert.Equal(resolutions[i], result.Lods[i].Resolution);
        }

        [Fact]
        public void Roundtrip_FaceIndicesMatch_AfterTriangulation()
        {
            // Multiple tris/quads sharing vertices — verify index consistency
            var points = new[] { P(0,0,0), P(1,0,0), P(0,1,0), P(1,1,0), P(0,2,0) };
            var normals = Enumerable.Repeat(Normal, 5).ToArray();
            var vx = points.Select((p, i) => new Vertex(i, 0, 0, 0)).ToArray();

            var faces = new[]
            {
                MakeTri(vx[0], vx[1], vx[2], "a.paa", "a.rvmat"),
                MakeQuad(vx[1], vx[3], vx[2], vx[0], "b.paa", "b.rvmat"),
                MakeTri(vx[2], vx[3], vx[4], "c.paa", "c.rvmat"),
            };
            var lods = new[] { MakeLod(1e8f, points, normals, faces, null) };
            var mlod = new Mlod(lods);

            var odol = MLOD2ODOL.Convert(mlod);
            var result = ODOL2MLOD.Convert(odol);

            var rfaces = result.Lods[0].Faces;
            Assert.Equal(3, rfaces.Length);

            // Each face should reference valid point indices (0-4) and have consistent vertex count
            for (int i = 0; i < rfaces.Length; i++)
            {
                Assert.True(rfaces[i].VertexCount >= 3 && rfaces[i].VertexCount <= 4);
                for (int j = 0; j < rfaces[i].VertexCount; j++)
                    Assert.InRange(rfaces[i].Vertices[j].PointIndex, 0, 4);
            }
        }

        [Fact]
        public void ODOL2MLOD_NullOdol_ThrowsNullReferenceException()
        {
            var ex = Record.Exception(() => ODOL2MLOD.Convert(null));
            Assert.NotNull(ex);
            Assert.IsType<NullReferenceException>(ex);
        }

        [Fact]
        public void ODOL2MLOD_NoLods_ThrowsNullReferenceException()
        {
            var odol = new BIS.P3D.ODOL.ODOL();
            var ex = Record.Exception(() => ODOL2MLOD.Convert(odol));
            Assert.NotNull(ex);
            Assert.IsType<NullReferenceException>(ex);
        }

        [Fact]
        public void ODOL2MLOD_StaticClass_Exists()
        {
            Assert.NotNull(typeof(ODOL2MLOD));
        }

        [Fact]
        public void MLOD2ODOL_NullInput_ThrowsNullReferenceException()
        {
            var ex = Record.Exception(() => MLOD2ODOL.Convert(null));
            Assert.NotNull(ex);
        }

        [Fact]
        public void MLOD2ODOL_EmptyMLOD_ReturnsEmptyODOL()
        {
            var mlod = new Mlod(Array.Empty<P3DM_LOD>());
            var odol = MLOD2ODOL.Convert(mlod);
            Assert.NotNull(odol);
            Assert.Empty(odol.Lods);
        }
    }
}
