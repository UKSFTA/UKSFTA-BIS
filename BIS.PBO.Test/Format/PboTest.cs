using System;
using Xunit;
using BIS.PBO;
using BIS.Core.Streams;
using System.IO;
using System.Text;

namespace BIS.PBO.Test.Format
{
    public class PboTest
    {
        [Fact]
        public void FileEntry_Read_ShouldParseCorrectly()
        {
            var ms = new MemoryStream();
            var writer = new BinaryWriterEx(ms);

            writer.WriteAsciiz("test_file.txt");
            writer.Write(0);
            writer.Write(1024);
            writer.Write(2048);
            writer.Write(12345);
            writer.Write(1024);

            ms.Position = 0;
            var reader = new BinaryReaderEx(ms);
            var entry = new FileEntry(reader);

            Assert.Equal("test_file.txt", entry.FileName);
            Assert.Equal(0, entry.CompressedMagic);
            Assert.Equal(1024, entry.UncompressedSize);
            Assert.Equal(2048, entry.StartOffset);
            Assert.Equal(12345, entry.TimeStamp);
            Assert.Equal(1024, entry.DataSize);
        }

        [Fact]
        public void PBO_ReadHeader_WithInvalidFileName_ShouldSanitize()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                using (var fs = File.OpenWrite(tempFile))
                {
                    var writer = new BinaryWriterEx(fs);

                    writer.WriteAsciiz("");
                    writer.Write(FileEntry.VersionMagic);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    writer.WriteAsciiz("prefix");
                    writer.WriteAsciiz("test_prefix");
                    writer.Write((byte)0);

                    writer.WriteAsciiz("bad*file.txt");
                    writer.Write(0);
                    writer.Write(10);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(10);

                    writer.WriteAsciiz("..\\secret.txt");
                    writer.Write(0);
                    writer.Write(20);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(20);

                    writer.WriteAsciiz("*?<>");
                    writer.Write(0);
                    writer.Write(4);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(4);

                    writer.WriteAsciiz("");
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    var dummyData = new byte[34];
                    dummyData[0] = 0x00;
                    dummyData[1] = (byte)'r';
                    dummyData[2] = (byte)'a';
                    dummyData[3] = (byte)'S';
                    writer.Write(dummyData);
                }

                var pbo = new PBO(tempFile);
                Assert.Equal(3, pbo.Files.Count);

                Assert.Equal("badfile.txt", pbo.Files[0].FileName);
                Assert.Equal("secret.txt", pbo.Files[1].FileName);

                Assert.Equal("_unknown\\_unknown_file0.paa", pbo.Files[2].FileName);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public void AddFile_WithValidNameAndData_CreatesEntry()
        {
            var pbo = new PBO();
            byte[] data = [1, 2, 3, 4];

            pbo.AddFile("test.bin", data);

            Assert.Single(pbo.Files);
            Assert.Equal("test.bin", pbo.Files[0].FileName);
            Assert.Equal(data, ((PBOFileInMemory)pbo.Files[0]).Data);
        }

        [Fact]
        public void AddFile_MultipleFiles_AllAdded()
        {
            var pbo = new PBO();
            pbo.AddFile("a.txt", [0]);
            pbo.AddFile("b.txt", [1]);

            Assert.Equal(2, pbo.Files.Count);
        }

        [Fact]
        public void AddFile_NullFileName_Throws()
        {
            var pbo = new PBO();

            Assert.Throws<ArgumentNullException>(() => pbo.AddFile(null!, [0]));
        }

        [Fact]
        public void AddFile_NullData_Throws()
        {
            var pbo = new PBO();

            Assert.Throws<ArgumentNullException>(() => pbo.AddFile("a.txt", null!));
        }

        [Fact]
        public void RemoveFile_ByName_RemovesAndReturnsTrue()
        {
            var pbo = new PBO();
            pbo.AddFile("keep.txt", [0]);
            pbo.AddFile("remove.txt", [1]);

            bool result = pbo.RemoveFile("remove.txt");

            Assert.True(result);
            Assert.Single(pbo.Files);
            Assert.Equal("keep.txt", pbo.Files[0].FileName);
        }

