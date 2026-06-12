using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using BIS.PBO;
using BIS.PBO.Deobfuscator;

namespace BIS.PBO.Deobfuscator.Test
{
    public class ProfileDetectionTest
    {
        /// <summary>
        /// Simulates a DecoyInjection PBO (Maverick's obfusQf):
        /// - Many header properties with keys/values >40 chars
        /// - Zero-byte decoy files
        /// - Mix of obfuscated entry names and real asset files
        /// Expected: matched by DecoyInjectionProfile, NOT by Suffix profiles
        /// </summary>
        [Fact]
        public void DecoyInjectionPBO_MatchesDecoyInjectionProfile()
        {
            var pbo = new PBO();
            // Add long properties (>40 chars) — decoy injection signature
            for (int i = 0; i < 10; i++)
                pbo.PropertiesPairs.Add(new KeyValuePair<string, string>(
                    $"obfuscated_property_key_with_extra_padding_{i:D4}",
                    $"obfuscated_property_value_with_extra_padding_{i:D4}"));

            // Add short normal property
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test_mod"));

            // Add zero-byte decoy files
            pbo.Files.Add(new DummyFileEntry("xsOpHgLJR.rsa", new byte[0]));
            pbo.Files.Add(new DummyFileEntry("uflFiEZe2B.rsa", new byte[0]));

            // Add real asset files
            pbo.Files.Add(new DummyFileEntry("data\\spc\\spc_side_plates.rvmat", Encoding.ASCII.GetBytes("some content")));
            pbo.Files.Add(new DummyFileEntry("data\\spc\\spc_plates.rvmat", Encoding.ASCII.GetBytes("some content")));

            var deobf = new PboDeobfuscator();
            var result = deobf.Process(pbo);

            Assert.True(result.IsObfuscated);
            Assert.Contains("Decoy Injection", result.MatchedProfile);
        }

        /// <summary>
        /// Simulates a SuffiX Cyrillic PBO:
        /// - All short header properties
        /// - Zero-byte marker files (empty name, *.*, \\\)
        /// - Cyrillic-obfuscated filenames
        /// Expected: matched by ModularSuffixRecoveryProfile (Cyrillic),
        ///           NOT by DecoyInjectionProfile (no long properties)
        /// </summary>
        [Fact]
        public void SuffixCyrillicPBO_MatchesModularSuffixProfile()
        {
            var pbo = new PBO();
            // Short properties only — NO long properties
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "JSOAR"));
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("obfuscated", "фЬѓ.html"));
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("product", "JSOAR"));

            // Zero-byte marker files (SuffiX style: empty name, *.*, \\\)
            pbo.Files.Add(new DummyFileEntry(" ", new byte[0]));
            pbo.Files.Add(new DummyFileEntry("*.*", new byte[0]));
            pbo.Files.Add(new DummyFileEntry("\\\\\\", new byte[0]));

            // Cyrillic-obfuscated filenames
            pbo.Files.Add(new DummyFileEntry("acex\\їрѐёћ?СЫеъќ.paa", new byte[] { 0x00, 0x72, 0x61, 0x53 }));
            pbo.Files.Add(new DummyFileEntry("acex\\abвгдC.paa", new byte[] { 0x00, 0x72, 0x61, 0x53 }));

            var deobf = new PboDeobfuscator();
            var result = deobf.Process(pbo);

            Assert.True(result.IsObfuscated);
            // Must match Cyrillic/Suffix profile, NOT DecoyInjection
            Assert.Contains("Modular Suffix Recovery", result.MatchedProfile);
            Assert.DoesNotContain("Decoy Injection", result.MatchedProfile);
        }

        /// <summary>
        /// Simulates a Suffix-stripped PBO (SuffiX with stripped names, no Cyrillic):
        /// - Short properties
        /// - Files with only _suffix.ext or .ext names
        /// Expected: matched by SuffixRecoveryProfile, NOT by DecoyInjectionProfile
        /// </summary>
        [Fact]
        public void SuffixStrippedPBO_MatchesSuffixRecoveryProfile()
        {
            var pbo = new PBO();
            // Short properties
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "some_mod"));
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("product", "Some Mod"));

            // Suffix-only filenames (stripped base name)
            pbo.Files.Add(new DummyFileEntry("data\\abav\\_as.paa", new byte[] { 0x00, 0x72, 0x61, 0x53 }));
            pbo.Files.Add(new DummyFileEntry("data\\abav\\_mc.paa", new byte[] { 0x00, 0x72, 0x61, 0x53 }));
            pbo.Files.Add(new DummyFileEntry("acex\\.paa", new byte[] { 0x00, 0x72, 0x61, 0x53 }));

            var deobf = new PboDeobfuscator();
            var result = deobf.Process(pbo);

            Assert.True(result.IsObfuscated);
            Assert.Contains("Suffix", result.MatchedProfile);
            Assert.DoesNotContain("Decoy Injection", result.MatchedProfile);
        }

        /// <summary>
        /// Simulates a clean PBO:
        /// - Normal properties
        /// - Normal readable filenames
        /// Expected: not obfuscated
        /// </summary>
        [Fact]
        public void CleanPBO_NotDetectedAsObfuscated()
        {
            var pbo = new PBO();
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "uksf_tfa_vests"));
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("product", "UKSF TFA Vests"));

            // Clean readable filenames
            pbo.Files.Add(new DummyFileEntry("data\\vests\\vest_a.paa", new byte[] { 0x00, 0x72, 0x61, 0x53 }));
            pbo.Files.Add(new DummyFileEntry("data\\vests\\vest_b.paa", new byte[] { 0x00, 0x72, 0x61, 0x53 }));
            pbo.Files.Add(new DummyFileEntry("config.bin", new byte[] { 0x72, 0x61, 0x50, 0x00 }));

            var deobf = new PboDeobfuscator();
            var result = deobf.Process(pbo);

            // Clean PBO should not match any obfuscation profile
            Assert.False(result.IsObfuscated);
        }

        /// <summary>
        /// Simulates a PBO that has zero-byte files but NO long properties.
        /// This should NOT match DecoyInjectionProfile (requires BOTH conditions).
        /// SuffiX PBOs with only zero-byte markers + Cyrillic names are the real case.
        /// </summary>
        [Fact]
        public void ZeroByteFilesWithoutLongProps_DoesNotMatchDecoyInjection()
        {
            var pbo = new PBO();
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test"));
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("product", "Test"));

            // Three zero-byte files (like SuffiX markers)
            pbo.Files.Add(new DummyFileEntry(" ", new byte[0]));
            pbo.Files.Add(new DummyFileEntry("*.*", new byte[0]));
            pbo.Files.Add(new DummyFileEntry("\\\\\\", new byte[0]));

            // Normal filenames (no Cyrillic, no stripping)
            pbo.Files.Add(new DummyFileEntry("data\\test.paa", new byte[] { 0x00, 0x72, 0x61, 0x53 }));

            var deobf = new PboDeobfuscator();
            var result = deobf.Process(pbo);

            // Zero-byte files alone is NOT enough for DecoyInjection
            Assert.False(result.IsObfuscated);
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
                Size = data.Length;
                _data = data;
            }

            public Stream OpenRead() => new MemoryStream(_data, false);
        }
    }
}
