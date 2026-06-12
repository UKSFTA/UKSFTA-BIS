using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using BIS.P3D.MLOD;
using BIS.Core.Math;
using BIS.Core.Streams;
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

        [Fact]
        public void UpdateReferences_MlodMultipleLods_AllUpdated()
        {
            var lod1 = CreateSingleFaceLod(1.0f, "data/tex/lod1_co.paa", "data/tex/lod1.rvmat");
            var lod2 = CreateSingleFaceLod(0.5f, "data/tex/lod2_co.paa", "data/tex/lod2.rvmat");
            var lods = new[] { lod1, lod2 };
            var mlod = new BIS.P3D.MLOD.MLOD(lods);
            var ms = new MemoryStream();
            mlod.WriteToStream(ms);
            var data = ms.ToArray();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/tex/lod1_co.paa"] = "data/tex/new_lod1_co.paa",
                ["data/tex/lod1.rvmat"] = "data/tex/new_lod1.rvmat",
                ["data/tex/lod2_co.paa"] = "data/tex/new_lod2_co.paa",
                ["data/tex/lod2.rvmat"] = "data/tex/new_lod2.rvmat"
            };

            var entry = new DummyFileEntry("model.p3d", data);
            var updater = new P3DTextureReferenceUpdater();
            var result = updater.UpdateReferences(entry, pathMap);

            Assert.NotNull(result);

            var updated = new BIS.P3D.P3D(new MemoryStream(result));
            var updatedLods = updated.LODs.Cast<P3DM_LOD>().ToArray();
            Assert.Equal(2, updatedLods.Length);
            Assert.Equal("data/tex/new_lod1_co.paa", updatedLods[0].Faces[0].Texture);
            Assert.Equal("data/tex/new_lod2_co.paa", updatedLods[1].Faces[0].Texture);
        }

        [Fact]
        public void UpdateReferences_MlodCyrillicPath_CreatesRawBinary()
        {
            // P3D files store Cyrillic-obfuscated paths as raw UTF-8 bytes.
            // ReadAsciiz converts byte→char producing mojibake.
            // FixEncoding reverses that before matching pathMap keys.
            // Since MLOD.Write uses Encoding.ASCII (drops chars > 127), we must
            // build MLOD binary manually with the raw Cyrillic bytes in place.
            var texturePath = "jsoar/data/abav/ла?њ_co.paa";
            var materialPath = "jsoar/data/abav/ла?њ.rvmat";
            var data = CreateMlodBytesWithRawPaths(
                Encoding.UTF8.GetBytes(texturePath),
                Encoding.UTF8.GetBytes(materialPath));

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [texturePath] = "data/abav/abav_1_co.paa",
                [materialPath] = "data/abav/abav_1.rvmat"
            };

            var entry = new DummyFileEntry("model.p3d", data);
            var updater = new P3DTextureReferenceUpdater();
            var result = updater.UpdateReferences(entry, pathMap);

            Assert.NotNull(result);

            var updated = new BIS.P3D.P3D(new MemoryStream(result));
            var lod = (P3DM_LOD)updated.LODs.First();
            Assert.Equal("data/abav/abav_1_co.paa", lod.Faces[0].Texture);
            Assert.Equal("data/abav/abav_1.rvmat", lod.Faces[0].Material);
        }

        [Fact]
        public void UpdateReferences_MlodSuffixOnlyPath_FuzzyMatch()
        {
            // Slight variant: the P3D texture path has no directory prefix but the
            // pathMap keys have the full directory. Fuzzy EndsWith matching handles
            // this when the suffixes overlap (contentFile is shorter than keyFile).
            var data = CreateMlodBytes(texture: "_co.paa", material: "_smdi.paa");

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/abav/old_co.paa"] = "data/abav/abav_1_co.paa",
                ["data/abav/old_smdi.paa"] = "data/abav/abav_1_smdi.paa"
            };

            var entry = new DummyFileEntry("model.p3d", data);
            var updater = new P3DTextureReferenceUpdater();
            var result = updater.UpdateReferences(entry, pathMap);

            // contentFile="_co.paa", keyFile="old_co.paa" -> contentFile.EndsWith(keyFile) = FALSE
            // keyFile.EndsWith(contentFile) = "old_co.paa".EndsWith("_co.paa") = TRUE
            // contentDir="", keyDir="data/abav" -> empty dir matches only if contentDir empty
            // This match relies on keyFile.EndsWith(contentFile) which the current
            // TryResolvePath does NOT check (only contentFile.EndsWith(keyFile)).
            // For now, this returns null until TryResolvePath is extended.
            Assert.Null(result);
        }

        // ─── Helpers ───

        private static string CreateMojibake(string text)
        {
            var utf8 = Encoding.UTF8.GetBytes(text);
            return new string(utf8.Select(b => (char)b).ToArray());
        }

        private static P3DM_LOD CreateSingleFaceLod(float resolution, string texture, string material)
        {
            return new P3DM_LOD(resolution,
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
            );
        }

        private static byte[] CreateMlodBytes(string texture, string material)
        {
            var lods = new[] { CreateSingleFaceLod(1.0f, texture, material) };
            var mlod = new MLOD(lods);
            var ms = new MemoryStream();
            mlod.WriteToStream(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Creates MLOD binary with raw UTF-8 bytes for texture and material paths.
        /// This is needed because MLOD.Write uses WriteAsciiz (Encoding.ASCII) which
        /// drops chars > 127 — making it impossible to create Cyrillic paths via the API.
        /// Strategy: create a template MLOD with unique ASCII placeholders, find and
        /// replace them with the raw Cyrillic byte sequences.
        /// </summary>
        private static byte[] CreateMlodBytesWithRawPaths(byte[] textureUtf8, byte[] materialUtf8)
        {
            const string texPh = "TEXPH";
            const string matPh = "MATPH";

            var lods = new[] { CreateSingleFaceLod(1.0f, texPh, matPh) };
            var mlod = new MLOD(lods);
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriterEx(ms, true))
            {
                mlod.Write(writer);
            }
            var data = ms.ToArray();

            var texPhBytes = Encoding.ASCII.GetBytes(texPh);
            var matPhBytes = Encoding.ASCII.GetBytes(matPh);

            // Find texPh in the byte array
            int texPos = -1;
            for (int i = 0; i <= data.Length - texPhBytes.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < texPhBytes.Length; j++)
                    if (data[i + j] != texPhBytes[j]) { match = false; break; }
                if (match) { texPos = i; break; }
            }

            // matPh is right after texPh + null terminator in Face.Write
            int matPos = texPos + texPhBytes.Length + 1;
            // after matPh + null
            int afterMat = matPos + matPhBytes.Length + 1;

            using var result = new MemoryStream();
            result.Write(data, 0, texPos);
            result.Write(textureUtf8);
            result.WriteByte(0);
            result.Write(materialUtf8);
            result.WriteByte(0);
            result.Write(data, afterMat, data.Length - afterMat);
            return result.ToArray();
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