        [Fact]
        public void RemoveFile_ByName_CaseInsensitive()
        {
            var pbo = new PBO();
            pbo.AddFile("Test.Bin", [0]);

            Assert.True(pbo.RemoveFile("test.bin"));
            Assert.Empty(pbo.Files);
        }

        [Fact]
        public void RemoveFile_ByName_NormalizesForwardSlashes()
        {
            var pbo = new PBO();
            pbo.AddFile(@"folder\file.txt", [0]);

            Assert.True(pbo.RemoveFile("folder/file.txt"));
            Assert.Empty(pbo.Files);
        }

        [Fact]
        public void RemoveFile_ByName_NotFound_ReturnsFalse()
        {
            var pbo = new PBO();
            pbo.AddFile("a.txt", [0]);

            Assert.False(pbo.RemoveFile("nonexistent.txt"));
            Assert.Single(pbo.Files);
        }

        [Fact]
        public void RemoveFile_ByIndex_RemovesAtPosition()
        {
            var pbo = new PBO();
            pbo.AddFile("a.txt", [0]);
            pbo.AddFile("b.txt", [1]);

            pbo.RemoveFile(0);

            Assert.Single(pbo.Files);
            Assert.Equal("b.txt", pbo.Files[0].FileName);
        }

        [Fact]
        public void RemoveFile_ByIndex_OutOfRange_Throws()
        {
            var pbo = new PBO();

            Assert.Throws<ArgumentOutOfRangeException>(() => pbo.RemoveFile(0));
        }

        [Fact]
        public void FindFile_ReturnsMatchingEntry()
        {
            var pbo = new PBO();
            pbo.AddFile("target.dll", [0xBB]);

            var result = pbo.FindFile("target.dll");

            Assert.NotNull(result);
            Assert.Equal("target.dll", result.FileName);
        }

        [Fact]
        public void FindFile_CaseInsensitive()
        {
            var pbo = new PBO();
            pbo.AddFile("UpperCase.Bin", [0]);

            Assert.NotNull(pbo.FindFile("uppercase.bin"));
        }

        [Fact]
        public void FindFile_NormalizesForwardSlashes()
        {
            var pbo = new PBO();
            pbo.AddFile(@"a\b\c.paa", [0]);

            Assert.NotNull(pbo.FindFile("a/b/c.paa"));
        }

        [Fact]
        public void FindFile_NotFound_ReturnsNull()
        {
            var pbo = new PBO();
            pbo.AddFile("a.txt", [0]);

            Assert.Null(pbo.FindFile("missing.txt"));
        }

        [Fact]
        public void FindFile_EmptyPBO_ReturnsNull()
        {
            var pbo = new PBO();

            Assert.Null(pbo.FindFile("anything.paa"));
        }

        [Fact]
        public void GetFile_ReturnsMatchingEntry()
        {
            var pbo = new PBO();
            pbo.AddFile("config.bin", [0]);

            var result = pbo.GetFile("config.bin");

            Assert.NotNull(result);
            Assert.Equal("config.bin", result.FileName);
        }

        [Fact]
        public void GetFile_NotFound_ThrowsKeyNotFound()
        {
            var pbo = new PBO();

            Assert.Throws<KeyNotFoundException>(() => pbo.GetFile("missing.bin"));
        }

