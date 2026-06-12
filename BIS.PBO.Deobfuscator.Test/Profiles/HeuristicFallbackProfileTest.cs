using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using BIS.PBO;
using BIS.PBO.Deobfuscator.Profiles;

namespace BIS.PBO.Deobfuscator.Test.Profiles
{
    public class HeuristicFallbackProfileTest
    {
        [Fact]
        public void IsMatch_HighSmallFileRatio_ReturnsTrue()
        {
            var pbo = MakePbo();
            // 6 small files (<512) + 4 large = 10 total => 60% ratio -> threshold is >60%, this is exactly 60% which is NOT >60%
            // Make it 7 small + 3 large = 70% -> >60% -> true
            for (int i = 0; i < 7; i++)
                pbo.Files.Add(new DummyFileEntry($"small{i}.bin", new byte[100]));
            for (int i = 0; i < 3; i++)
                pbo.Files.Add(new DummyFileEntry($"large{i}.bin", new byte[5000]));

            Assert.True(new HeuristicFallbackProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_BorderlineSmallFileRatio_NotEnough()
        {
            var pbo = MakePbo();
            // 6 small + 4 large = 60% -> NOT >60%
            for (int i = 0; i < 6; i++)
                pbo.Files.Add(new DummyFileEntry($"small{i}.bin", new byte[100]));
            for (int i = 0; i < 4; i++)
                pbo.Files.Add(new DummyFileEntry($"large{i}.bin", new byte[5000]));

            // Small file ratio is exactly 60%, but threshold is >60% so false
            // Also unusual names ratio is 0/10 = 0% -> false
            Assert.False(new HeuristicFallbackProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_HighUnusualNameRatio_ReturnsTrue()
        {
            var pbo = MakePbo();
            // 5 files, 3 with _ prefix (>40%) -> true
            pbo.Files.Add(new DummyFileEntry("_weird1.paa", new byte[1000]));
            pbo.Files.Add(new DummyFileEntry("_weird2.paa", new byte[1000]));
            pbo.Files.Add(new DummyFileEntry("_weird3.paa", new byte[1000]));
            pbo.Files.Add(new DummyFileEntry("normal1.rvmat", new byte[1000]));
            pbo.Files.Add(new DummyFileEntry("normal2.rvmat", new byte[1000]));

            Assert.True(new HeuristicFallbackProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_BlockedByLongPropsPlusZeroBytes_ReturnsFalse()
        {
            var pbo = MakePbo();
            // Long properties (DecoyInjection signature) -> IsMatch should return false
            for (int i = 0; i < 3; i++)
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>(
                    $"very_long_property_key_that_exceeds_forty_characters_{i}",
                    $"very_long_property_value_that_exceeds_forty_characters_{i}"));
            pbo.Files.Add(new DummyFileEntry(string.Empty, new byte[0])); // zero byte
            // Also high small file ratio would normally trigger it
            for (int i = 0; i < 7; i++)
                pbo.Files.Add(new DummyFileEntry($"small{i}.bin", new byte[100]));
            for (int i = 0; i < 3; i++)
                pbo.Files.Add(new DummyFileEntry($"large{i}.bin", new byte[5000]));

            Assert.False(new HeuristicFallbackProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_BlockedBySuffixPattern_ReturnsFalse()
        {
            var pbo = MakePbo();
            // File starts with . (suffix pattern) -> should return false
            pbo.Files.Add(new DummyFileEntry(".paa", new byte[1000]));

            // Would normally trigger due to high unusual name ratio
            Assert.False(new HeuristicFallbackProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_BlockedByCyrillicPattern_ReturnsFalse()
        {
            var pbo = MakePbo();
            // Cyrillic chars in name -> should return false (Suffix profile handles it)
            pbo.Files.Add(new DummyFileEntry("data\\abav\\\u043B\u0430\u045A_co.paa", new byte[1000]));

            Assert.False(new HeuristicFallbackProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_EmptyPbo_ReturnsFalse()
        {
            var pbo = MakePbo();
            Assert.False(new HeuristicFallbackProfile().IsMatch(pbo));
        }

        [Fact]
        public void IsMatch_CleanPbo_ReturnsFalse()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\model.p3d", new byte[50000]));
            pbo.Files.Add(new DummyFileEntry("data\\tex_co.paa", new byte[2000]));

            Assert.False(new HeuristicFallbackProfile().IsMatch(pbo));
        }

        [Fact]
        public void Deobfuscate_DelegatesToModularSuffix()
        {
            // HeuristicFallbackProfile.Deobfuscate() delegates to ModularSuffixRecoveryProfile
            // We can't fully test the result, but verify it doesn't throw
            var pbo = MakePbo();
            var result = new HeuristicFallbackProfile().Deobfuscate(pbo);
            Assert.NotNull(result);
        }

        [Fact]
        public void ProfileName_IsCorrect()
        {
            Assert.Equal("HeuristicFallback", new HeuristicFallbackProfile().ProfileName);
        }

        [Fact]
        public void IsMatch_LessThanThresholdUnusualNames_ReturnsFalse()
        {
            var pbo = MakePbo();
            // 5 files, 2 with unusual prefix (40%) -> threshold is >40% so false
            pbo.Files.Add(new DummyFileEntry("_weird1.paa", new byte[1000]));
            pbo.Files.Add(new DummyFileEntry("_weird2.paa", new byte[1000]));
            pbo.Files.Add(new DummyFileEntry("normal1.rvmat", new byte[1000]));
            pbo.Files.Add(new DummyFileEntry("normal2.rvmat", new byte[1000]));
            pbo.Files.Add(new DummyFileEntry("normal3.rvmat", new byte[1000]));

            Assert.False(new HeuristicFallbackProfile().IsMatch(pbo));
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
