using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using BIS.P3D.MLOD;
using BIS.Core.Math;
using BIS.PBO;

namespace BIS.PBO.Deobfuscator.Test
{
    public class P3DTextureReferenceUpdaterTest
    {
        // ─── FixEncoding Unit Tests ───

        [Fact]
        public void FixEncoding_PureAscii_PassesThrough()
        {
            var input = "a3/data_f/env_land_ca.paa";
            Assert.Same(input, P3DTextureReferenceUpdater.FixEncoding(input));
        }

        [Fact]
        public void FixEncoding_CyrillicSuffix_DecodesCorrectly()
        {
            var mojibake = CreateMojibake("ла?њ_co.paa");
            var result = P3DTextureReferenceUpdater.FixEncoding(mojibake);
            Assert.Equal("ла?њ_co.paa", result);
        }

        [Fact]
        public void FixEncoding_FullCyrillicPath_DecodesCorrectly()
        {
            var original = "jsoar/data/abav/ла?њ_co.paa";
            var mojibake = CreateMojibake(original);
            var result = P3DTextureReferenceUpdater.FixEncoding(mojibake);
            Assert.Equal(original, result);
        }

        [Fact]
        public void FixEncoding_EmptyString_ReturnsEmpty()
        {
            Assert.Equal("", P3DTextureReferenceUpdater.FixEncoding(""));
        }

        [Fact]
        public void FixEncoding_InvalidUtf8Bytes_DoesNotThrow()
        {
            var input = new string(new[] { (char)0xFF, (char)0xFE, (char)0x80 });
            var result = P3DTextureReferenceUpdater.FixEncoding(input);
            Assert.NotNull(result);
        }

        // ─── MLOD Integration Tests ───

        [Fact]
        public void UpdateReferences_MlodExactMatch_ResolvesTextureAndMaterial()
        {
            var data = CreateMlodBytes(
                texture: "data/tex/old_co.paa",
                material: "data/tex/old.rvmat"
            );

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/tex/old_co.paa"] = "data/tex/new_co.paa",
                ["data/tex/old.rvmat"] = "data/tex/new.rvmat"
            };

            var entry = new DummyFileEntry("model.p3d", data);
            var updater = new P3DTextureReferenceUpdater();
            var result = updater.UpdateReferences(entry, pathMap);

            Assert.NotNull(result);

            var updated = new BIS.P3D.P3D(new MemoryStream(result));
            var lod = (P3DM_LOD)updated.LODs.First();
            Assert.Equal("data/tex/new_co.paa", lod.Faces[0].Texture);
            Assert.Equal("data/tex/new.rvmat", lod.Faces[0].Material);
        }

        [Fact]
        public void UpdateReferences_MlodFuzzyMatch_WithDirectoryPrefix()
        {
            var data = CreateMlodBytes(
                texture: "jsoar/data/tex/old_co.paa",
                material: "jsoar/data/tex/old.rvmat"
            );

            // pathMap keys WITHOUT the jsoar/ prefix => should match via EndsWith logic
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/tex/old_co.paa"] = "data/tex/new_co.paa",
                ["data/tex/old.rvmat"] = "data/tex/new.rvmat"
            };

            var entry = new DummyFileEntry("model.p3d", data);
            var updater = new P3DTextureReferenceUpdater();
            var result = updater.UpdateReferences(entry, pathMap);

            Assert.NotNull(result);

            var updated = new BIS.P3D.P3D(new MemoryStream(result));
            var lod = (P3DM_LOD)updated.LODs.First();
            Assert.Equal("data/tex/new_co.paa", lod.Faces[0].Texture);
            Assert.Equal("data/tex/new.rvmat", lod.Faces[0].Material);
        }

        [Fact]
        public void UpdateReferences_MlodNoMatch_ReturnsNull()
        {
            var data = CreateMlodBytes(
                texture: "data/tex/old_co.paa",
                material: "data/tex/old.rvmat"
            );

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["unrelated/path.paa"] = "new/path.paa"
            };

            var entry = new DummyFileEntry("model.p3d", data);
            var updater = new P3DTextureReferenceUpdater();
            var result = updater.UpdateReferences(entry, pathMap);

            Assert.Null(result);
        }

        [Fact]
        public void UpdateReferences_NonP3DEntry_ReturnsNull()
        {
            var entry = new DummyFileEntry("file.txt", new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F });
            var updater = new P3DTextureReferenceUpdater();
            Assert.Null(updater.UpdateReferences(entry, new Dictionary<string, string>()));
        }

        [Fact]
        public void UpdateReferences_InvalidP3DData_ReturnsNull()
        {
            var entry = new DummyFileEntry("model.p3d", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
            var updater = new P3DTextureReferenceUpdater();
            Assert.Null(updater.UpdateReferences(entry, new Dictionary<string, string>()));
        }

        // ─── Helpers ───

        private static string CreateMojibake(string text)
        {
            var utf8 = Encoding.UTF8.GetBytes(text);
            return new string(utf8.Select(b => (char)b).ToArray());
        }

        private static byte[] CreateMlodBytes(string texture, string material)
        {
            var lods = new P3DM_LOD[]
            {
                new P3DM_LOD(1.0f,
                    new Point[]
                    {
                        new Point(new Vector3P(0, 0, 0), PointFlags.NONE),
                        new Point(new Vector3P(1, 0, 0), PointFlags.NONE),
                        new Point(new Vector3P(0, 1, 0), PointFlags.NONE),
                    },
                    new Vector3P[]
                    {
                        new Vector3P(0, 0, 1),
                        new Vector3P(0, 0, 1),
                        new Vector3P(0, 0, 1),
                    },
                    new Face[]
                    {
                        new Face(3,
                            new Vertex[]
                            {
                                new Vertex(0, 0, 0, 0),
                                new Vertex(1, 1, 1, 0),
                                new Vertex(2, 2, 0, 1),
                                new Vertex(0, 0, 0, 0),
                            },
                            FaceFlags.DEFAULT,
                            texture,
                            material
                        )
                    },
                    new List<Tagg> { new EOFTagg() }
                )
            };

            var mlod = new MLOD(lods);
            var ms = new MemoryStream();
            mlod.WriteToStream(ms);
            return ms.ToArray();
        }

        private class DummyFileEntry : IPBOFileEntry
        {
            public string FileName { get; }
            public string RawFileName => FileName;
            public int Size { get; }
            public int TimeStamp => 0;
            public bool IsCompressed => false;
            public int DiskSize => Size;
            private readonly byte[] _data;

            public DummyFileEntry(string fileName, byte[] data)
            {
                FileName = fileName;
                _data = data;
                Size = data.Length;
            }

            public Stream OpenRead() => new MemoryStream(_data, false);
        }
    }
}