        [Fact]
        public void AddFileAndSave_Roundtrip_PreservesData()
        {
            var pbo = new PBO();
            byte[] originalData = [0xDE, 0xAD, 0xBE, 0xEF];
            pbo.AddFile("payload.dll", originalData);

            var tempFile = Path.GetTempFileName();
            try
            {
                pbo.SaveTo(tempFile);

                using (var loaded = new PBO(tempFile))
                {
                    var entry = loaded.FindFile("payload.dll");
                    Assert.NotNull(entry);
                    Assert.Equal(originalData.Length, entry.Size);

                    using var stream = entry.OpenRead();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    Assert.Equal(originalData, ms.ToArray());
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        private static void CreateTestFile(string dir, string name, string content)
        {
            var path = Path.Combine(dir, name);
            var parent = Path.GetDirectoryName(path);
            if (parent is not null)
                Directory.CreateDirectory(parent);
            File.WriteAllText(path, content);
        }

        private static void CreateTestFile(string dir, string name, byte[] data)
        {
            var path = Path.Combine(dir, name);
            var parent = Path.GetDirectoryName(path);
            if (parent is not null)
                Directory.CreateDirectory(parent);
            File.WriteAllBytes(path, data);
        }

        [Fact]
        public void PackRoundtrip_SingleFile_ListShowsEntry()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pbo_rt_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                CreateTestFile(dir, "readme.txt", "hello arma");
                var pboPath = Path.Combine(Path.GetTempPath(), "test_single.pbo");

                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test_single"));
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(dir, "readme.txt")), "readme.txt"));
                pbo.SaveTo(pboPath);

