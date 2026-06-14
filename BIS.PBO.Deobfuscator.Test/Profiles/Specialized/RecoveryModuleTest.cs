using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using BIS.PBO;
using BIS.PBO.Deobfuscator;
using BIS.PBO.Deobfuscator.Profiles.Specialized;

namespace BIS.PBO.Deobfuscator.Test.Profiles.Specialized
{
    public class RecoveryModuleTest
    {
        private static readonly byte[] PaaHeader = { 0x00, 0x72, 0x61, 0x53 };

        // ─── CyrillicDetectionModule ───

        [Fact]
        public void CyrillicDetectionModule_CyrillicInFilename_ReturnsTrue()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\\u043B\u0430\u045A_co.paa", PaaHeader));

            Assert.True(new CyrillicDetectionModule().IsMatch(pbo));
        }

        [Fact]
        public void CyrillicDetectionModule_AsciiOnly_ReturnsFalse()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\tex_co.paa", PaaHeader));

            Assert.False(new CyrillicDetectionModule().IsMatch(pbo));
        }

        [Fact]
        public void CyrillicDetectionModule_EmptyPbo_ReturnsFalse()
        {
            Assert.False(new CyrillicDetectionModule().IsMatch(new PBO()));
        }

        [Fact]
        public void CyrillicDetectionModule_ModuleName_IsCorrect()
        {
            Assert.Equal("Cyrillic Detection", new CyrillicDetectionModule().ModuleName);
        }

        // ─── DecoyDetectionModule ───

        [Fact]
        public void DecoyDetectionModule_LongPropsAndZeroBytes_ReturnsTrue()
        {
            var pbo = MakePbo();
            for (int i = 0; i < 3; i++)
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>(
                    new string('x', 41), new string('y', 41)));
            pbo.Files.Add(new DummyFileEntry("dummy.bin", new byte[0]));

            Assert.True(new DecoyDetectionModule().IsMatch(pbo));
        }

        [Fact]
        public void DecoyDetectionModule_OnlyLongProps_ReturnsFalse()
        {
            var pbo = MakePbo();
            for (int i = 0; i < 3; i++)
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>(
                    new string('x', 41), new string('y', 41)));

            Assert.False(new DecoyDetectionModule().IsMatch(pbo));
        }

        [Fact]
        public void DecoyDetectionModule_OnlyZeroBytes_ReturnsFalse()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("dummy.bin", new byte[0]));

            Assert.False(new DecoyDetectionModule().IsMatch(pbo));
        }

        [Fact]
        public void DecoyDetectionModule_ModuleName_IsCorrect()
        {
            Assert.Equal("Decoy Detection", new DecoyDetectionModule().ModuleName);
        }

        // ─── DecoyFilteringModule ───

        [Fact]
        public void DecoyFilteringModule_ZeroByteFile_FiltersOut()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("decoy.bin", new byte[0]));
            var result = new DeobfuscationResult();

            new DecoyFilteringModule().Recover(pbo, result, new List<string>(), "");

            Assert.Contains(0, result.FilteredOut);
            Assert.Equal(1, result.Stats["Decoys"]);
        }

        [Fact]
        public void DecoyFilteringModule_SmallRandomName_Stub()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("aB3xY9.bin", new byte[10]));
            var result = new DeobfuscationResult();

            new DecoyFilteringModule().Recover(pbo, result, new List<string>(), "");

            Assert.Contains(0, result.FilteredOut);
            Assert.Equal(1, result.Stats["Stubs"]);
        }

        [Fact]
        public void DecoyFilteringModule_NormalFile_NotFiltered()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\model.rvmat", new byte[1000]));
            var result = new DeobfuscationResult();

            new DecoyFilteringModule().Recover(pbo, result, new List<string>(), "");

            Assert.Empty(result.FilteredOut);
        }

        [Fact]
        public void DecoyFilteringModule_EmptyPbo_NoStats()
        {
            var result = new DecoyFilteringModule().Recover(new PBO(), new DeobfuscationResult(), new List<string>(), "");
            Assert.Equal(0, result.Stats.GetValueOrDefault("Decoys", 0));
            Assert.Equal(0, result.Stats.GetValueOrDefault("Stubs", 0));
        }

        // ─── SuffixBasedFilenameRecoveryModule ───

        [Fact]
        public void SuffixBasedFilenameRecoveryModule_ExactSuffixMatch_Recovers()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\_as.paa", PaaHeader));
            var knownPaths = new List<string> { "data/avs_assault_as.paa" };
            var result = new DeobfuscationResult();

            new SuffixBasedFilenameRecoveryModule().Recover(pbo, result, knownPaths, "");

            Assert.Equal("data/avs_assault_as.paa", result.RecoveredNames[0]);
        }

        [Fact]
        public void SuffixBasedFilenameRecoveryModule_ExtensionOnlyMatch_Recovers()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\.paa", PaaHeader));
            var knownPaths = new List<string> { "data/avs_assault_co.paa" };
            var result = new DeobfuscationResult();

            new SuffixBasedFilenameRecoveryModule().Recover(pbo, result, knownPaths, "");

            Assert.Equal("data/avs_assault_co.paa", result.RecoveredNames[0]);
        }

        [Fact]
        public void SuffixBasedFilenameRecoveryModule_NormalName_Skips()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\model.p3d", PaaHeader));
            var knownPaths = new List<string>();
            var result = new DeobfuscationResult();

            new SuffixBasedFilenameRecoveryModule().Recover(pbo, result, knownPaths, "");

            Assert.Empty(result.RecoveredNames);
        }

        [Fact]
        public void SuffixBasedFilenameRecoveryModule_NoKnownPaths_NoRecovery()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\_as.paa", PaaHeader));
            var result = new DeobfuscationResult();

            new SuffixBasedFilenameRecoveryModule().Recover(pbo, result, new List<string>(), "");

            Assert.Empty(result.RecoveredNames);
        }

        [Fact]
        public void SuffixBasedFilenameRecoveryModule_MultipleFilesWithSuffix_EachRecovered()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\_as.paa", PaaHeader));
            pbo.Files.Add(new DummyFileEntry("data\\_mc.paa", PaaHeader));
            var knownPaths = new List<string>
            {
                "data/avs_assault_as.paa",
                "data/avs_assault_mc.paa"
            };
            var result = new DeobfuscationResult();

            new SuffixBasedFilenameRecoveryModule().Recover(pbo, result, knownPaths, "");

            Assert.Equal(2, result.RecoveredNames.Count);
            Assert.Equal(2, result.Stats["Recovered"]);
        }

        [Fact]
        public void SuffixBasedFilenameRecoveryModule_EachPathUsedOnce()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\_as.paa", PaaHeader));
            pbo.Files.Add(new DummyFileEntry("data\\_as.paa", PaaHeader)); // same suffix, same dir
            var knownPaths = new List<string>
            {
                "data/avs_assault_as.paa" // only one path matches
            };
            var result = new DeobfuscationResult();

            new SuffixBasedFilenameRecoveryModule().Recover(pbo, result, knownPaths, "");

            Assert.Single(result.RecoveredNames); // only one can be recovered
        }

        // ─── P3DPathRecoveryModule ───

        [Fact]
        public void P3DPathRecoveryModule_NoP3DFiles_ReturnsEmpty()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("config.bin", new byte[] { 0x72, 0x61, 0x50, 0x00 }));
            var knownPaths = new List<string>();
            var result = new DeobfuscationResult();

            new P3DPathRecoveryModule().Recover(pbo, result, knownPaths, "");

            Assert.Empty(knownPaths);
        }

        [Fact]
        public void P3DPathRecoveryModule_InvalidP3D_DoesNotThrow()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("model.p3d", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }));
            var knownPaths = new List<string>();
            var result = new DeobfuscationResult();

            // Should not throw despite invalid P3D data
            var ex = Record.Exception(() =>
                new P3DPathRecoveryModule().Recover(pbo, result, knownPaths, ""));
            Assert.Null(ex);
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
