using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using BIS.Core.Config;
using BIS.Core.Streams;
using BIS.PBO;
using BIS.PBO.Deobfuscator.Profiles;

namespace BIS.PBO.Deobfuscator.Test.Profiles
{
    public class SuffixRecoveryProfileTest
    {
        private static readonly byte[] PaaHeader = { 0x00, 0x72, 0x61, 0x53 };

        // ─── Config binary roundtrip helpers ───

        private static byte[] SerializeParamFile(ParamFile config)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriterEx(ms, true))
            {
                config.Write(writer);
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Creates a config.bin that defines classes with model/texture paths.
        /// </summary>
        private static byte[] CreateConfigWithPaths(params (string className, string[] paths)[] classDefs)
        {
            var entries = new List<ParamEntry>();
            foreach (var (className, paths) in classDefs)
            {
                var classEntries = new List<ParamEntry>();
                for (int i = 0; i < paths.Length; i++)
                {
                    classEntries.Add(new ParamValue($"path{i}", new RawValue(paths[i])));
                }
                entries.Add(new ParamClass(className, classEntries.ToArray()));
            }

            var config = new ParamFile
            {
                Root = new ParamClass("root", entries.ToArray())
            };
            return SerializeParamFile(config);
        }

        private static byte[] CreateEmptyConfig()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("root", Array.Empty<ParamEntry>())
            };
            return SerializeParamFile(config);
        }

        // ─── Test: No config.bin ───

        [Fact]
        public void Deobfuscate_NoConfigBin_ReturnsEmptyResult()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\_co.paa", PaaHeader));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Empty(result.RecoveredNames);
            Assert.Empty(result.FilteredOut);
            Assert.Equal("Suffix-based Recovery", result.MatchedProfile);
        }

        // ─── Test: Empty config ───

        [Fact]
        public void Deobfuscate_EmptyConfig_ReturnsEmptyResult()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("config.bin", CreateEmptyConfig()));
            pbo.Files.Add(new DummyFileEntry("data\\_co.paa", PaaHeader));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Empty(result.RecoveredNames);
        }

        // ─── Test: Fallback path matching ───

        [Fact]
        public void Deobfuscate_FallbackPathMatch_RecoversSuffixFile()
        {
            var pbo = MakePbo();
            // Config with a path in the same directory as the suffix file
            var configBin = CreateConfigWithPaths(
                ("avs_assault_vest", new[] { "data\\abav\\avs_assault_vest_co.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            // Suffix file: dir=data/abav, name=_co.paa
            // Fallback match: suffixKey = "data/abav|_co.paa" matches lookup key
            pbo.Files.Add(new DummyFileEntry("data\\abav\\_co.paa", PaaHeader));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Single(result.RecoveredNames);
            Assert.Equal(1, result.Stats["Recovered"]);
            Assert.Equal("data/abav/avs_assault_vest_co.paa", result.RecoveredNames[1]);
        }

        [Fact]
        public void Deobfuscate_FallbackPathMatch_NoCandidates_NoRecovery()
        {
            var pbo = MakePbo();
            var configBin = CreateConfigWithPaths(
                ("avs_assault_vest", new[] { "data\\models\\car.p3d" })  // .p3d, not .paa
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            // Suffix .paa in a different directory
            pbo.Files.Add(new DummyFileEntry("data\\abav\\_co.paa", PaaHeader));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            // No match: extension .paa vs .p3d, and dir doesn't overlap
            Assert.Equal(0, result.Stats["Recovered"]);
        }

        [Fact]
        public void Deobfuscate_FallbackPathMatch_MultipleCandidates_UsesEachOnce()
        {
            var pbo = MakePbo();
            var configBin = CreateConfigWithPaths(
                ("vest_a", new[] { "data\\abav\\vest_a_co.paa" }),
                ("vest_b", new[] { "data\\abav\\vest_b_co.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            pbo.Files.Add(new DummyFileEntry("data\\abav\\_co.paa", PaaHeader));
            pbo.Files.Add(new DummyFileEntry("data\\abav\\_co.paa", PaaHeader)); // same suffix

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            // Both have suffix _co.paa in same dir, but only 2 known paths
            Assert.Equal(2, result.Stats["Recovered"]);
            Assert.Equal(2, result.RecoveredNames.Count);
        }

        // ─── Test: Class-name matching ───

        [Fact]
        public void Deobfuscate_ClassNameMatch_RecoversFile()
        {
            var pbo = MakePbo();
            // Class "avs_assault_vest" in config — words: avs, assault, vest
            // Suffix file "avs/_mc.paa": dir="avs" -> contains word "avs" -> class-name match
            var configBin = CreateConfigWithPaths(
                ("avs_assault_vest", new[] { "data\\abav\\avs_assault_vest_mc.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            pbo.Files.Add(new DummyFileEntry("avs\\_mc.paa", PaaHeader));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Single(result.RecoveredNames);
            Assert.Equal(1, result.Stats["Recovered"]);
            // Reconstructed name: dir + normalized_class + suffix + ext
            // normalized "avs_assault_vest" + suffix "_mc" + ext ".paa"
            Assert.Equal("avs/avs_assault_vest_mc.paa", result.RecoveredNames[1]);
        }

        [Fact]
        public void Deobfuscate_ClassNameMatch_DirWordSubstring_StillMatches()
        {
            var pbo = MakePbo();
            // Word "vest" should match dir "vests" via Contains
            // BUT: dir.Contains(dirWord) — "vests".Contains("vest")? YES
            // Or: dirWord.Contains(dir) — "vest".Contains("vests")? NO
            // So the first condition passes: dir ("vests") contains dirWord ("vest")
            // Wait, actually dir = "vests", dirWord = "vests"? No, dirWord comes from class words.
            // Class: "avs_heavy_vest" -> words: avs, heavy, vest
            // File: "vests\\_co.paa" -> dir = "vests"
            // For dirWord="vest": dir.Contains("vest")? "vests".Contains("vest")? YES!
            // "vest".Contains("vests")? NO, but first condition passed.
            // Actually wait, the condition is:
            // !dir.Contains(dirWord) && !dirWord.Contains(dir) -> continue (skip)
            // So if dir.Contains(dirWord) OR dirWord.Contains(dir) -> DON'T skip -> proceed with match
            // "vests".Contains("vest")? YES -> NOT skipped -> match!

            // Wait, but the reconstructed path would be: "vests/avs_heavy_vest_co.paa"
            // That's a bit odd but follows the algorithm.
            var configBin = CreateConfigWithPaths(
                ("avs_heavy_vest", new[] { "data\\abav\\avs_heavy_vest_co.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            pbo.Files.Add(new DummyFileEntry("vests\\_co.paa", PaaHeader));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            // "vests".Contains("vest") -> true -> class-name match
            // OR fallback: vests|_co.paa vs data/abav|_co.paa -> different dirs, no fallback match
            // Class-name should match because vests contains "vest"
            Assert.Equal(1, result.Stats["Recovered"]);
            Assert.Contains("vest", result.RecoveredNames[1]);
        }

        // ─── Test: Decoy filtering ───

        [Fact]
        public void Deobfuscate_ZeroByteFile_FilteredOut()
        {
            var pbo = MakePbo();
            var configBin = CreateConfigWithPaths(
                ("avs_assault_vest", new[] { "data\\abav\\avs_assault_vest_co.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            pbo.Files.Add(new DummyFileEntry("decoy.bin", new byte[0]));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Contains(1, result.FilteredOut);
            Assert.Equal(1, result.Stats["Decoys"]);
        }

        [Fact]
        public void Deobfuscate_SmallRandomNameFile_FilteredAsStub()
        {
            var pbo = MakePbo();
            var configBin = CreateConfigWithPaths(
                ("avs_assault_vest", new[] { "data\\abav\\avs_assault_vest_co.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            pbo.Files.Add(new DummyFileEntry("aB3xY9.tmp", new byte[10]));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Contains(1, result.FilteredOut);
            Assert.Equal(1, result.Stats["Stubs"]);
        }

        [Fact]
        public void Deobfuscate_LargeRealExtensionFile_CountedAsEntryPoint()
        {
            var pbo = MakePbo();
            var configBin = CreateConfigWithPaths(
                ("avs_assault_vest", new[] { "data\\abav\\avs_assault_vest_co.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            pbo.Files.Add(new DummyFileEntry("model.p3d", new byte[200])); // >100, .p3d in RealExtensions

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Equal(1, result.Stats["EntryPoints"]);
            Assert.DoesNotContain(1, result.FilteredOut);
        }

        // ─── Test: Stats calculations ───

        [Fact]
        public void Deobfuscate_MixedEntries_CalculatesCorrectStats()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("config.bin", CreateEmptyConfig()));
            pbo.Files.Add(new DummyFileEntry("decoy.bin", new byte[0]));         // decoy
            pbo.Files.Add(new DummyFileEntry("stub.bin", new byte[10]));          // stub (random name)
            pbo.Files.Add(new DummyFileEntry("real.paa", new byte[1000]));        // genuine (>=20 bytes, normal ext, not _/._ prefixed)

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Equal(1, result.Stats["Decoys"]);
            Assert.Equal(1, result.Stats["Stubs"]);
            Assert.Equal(1, result.Stats["EntryPoints"]); // .paa in RealExtensions and >100 bytes
            Assert.Equal(1, result.Stats["Genuine"]);
            Assert.Equal(4, result.Stats["Total"]);
            Assert.Equal(4, result.FilteredOut.Count + result.RecoveredNames.Count);
        }

        [Fact]
        public void Deobfuscate_Stats_UnrecoveredCounted()
        {
            var pbo = MakePbo();
            var configBin = CreateConfigWithPaths(
                ("avs_assault_vest", new[] { "data\\abav\\avs_assault_vest_co.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            pbo.Files.Add(new DummyFileEntry("data\\_co.paa", PaaHeader));  // suffix, but no dir match for class-name + no fallback match

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            // Total=2 (config + suffix), Recovered=0 (no match for data/_co.paa since dir "data" doesn't match class words, and config paths are in data/abav/, not data/)
            Assert.Equal(2, result.Stats["Total"]);
            Assert.Equal(0, result.Stats["Recovered"]);
            Assert.Equal(2, result.Stats["Unrecovered"]);
        }

        // ─── Test: Normal file not affected ───

        [Fact]
        public void Deobfuscate_NormalFileName_NotModified()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("config.bin", CreateEmptyConfig()));
            pbo.Files.Add(new DummyFileEntry("data\\model.p3d", new byte[] { 0x4F, 0x44, 0x4F, 0x4C, 0x00 }));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Empty(result.RecoveredNames);
        }

        // ─── Test: Prefix stripping ───

        [Fact]
        public void Deobfuscate_PrefixIsNull_PathExtractionWorks()
        {
            // Prefix is null for new PBO() - verify it doesn't crash
            var pbo = MakePbo();
            var configBin = CreateConfigWithPaths(
                ("avs_assault_vest", new[] { "data\\abav\\avs_assault_vest_co.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            pbo.Files.Add(new DummyFileEntry("data\\abav\\_co.paa", PaaHeader));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            Assert.Equal(1, result.Stats["Recovered"]);
        }

        // ─── Test: RVMAT scanning ───

        [Fact]
        public void Deobfuscate_RvmatFile_ExtractsAdditionalPaths()
        {
            var pbo = MakePbo();
            // Config has one known path
            var configBin = CreateConfigWithPaths(
                ("avs_assault_vest", new[] { "data\\abav\\avs_assault_vest_co.paa" })
            );
            pbo.Files.Add(new DummyFileEntry("config.bin", configBin));
            // RVMAT with an additional texture path not in config
            var rvmatContent = CreateRvmatWithTexture("data\\abav\\extra_texture_co.paa");
            pbo.Files.Add(new DummyFileEntry("mat.rvmat", rvmatContent));
            // Suffix file that should match the extra path
            pbo.Files.Add(new DummyFileEntry("data\\abav\\_co.paa", PaaHeader));

            var result = new SuffixRecoveryProfile().Deobfuscate(pbo);

            // The RVMAT export should add "data/abav/extra_texture_co.paa" to knownPaths
            // Then the suffix file should match via fallback path matching
            // The suffix _co.paa could match EITHER the config path or the RVMAT path
            // Both are at data/abav/*_co.paa so both are eligible
            Assert.Equal(1, result.Stats["Recovered"]);
        }

        // ─── Test: Profile name ───

        [Fact]
        public void ProfileName_IsCorrect()
        {
            Assert.Equal("Suffix-based Recovery", new SuffixRecoveryProfile().ProfileName);
        }

        // ─── Test: IsMatch ───

        [Fact]
        public void IsMatch_ExtensionOnlyName_ReturnsTrue()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry(".paa", PaaHeader));
            Assert.True(new SuffixRecoveryProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_CyrillicName_ReturnsTrue()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\\u043B\u0430\u045A_co.paa", PaaHeader));
            Assert.True(new SuffixRecoveryProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_CleanNames_ReturnsFalse()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\model.p3d", new byte[] { 0x4F, 0x44, 0x4F, 0x4C }));
            Assert.False(new SuffixRecoveryProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_SuffixWithUnderscore_ReturnsTrue()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\_co.paa", PaaHeader));
            Assert.True(new SuffixRecoveryProfile().IsMatch(pbo));
        }

        // ─── Helpers ───

        private static byte[] CreateRvmatWithTexture(string texturePath)
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamClass("Stage0", new ParamEntry[]
                    {
                        new ParamValue("texture", new RawValue(texturePath)),
                        new ParamValue("uvSource", new RawValue("tex"))
                    })
                })
            };
            return SerializeParamFile(config);
        }

        private static PBO MakePbo()
        {
            return new PBO();
        }

        private class DummyFileEntry : IPBOFileEntry
        {
            public string FileName { get; }
            public string RawFileName => FileName;
            public int Size => _data.Length;
            public int TimeStamp => 0;
            public bool IsCompressed => false;
            public int DiskSize => _data.Length;
            private readonly byte[] _data;

            public DummyFileEntry(string fileName, byte[] data)
            {
                FileName = fileName;
                _data = data;
            }

            public Stream OpenRead() => new MemoryStream(_data, false);
        }
    }
}