                try
                {
                    var loaded = new PBO(pboPath);
                    Assert.Single(loaded.Files);
                    Assert.Equal("readme.txt", loaded.Files[0].FileName);
                }
                finally
                {
                    File.Delete(pboPath);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void PackRoundtrip_MultipleFiles_ListShowsAll()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pbo_rt_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                CreateTestFile(dir, "config.cpp", "class CfgPatches {};");
                CreateTestFile(dir, Path.Combine("data", "texture.paa"), "dummy");
                CreateTestFile(dir, Path.Combine("scripts", "init.sqf"), "systemChat 'hello';");

                var pboPath = Path.Combine(Path.GetTempPath(), "test_multi.pbo");

                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test_multi"));
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(dir, "config.cpp")), "config.cpp"));
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(dir, "data", "texture.paa")), @"data\texture.paa"));
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(dir, "scripts", "init.sqf")), @"scripts\init.sqf"));
                pbo.SaveTo(pboPath);

                try
                {
                    var loaded = new PBO(pboPath);
                    Assert.Equal(3, loaded.Files.Count);

                    var names = loaded.Files.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    Assert.Contains("config.cpp", names);
                    Assert.Contains(@"data\texture.paa", names);
                    Assert.Contains(@"scripts\init.sqf", names);
                }
                finally
                {
                    File.Delete(pboPath);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void PackRoundtrip_Extract_ContentPreserved()
        {
            var srcDir = Path.Combine(Path.GetTempPath(), "pbo_src_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(srcDir);
            try
            {
                var content = "Mission config data\nline 2\nline 3";
                CreateTestFile(srcDir, "description.ext", content);

                var pboPath = Path.Combine(Path.GetTempPath(), "test_extract.pbo");
                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test_extract"));
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(srcDir, "description.ext")), "description.ext"));
                pbo.SaveTo(pboPath);

                try
                {
                    using (var loaded = new PBO(pboPath))
                    {
                        var outDir = Path.Combine(Path.GetTempPath(), "pbo_out_" + Guid.NewGuid().ToString("N")[..8]);
                        Directory.CreateDirectory(outDir);
                        try
                        {
                            loaded.ExtractFiles(loaded.Files, outDir);
                            var extracted = File.ReadAllText(Path.Combine(outDir, "description.ext"));
                            Assert.Equal(content, extracted);
                        }
                        finally
                        {
                            Directory.Delete(outDir, true);
                        }
                    }
                }
                finally
                {
                    File.Delete(pboPath);
                }
            }
            finally
            {
                Directory.Delete(srcDir, true);
            }
        }

        [Fact]
        public void PackRoundtrip_Compressed_EntriesShowCompressed()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pbo_rt_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                var largeContent = new string('A', 2048);
                CreateTestFile(dir, "large.txt", largeContent);

                var pboPath = Path.Combine(Path.GetTempPath(), "test_comp.pbo");
                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test_comp"));
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(dir, "large.txt")), "large.txt"));
                pbo.SaveTo(pboPath, compress: true);

                try
                {
                    var loaded = new PBO(pboPath);
                    Assert.Single(loaded.Files);
                    Assert.True(loaded.Files[0].IsCompressed);
                }
                finally
                {
                    File.Delete(pboPath);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void PackRoundtrip_Prefix_PropertyPreserved()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pbo_rt_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                CreateTestFile(dir, "config.cpp", "class CfgPatches {};");

                var pboPath = Path.Combine(Path.GetTempPath(), "test_prefix.pbo");
                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "my_addon\\data"));
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(dir, "config.cpp")), "config.cpp"));
                pbo.SaveTo(pboPath);

                try
                {
                    var loaded = new PBO(pboPath);
                    Assert.Equal("my_addon\\data", loaded.Prefix);
                    Assert.Contains(loaded.PropertiesPairs,
                        kv => kv.Key == "prefix" && kv.Value == "my_addon\\data");
                }
                finally
                {
                    File.Delete(pboPath);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void PackRoundtrip_EmptyPBO_ValidWithPrefix()
        {
            var pboPath = Path.Combine(Path.GetTempPath(), "test_empty.pbo");
            var pbo = new PBO();
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "empty_addon"));
            pbo.SaveTo(pboPath);

            try
            {
                Assert.True(File.Exists(pboPath));
                Assert.True(new FileInfo(pboPath).Length > 0);

                var rawBytes = File.ReadAllBytes(pboPath);
                var rawText = Encoding.UTF8.GetString(rawBytes);
                Assert.Contains("prefix", rawText);
                Assert.Contains("empty_addon", rawText);
            }
            finally
            {
                File.Delete(pboPath);
            }
        }

        [Fact]
        public void PackRoundtrip_SubdirectoryPaths_RelativePathsCorrect()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pbo_rt_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                CreateTestFile(dir, Path.Combine("ui", "icons", "gear_ca.paa"), "icon");
                CreateTestFile(dir, Path.Combine("sounds", "weapons", "rifle_fire.wss"), "sound");

                var pboPath = Path.Combine(Path.GetTempPath(), "test_paths.pbo");
                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test_paths"));
                pbo.Files.Add(new PBOFileToAdd(
                    new FileInfo(Path.Combine(dir, "ui", "icons", "gear_ca.paa")),
                    @"ui\icons\gear_ca.paa"));
                pbo.Files.Add(new PBOFileToAdd(
                    new FileInfo(Path.Combine(dir, "sounds", "weapons", "rifle_fire.wss")),
                    @"sounds\weapons\rifle_fire.wss"));
                pbo.SaveTo(pboPath);

                try
                {
                    var loaded = new PBO(pboPath);
                    Assert.Equal(2, loaded.Files.Count);

                    var names = loaded.Files.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    Assert.Contains(@"ui\icons\gear_ca.paa", names);
                    Assert.Contains(@"sounds\weapons\rifle_fire.wss", names);
                }
                finally
                {
                    File.Delete(pboPath);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void PackRoundtrip_Repack_LoadedPBO_SaveToNewFile_PreservesEntries()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pbo_rt_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                CreateTestFile(dir, "mission.sqm", "version=12;");
                CreateTestFile(dir, "script_component.hpp", "#define COMPONENT main");

                var pboPath1 = Path.Combine(Path.GetTempPath(), "test_repack1.pbo");
                var pbo1 = new PBO();
                pbo1.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test_repack"));
                pbo1.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(dir, "mission.sqm")), "mission.sqm"));
                pbo1.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(dir, "script_component.hpp")), "script_component.hpp"));
                pbo1.SaveTo(pboPath1);

                try
                {
                    using (var loaded = new PBO(pboPath1))
                    {
                        Assert.Equal(2, loaded.Files.Count);

                        var pboPath2 = Path.Combine(Path.GetTempPath(), "test_repack2.pbo");
                        loaded.SaveTo(pboPath2);

                        try
                        {
                            using (var reloaded = new PBO(pboPath2))
                            {
                                Assert.Equal(2, reloaded.Files.Count);

                                var names = reloaded.Files.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                                Assert.Contains("mission.sqm", names);
                                Assert.Contains("script_component.hpp", names);
                            }
                        }
                        finally
                        {
                            File.Delete(pboPath2);
                        }
                    }
                }
                finally
                {
                    File.Delete(pboPath1);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void PackRoundtrip_BinaryContent_ExtractPreservesBytes()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pbo_rt_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                var binaryData = new byte[256];
                for (int i = 0; i < binaryData.Length; i++)
                    binaryData[i] = (byte)(i ^ 0xAA);
                CreateTestFile(dir, "binary.dat", binaryData);

                var pboPath = Path.Combine(Path.GetTempPath(), "test_binary.pbo");
                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test_binary"));
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(Path.Combine(dir, "binary.dat")), "binary.dat"));
                pbo.SaveTo(pboPath);

                try
                {
                    using (var loaded = new PBO(pboPath))
                    {
                        var outDir = Path.Combine(Path.GetTempPath(), "pbo_out_" + Guid.NewGuid().ToString("N")[..8]);
                        Directory.CreateDirectory(outDir);
                        try
                        {
                            loaded.ExtractFiles(loaded.Files, outDir);
                            var extracted = File.ReadAllBytes(Path.Combine(outDir, "binary.dat"));
                            Assert.Equal(binaryData, extracted);
                        }
                        finally
                        {
                            Directory.Delete(outDir, true);
                        }
                    }
                }
                finally
                {
                    File.Delete(pboPath);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void PackRoundtrip_MultipleSubdirs_ExtractPreservesStructure()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pbo_rt_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                CreateTestFile(dir, "addons\\weapons\\config.cpp", "class Weapons {};");
                CreateTestFile(dir, "addons\\weapons\\data\\m16.paa", "m16tex");
                CreateTestFile(dir, "addons\\vehicles\\config.cpp", "class Vehicles {};");
                CreateTestFile(dir, "addons\\vehicles\\scripts\\init.sqf", "nil");

                var pboPath = Path.Combine(Path.GetTempPath(), "test_struct.pbo");
                var pbo = new PBO();
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test_struct"));
                pbo.Files.Add(new PBOFileToAdd(
                    new FileInfo(Path.Combine(dir, "addons\\weapons\\config.cpp")), @"addons\weapons\config.cpp"));
                pbo.Files.Add(new PBOFileToAdd(
                    new FileInfo(Path.Combine(dir, "addons\\weapons\\data\\m16.paa")), @"addons\weapons\data\m16.paa"));
                pbo.Files.Add(new PBOFileToAdd(
                    new FileInfo(Path.Combine(dir, "addons\\vehicles\\config.cpp")), @"addons\vehicles\config.cpp"));
                pbo.Files.Add(new PBOFileToAdd(
                    new FileInfo(Path.Combine(dir, "addons\\vehicles\\scripts\\init.sqf")), @"addons\vehicles\scripts\init.sqf"));
                pbo.SaveTo(pboPath);

                try
                {
                    using (var loaded = new PBO(pboPath))
                    {
                        var outDir = Path.Combine(Path.GetTempPath(), "pbo_out_" + Guid.NewGuid().ToString("N")[..8]);
                        Directory.CreateDirectory(outDir);
                        try
                        {
                            loaded.ExtractFiles(loaded.Files, outDir);

                            Assert.True(File.Exists(Path.Combine(outDir, "addons", "weapons", "config.cpp")));
                            Assert.True(File.Exists(Path.Combine(outDir, "addons", "weapons", "data", "m16.paa")));
                            Assert.True(File.Exists(Path.Combine(outDir, "addons", "vehicles", "config.cpp")));
                            Assert.True(File.Exists(Path.Combine(outDir, "addons", "vehicles", "scripts", "init.sqf")));

                            Assert.Equal("class Weapons {};",
                                File.ReadAllText(Path.Combine(outDir, "addons", "weapons", "config.cpp")));
                            Assert.Equal("class Vehicles {};",
                                File.ReadAllText(Path.Combine(outDir, "addons", "vehicles", "config.cpp")));
                        }
                        finally
                        {
                            Directory.Delete(outDir, true);
                        }
                    }
                }
                finally
                {
                    File.Delete(pboPath);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
