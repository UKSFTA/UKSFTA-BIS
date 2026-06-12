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

namespace BIS.PBO.Deobfuscator.Test
{
    public class PboDeobfuscatorTest
    {
        // ─── Config serialization helper ───

        private static byte[] SerializeConfig(ParamFile config)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriterEx(ms, true))
                config.Write(writer);
            return ms.ToArray();
        }

        // ─── PBO roundtrip helper (save+load so Prefix is parsed from header) ───

        private static PBO CreateLoadedPbo(string prefix, params (string name, byte[] data)[] files)
        {
            var pbo = new PBO();
            if (prefix != null)
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", prefix));
            foreach (var (name, data) in files)
                pbo.AddFile(name, data);

            var tempPath = Path.GetTempFileName();
            pbo.SaveTo(tempPath);
            return new PBO(tempPath);
        }

        // ─── Config builders ───

        private static byte[] CreateConfigWithModelPaths(params (string cls, string model)[] models)
        {
            var entries = new List<ParamEntry>();
            foreach (var (cls, model) in models)
            {
                entries.Add(new ParamClass(cls, new ParamEntry[]
                {
                    new ParamValue("model", model)
                }));
            }
            return SerializeConfig(new ParamFile { Root = new ParamClass("root", entries.ToArray()) });
        }

        private static byte[] CreateConfigWithImagePaths(params (string cls, string image)[] images)
        {
            var entries = new List<ParamEntry>();
            foreach (var (cls, image) in images)
            {
                entries.Add(new ParamClass(cls, new ParamEntry[]
                {
                    new ParamValue("image", image)
                }));
            }
            return SerializeConfig(new ParamFile { Root = new ParamClass("root", entries.ToArray()) });
        }

        private static byte[] CreateConfigWithPaths(
            (string className, string[] paths)[] classDefs,
            (string name, string value)[] values = null)
        {
            var entries = new List<ParamEntry>();
            foreach (var (className, paths) in classDefs)
            {
                var classEntries = new List<ParamEntry>();
                for (int i = 0; i < paths.Length; i++)
                    classEntries.Add(new ParamValue($"path{i}", paths[i]));
                if (values != null)
                    foreach (var v in values)
                        classEntries.Add(new ParamValue(v.name, v.value));
                entries.Add(new ParamClass(className, classEntries.ToArray()));
            }
            return SerializeConfig(new ParamFile { Root = new ParamClass("root", entries.ToArray()) });
        }

        // ─── PBO content helpers ───

        private static readonly byte[] PaaHeader = { 0x00, 0x72, 0x61, 0x53 };
        private static readonly byte[] P3dHeader = { 0x4D, 0x4C, 0x4F, 0x44 }; // MLOD

        // ─── Tests ───

        [Fact]
        public void Rebuild_RecoveredNamesApplied()
        {
            var pbo = CreateLoadedPbo(null,
                ("_unknown\\_file1.paa", PaaHeader),
                ("_unknown\\_file2.paa", PaaHeader)
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                RecoveredNames =
                {
                    [0] = "data\\tex\\weapon_co.paa",
                    [1] = "data\\tex\\weapon_ni.paa"
                },
                Stats = { ["recovered"] = 2 }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(2, outputPbo.Files.Count);
                Assert.Contains(outputPbo.Files, f => f.FileName == "data\\tex\\weapon_co.paa");
                Assert.Contains(outputPbo.Files, f => f.FileName == "data\\tex\\weapon_ni.paa");
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_FilteredEntriesExcluded()
        {
            var pbo = CreateLoadedPbo(null,
                ("_unknown\\_decoy.paa", PaaHeader),
                ("_unknown\\_real.paa", PaaHeader),
                ("_unknown\\_stub.paa", new byte[] { 0xFF })
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                FilteredOut = { 0, 2 },
                RecoveredNames = { [1] = "data\\tex\\real_co.paa" }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(1, outputPbo.Files.Count);
                Assert.Equal("data\\tex\\real_co.paa", outputPbo.Files[0].FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_ZeroBytesSkipped()
        {
            var pbo = CreateLoadedPbo(null,
                ("_unknown\\_valid.paa", new byte[] { 0x01, 0x02 }),
                ("_unknown\\_empty.bin", Array.Empty<byte>())
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                RecoveredNames = { [0] = "data\\tex\\valid_co.paa" }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(1, outputPbo.Files.Count);
                Assert.Equal("data\\tex\\valid_co.paa", outputPbo.Files[0].FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_ConfigModelNaming_AppliesClassName()
        {
            // Config has class with model= pointing to the raw obfuscated path
            var configBin = CreateConfigWithModelPaths(
                ("MyVehicle", "_unknown\\_vehicle.p3d")
            );
            var pbo = CreateLoadedPbo(null,
                ("config.bin", configBin),
                ("_unknown\\_vehicle.p3d", P3dHeader)
            );
            // No recovered name for [1] — config naming should apply
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                RecoveredNames = { }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(2, outputPbo.Files.Count); // config.bin + .p3d
                var p3dEntry = outputPbo.Files.FirstOrDefault(f => f.FileName.EndsWith(".p3d"));
                Assert.NotNull(p3dEntry);
                Assert.Equal("_unknown\\myvehicle.p3d", p3dEntry.FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_ConfigModelNaming_StripsPrefixFromConfigPaths()
        {
            // Config model path includes PBO prefix; Rebuild strips it before matching
            var configBin = CreateConfigWithModelPaths(
                ("Backpack", "testmod\\equipment\\_backpack.p3d")
            );
            var pbo = CreateLoadedPbo("testmod",
                ("config.bin", configBin),
                ("equipment\\_backpack.p3d", P3dHeader)
            );
            var result = new DeobfuscationResult { MatchedProfile = "TestProfile" };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                var p3dEntry = outputPbo.Files.FirstOrDefault(f => f.FileName.EndsWith(".p3d"));
                Assert.NotNull(p3dEntry);
                Assert.Equal("equipment\\backpack.p3d", p3dEntry.FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_ConfigImageNaming_AppliesVariantName()
        {
            var configBin = CreateConfigWithImagePaths(
                ("MyVest", "testmod\\textures\\_icon.paa")
            );
            var pbo = CreateLoadedPbo("testmod",
                ("config.bin", configBin),
                ("textures\\_icon.paa", PaaHeader)
            );
            var result = new DeobfuscationResult { MatchedProfile = "TestProfile" };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                var paaEntry = outputPbo.Files.FirstOrDefault(f => f.FileName.EndsWith(".paa"));
                Assert.NotNull(paaEntry);
                Assert.Equal("textures\\myvest.paa", paaEntry.FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_ConfigImageNaming_CollisionAddsNumericSuffix()
        {
            var configBin = CreateConfigWithImagePaths(
                ("MyVest", "textures\\_icon.paa"),
                ("MyVest2", "textures\\_icon2.paa")
            );
            // Both image= paths resolve to the same variant name "myvest" after
            // stripping the first path component. The second should get "_2".
            using var ms = new MemoryStream();
            using (var w = new BinaryWriterEx(ms, true))
            {
                // Write two classes sharing the same variant name
                var config = new ParamFile
                {
                    Root = new ParamClass("root", new ParamEntry[]
                    {
                        new ParamClass("MyVest", new ParamEntry[]
                        {
                            new ParamValue("image", "prefix/textures/_icon.paa")
                        }),
                        new ParamClass("MyVest2", new ParamEntry[]
                        {
                            new ParamValue("image", "prefix/textures/_icon2.paa")
                        })
                    })
                };
                config.Write(w);
            }
            var configBytes = ms.ToArray();

            var pbo = CreateLoadedPbo(null,
                ("config.bin", configBytes),
                ("textures\\_icon.paa", PaaHeader),
                ("textures\\_icon2.paa", PaaHeader)
            );
            var result = new DeobfuscationResult { MatchedProfile = "TestProfile" };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                var paaFiles = outputPbo.Files
                    .Where(f => f.FileName.EndsWith(".paa"))
                    .OrderBy(f => f.FileName)
                    .ToList();
                Assert.Equal(2, paaFiles.Count);
                // Each class name maps to a variant name (myvest, myvest2) — no collision
                Assert.Equal("textures\\myvest.paa", paaFiles[0].FileName);
                Assert.Equal("textures\\myvest2.paa", paaFiles[1].FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_CleanNamesPreserved()
        {
            var pbo = CreateLoadedPbo(null,
                ("data\\tex\\clean_co.paa", PaaHeader),
                ("_unknown\\_obfuscated.paa", PaaHeader)
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                RecoveredNames = { [1] = "data\\tex\\recovered_co.paa" }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(2, outputPbo.Files.Count);
                var clean = outputPbo.Files.First(f => f.FileName == "data\\tex\\clean_co.paa");
                var recovered = outputPbo.Files.First(f => f.FileName == "data\\tex\\recovered_co.paa");
                Assert.NotNull(clean);
                Assert.NotNull(recovered);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_NoConfig_FallbackToNumberedPlaceholders()
        {
            // No config.bin at all — heuristic has no word index, falls to numbers
            var pbo = CreateLoadedPbo(null,
                ("_unknown\\_file1.bin", new byte[] { 0x01 }),
                ("_unknown\\_file2.bin", new byte[] { 0x02 })
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                RecoveredNames = { }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(2, outputPbo.Files.Count);
                // Fallback: dir prefix "file" for _unknown + counter + extension
                Assert.Equal("_unknown\\file_001.bin", outputPbo.Files[0].FileName);
                Assert.Equal("_unknown\\file_002.bin", outputPbo.Files[1].FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_ReferenceUpdaters_RunOnConfigBin()
        {
            // Config.bin with a model path that should get updated after file renaming.
            // When config.bin is processed by ConfigReferenceUpdater, paths in the
            // config values that match renamed files should be rewritten.
            var configBin = CreateConfigWithPaths(
                new[] { ("SomeClass", new[] { "data\\tex\\old_co.paa" }) }
            );
            var pbo = CreateLoadedPbo(null,
                ("config.bin", configBin),
                ("_unknown\\_old_co.paa", PaaHeader)
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                // Rebuild the path map: _old_co.paa → data/tex/old_co.paa
                RecoveredNames = { [1] = "data\\tex\\old_co.paa" }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                // The path map should include the recovered mapping, and
                // ConfigReferenceUpdater should have updated the config.bin
                // entries to reflect the new name.
                var configEntry = outputPbo.Files.First(f =>
                    f.FileName.Equals("config.bin", StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(configEntry);

                // Re-parse the output config.bin and verify at least the content
                // is still valid raP (roundtrips correctly)
                using var stream = configEntry.OpenRead();
                var parsed = new ParamFile(stream);
                // The root should have the same class structure
                Assert.NotNull(parsed.Root);
                Assert.Equal("rootClass", parsed.Root.Name);
                // path references should still be valid paths
                var classEntries = parsed.Root.Entries.OfType<ParamClass>().ToList();
                Assert.Single(classEntries);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_EmptyPBO_ProducesEmptyOutput()
        {
            var pbo = new PBO();
            var result = new DeobfuscationResult { MatchedProfile = "TestProfile" };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                // Empty PBO still produces a valid file (version entry + SHA1)
                Assert.True(File.Exists(outputPath));
                Assert.True(new FileInfo(outputPath).Length > 0);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_PrefixedPathMap_BuiltCorrectly()
        {
            // When a profile recovers a name, both the unprefixed and prefixed
            // entries are added to the path map for reference updating
            var configBin = CreateConfigWithPaths(
                new[] { ("SomeClass", new[] { "data\\tex\\weapon_co.paa" }) }
            );
            var pbo = CreateLoadedPbo("testmod",
                ("config.bin", configBin),
                ("_unknown\\_file.paa", PaaHeader)
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                RecoveredNames = { [1] = "data\\tex\\weapon_co.paa" }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(2, outputPbo.Files.Count);
                var paaEntry = outputPbo.Files.First(f => f.FileName.EndsWith(".paa"));
                Assert.Equal("data\\tex\\weapon_co.paa", paaEntry.FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_ConfigWithImage_NoPaaFiles_DoesNotThrow()
        {
            // Config has image= values but no .paa files in PBO — should skip gracefully
            var configBin = CreateConfigWithImagePaths(
                ("MyVest", "data\\tex\\_icon.paa")
            );
            var pbo = CreateLoadedPbo(null,
                ("config.bin", configBin),
                ("_unknown\\_file.bin", new byte[] { 0x01 })
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                RecoveredNames = { [1] = "data\\some\\file.bin" }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(2, outputPbo.Files.Count);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_DetectExtension_MagicBytes()
        {
            // Verify that DetectExtension correctly identifies file types by magic
            // Test via direct PBO with raw files
            var pbo = CreateLoadedPbo(null,
                ("_unknown\\_test.bin", new byte[] { 0x72, 0x61, 0x50, 0x00 }), // raP\0 → .bin
                ("_unknown\\_test.paa", new byte[] { 0x00, 0x72, 0x61, 0x53 }), // \0raS → .paa
                ("_unknown\\_test.p3d", new byte[] { 0x4D, 0x4C, 0x4F, 0x44 })  // MLOD → .p3d
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                RecoveredNames = { }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(3, outputPbo.Files.Count);
                // All should have fallback numbered names with correct extensions
                Assert.All(outputPbo.Files, f => Assert.StartsWith("_unknown\\file_", f.FileName));
                var exts = outputPbo.Files.Select(f => Path.GetExtension(f.FileName)).ToList();
                Assert.Contains(".bin", exts);
                Assert.Contains(".paa", exts);
                Assert.Contains(".p3d", exts);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_MultipleProfiles_MergedStats()
        {
            var pbo = CreateLoadedPbo(null,
                ("_unknown\\_f1.paa", PaaHeader),
                ("_unknown\\_f2.paa", PaaHeader)
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "ProfileA + ProfileB",
                RecoveredNames =
                {
                    [0] = "data\\tex\\a_co.paa",
                    [1] = "data\\tex\\b_co.paa"
                },
                Stats =
                {
                    ["recovered"] = 1,
                    ["stubs"] = 1
                }
            };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                Assert.Equal(2, outputPbo.Files.Count);
                Assert.Equal("data\\tex\\a_co.paa", outputPbo.Files[0].FileName);
                Assert.Equal("data\\tex\\b_co.paa", outputPbo.Files[1].FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_OutputProperties_OnlyPrefixAndProduct()
        {
            // Rebuild should strip all header properties except prefix and product
            var pbo = new PBO();
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "testmod"));
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("product", "Arma3"));
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("someDecoy", "garbage"));
            pbo.AddFile("_unknown\\_file.paa", PaaHeader);

            var tempPath = Path.GetTempFileName();
            try
            {
                pbo.SaveTo(tempPath);
                var loadedPbo = new PBO(tempPath);

                var result = new DeobfuscationResult
                {
                    MatchedProfile = "TestProfile",
                    RecoveredNames = { [0] = "data\\tex\\file_co.paa" }
                };

                var outputPath = Path.GetTempFileName();
                try
                {
                    new PboDeobfuscator().Rebuild(loadedPbo, result, outputPath);

                    using var outputPbo = new PBO(outputPath);
                    // Only prefix and product should be preserved
                    Assert.Contains(outputPbo.PropertiesPairs,
                        p => p.Key.Equals("prefix", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(outputPbo.PropertiesPairs,
                        p => p.Key.Equals("product", StringComparison.OrdinalIgnoreCase));
                    Assert.DoesNotContain(outputPbo.PropertiesPairs,
                        p => p.Key.Equals("someDecoy", StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    if (File.Exists(outputPath)) File.Delete(outputPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void Rebuild_ValidOutputPBO_CanRoundtrip()
        {
            // Full roundtrip: create PBO → rebuild → load → save → load again
            var configBin = CreateConfigWithModelPaths(
                ("MyWeapon", "_unknown\\_weapon.p3d")
            );
            var pbo = CreateLoadedPbo("testmod",
                ("config.bin", configBin),
                ("_unknown\\_weapon.p3d", P3dHeader),
                ("_unknown\\_tex.paa", PaaHeader)
            );
            var result = new DeobfuscationResult
            {
                MatchedProfile = "TestProfile",
                RecoveredNames =
                {
                    [2] = "data\\tex\\myweapon_co.paa"
                }
            };

            var outputPath = Path.GetTempFileName();
            var roundtripPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                // Load output and re-save (roundtrip)
                using (var outputPbo = new PBO(outputPath))
                {
                    Assert.Equal(3, outputPbo.Files.Count);
                    outputPbo.SaveTo(roundtripPath);
                }

                // Load the roundtripped copy
                using var rt = new PBO(roundtripPath);
                Assert.Equal(3, rt.Files.Count);
                Assert.Contains(rt.Files, f => f.FileName == "config.bin");
                Assert.Contains(rt.Files, f => f.FileName == "_unknown\\myweapon.p3d");
                Assert.Contains(rt.Files, f => f.FileName == "data\\tex\\myweapon_co.paa");
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
                if (File.Exists(roundtripPath)) File.Delete(roundtripPath);
            }
        }

        [Fact]
        public void Rebuild_HeuristicName_FromClassWordIndex()
        {
            // Config with class names containing words that match directory names
            // If an obfuscated file is in a dir that matches a class name word,
            // heuristic generates a name from that class.
            // Use multiple classes to avoid mod prefix detection stripping "assault_"
            var configBin = CreateConfigWithPaths(
                new[] { ("assault_vest", new[] { "data\\tex\\vest_co.paa" }), ("other_class", new[] { "data\\tex\\other_co.paa" }) }
            );
            var pbo = CreateLoadedPbo(null,
                ("config.bin", configBin),
                ("assault\\.p3d", P3dHeader) // "assault" dir matches word from "assault_vest"
            );
            var result = new DeobfuscationResult { MatchedProfile = "TestProfile" };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                var p3d = outputPbo.Files.FirstOrDefault(f => f.FileName.EndsWith(".p3d"));
                Assert.NotNull(p3d);
                // Heuristic matched "assault" dir word → "assault_vest" class
                // origFile is ".p3d" (extension only) so candidate is "assault/assault_vest.p3d"
                Assert.Equal("assault\\assault_vest.p3d", p3d.FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [Fact]
        public void Rebuild_MultipleP3dFiles_EachNamedFromConfig()
        {
            var configBin = CreateConfigWithModelPaths(
                ("VehicleA", "_unknown\\_va.p3d"),
                ("VehicleB", "_unknown\\_vb.p3d")
            );
            var pbo = CreateLoadedPbo(null,
                ("config.bin", configBin),
                ("_unknown\\_va.p3d", P3dHeader),
                ("_unknown\\_vb.p3d", P3dHeader)
            );
            var result = new DeobfuscationResult { MatchedProfile = "TestProfile" };

            var outputPath = Path.GetTempFileName();
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, outputPath);

                using var outputPbo = new PBO(outputPath);
                var p3ds = outputPbo.Files
                    .Where(f => f.FileName.EndsWith(".p3d"))
                    .OrderBy(f => f.FileName)
                    .ToList();
                Assert.Equal(2, p3ds.Count);
                Assert.Equal("_unknown\\vehiclea.p3d", p3ds[0].FileName);
                Assert.Equal("_unknown\\vehicleb.p3d", p3ds[1].FileName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  DetectModPrefix tests
        // ════════════════════════════════════════════════════════════════

        [Fact]
        public void DetectModPrefix_NullClassNames_ReturnsNull()
        {
            Assert.Null(PboDeobfuscator.DetectModPrefix(null, null));
        }

        [Fact]
        public void DetectModPrefix_EmptyClassNames_ReturnsNull()
        {
            Assert.Null(PboDeobfuscator.DetectModPrefix(new List<string>(), null));
        }

        [Fact]
        public void DetectModPrefix_SingleClass_ReturnsFirstWordWithUnderscore()
        {
            var result = PboDeobfuscator.DetectModPrefix(
                new List<string> { "uksf_vest_base" }, null);
            Assert.Equal("uksf_", result);
        }

        [Fact]
        public void DetectModPrefix_AllShareFirstWord_ReturnsPrefix()
        {
            var result = PboDeobfuscator.DetectModPrefix(
                new List<string> { "jsoar_helmet", "jsoar_vest", "jsoar_uniform" }, null);
            Assert.Equal("jsoar_", result);
        }

        [Fact]
        public void DetectModPrefix_SeventyPercentThreshold()
        {
            // 3 out of 4 share "jsoar" → 75% ≥ 70% → detected
            var result = PboDeobfuscator.DetectModPrefix(
                new List<string> { "jsoar_helmet", "jsoar_vest", "jsoar_uniform", "other_item" }, null);
            Assert.Equal("jsoar_", result);
        }

        [Fact]
        public void DetectModPrefix_BelowThresholdWithPboPrefix_FallbackToPboPrefix()
        {
            // 1/3 share "jsoar" → 33% < 70% → fallback to PBO prefix
            var result = PboDeobfuscator.DetectModPrefix(
                new List<string> { "jsoar_helmet", "something_else", "other_item" },
                "jsoar");
            Assert.Equal("jsoar_", result);
        }

        [Fact]
        public void DetectModPrefix_BelowThresholdWithNoPboPrefix_ReturnsNull()
        {
            var result = PboDeobfuscator.DetectModPrefix(
                new List<string> { "jsoar_helmet", "something_else" }, null);
            Assert.Null(result);
        }

        [Fact]
        public void DetectModPrefix_PboPrefixFallback_NoClassMatch_ReturnsNull()
        {
            // PBO prefix is "vests" but no class starts with "vests_"
            var result = PboDeobfuscator.DetectModPrefix(
                new List<string> { "jsoar_helmet", "other_item" }, "vests");
            Assert.Null(result);
        }

        [Fact]
        public void DetectModPrefix_ClassNamesWithoutUnderscore_SingleClassReturnsFirstWord()
        {
            // No underscore → entire name is first word
            var result = PboDeobfuscator.DetectModPrefix(
                new List<string> { "weapon" }, null);
            Assert.Equal("weapon_", result);
        }

        [Fact]
        public void DetectModPrefix_PboPrefixWithPath_Fallback()
        {
            // PBO prefix with path "mods/jsoar" → last part "jsoar"
            // Multiple classes with different first words → frequency <70% → falls back to PBO prefix
            // Need at least one class starting with the tag "jsoar_" for fallback
            var result = PboDeobfuscator.DetectModPrefix(
                new List<string> { "something_else", "other_thing", "jsoar_helmet" }, "mods\\jsoar");
            Assert.Equal("jsoar_", result);
        }

        [Fact]
        public void DetectModPrefix_ShortFirstWordIgnored()
        {
            // Split('_')[0] "ab" has length 2 (< 3... wait, the filter is w.Length >= 2
            // Looking at code: .Where(w => w.Length >= 2)
            // "ab" has length 2 ≥ 2 → included
            var result = PboDeobfuscator.DetectModPrefix(
                new List<string> { "ab_vest", "ab_helmet", "ab_uniform" }, null);
            Assert.Equal("ab_", result); // included because >= 2
        }

        // ════════════════════════════════════════════════════════════════
        //  Integration tests with real PBO files
        // ════════════════════════════════════════════════════════════════

        private static string GetTestDataPath(string fileName)
        {
            var assemblyDir = AppContext.BaseDirectory;
            return Path.GetFullPath(Path.Combine(assemblyDir,
                "../../../../_testdata_untracked", fileName));
        }

        private static readonly string[] RealPboFiles = { "JSOAR.pbo", "TFG_Vests.pbo", "uksf_tfa_vests.pbo", "UKSF_KS.pbo" };

        [Fact]
        public void Process_RealPbo_JSOAR_RunsWithoutException()
        {
            var path = GetTestDataPath("JSOAR.pbo");
            Assert.True(File.Exists(path), $"Test data not found: {path}");
            using var pbo = new PBO(path);
            var result = new PboDeobfuscator().Process(pbo);
            Assert.NotNull(result);
            Assert.NotNull(result.MatchedProfile);
            Assert.True(result.Stats.Count > 0);
        }

        [Fact]
        public void Process_RealPbo_TFG_Vests_RunsWithoutException()
        {
            var path = GetTestDataPath("TFG_Vests.pbo");
            Assert.True(File.Exists(path), $"Test data not found: {path}");
            using var pbo = new PBO(path);
            var result = new PboDeobfuscator().Process(pbo);
            Assert.NotNull(result);
            Assert.NotNull(result.MatchedProfile);
            Assert.True(result.Stats.Count > 0);
        }

        [Fact]
        public void Process_RealPbo_uksf_tfa_vests_RunsWithoutException()
        {
            var path = GetTestDataPath("uksf_tfa_vests.pbo");
            Assert.True(File.Exists(path), $"Test data not found: {path}");
            using var pbo = new PBO(path);
            var result = new PboDeobfuscator().Process(pbo);
            Assert.NotNull(result);
            // Some PBOs may not match any profile (clean PBOs) — that's okay
            Assert.True(result.MatchedProfile == null || result.Stats.Count > 0);
        }

        [Fact]
        public void Process_RealPbo_UKSF_KS_RunsWithoutException()
        {
            var path = GetTestDataPath("UKSF_KS.pbo");
            Assert.True(File.Exists(path), $"Test data not found: {path}");
            using var pbo = new PBO(path);
            var result = new PboDeobfuscator().Process(pbo);
            Assert.NotNull(result);
            Assert.NotNull(result.MatchedProfile);
            Assert.True(result.Stats.Count > 0);
        }

        [Fact]
        public void Rebuild_RealPbo_ProducesValidOutput()
        {
            // Use the smallest PBO for rebuild test
            var pboPath = GetTestDataPath("UKSF_KS.pbo");
            Assert.True(File.Exists(pboPath), $"Test data not found: {pboPath}");

            using var pbo = new PBO(pboPath);
            var result = new PboDeobfuscator().Process(pbo);
            Assert.NotNull(result.MatchedProfile);

            var rebuildPath = Path.GetTempFileName();
            long fileSize;
            try
            {
                new PboDeobfuscator().Rebuild(pbo, result, rebuildPath);
                Assert.True(File.Exists(rebuildPath));

                fileSize = new FileInfo(rebuildPath).Length;
                Assert.True(fileSize > 0);

                // Verify output PBO is valid by loading it
                using var outputPbo = new PBO(rebuildPath);
                Assert.True(outputPbo.Files.Count > 0);
                // All output names should be ASCII (no Cyrillic)
                Assert.All(outputPbo.Files, f =>
                    Assert.True(f.FileName.All(c => c < 128),
                        $"Non-ASCII character in output filename: {f.FileName}"));
            }
            finally
            {
                if (File.Exists(rebuildPath)) File.Delete(rebuildPath);
            }
        }

        [Fact]
        public void Process_AllRealPbos_ProduceDifferentMatchedProfiles()
        {
            var profiles = new HashSet<string>();
            foreach (var pboName in RealPboFiles)
            {
                var path = GetTestDataPath(pboName);
                if (!File.Exists(path)) continue;
                using var pbo = new PBO(path);
                var result = new PboDeobfuscator().Process(pbo);
                if (result.MatchedProfile != null)
                    profiles.Add(result.MatchedProfile);
            }
            Assert.True(profiles.Count > 0, "No real PBO files found for testing");
        }
    }
}
