using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using BIS.Core.Config;
using BIS.PBO.Deobfuscator.Profiles.Specialized;

namespace BIS.PBO.Deobfuscator.Test.Profiles.Specialized
{
    public class ProfileUtilsTest
    {
        // ─── NormalizePath ───

        [Fact]
        public void NormalizePath_BackslashesToForwardSlashes()
        {
            Assert.Equal("data/tex/old_co.paa", ProfileUtils.NormalizePath("data\\tex\\old_co.paa"));
        }

        [Fact]
        public void NormalizePath_ForwardSlashesUnchanged()
        {
            Assert.Equal("data/tex/old_co.paa", ProfileUtils.NormalizePath("data/tex/old_co.paa"));
        }

        [Fact]
        public void NormalizePath_EmptyString_ReturnsEmpty()
        {
            Assert.Equal("", ProfileUtils.NormalizePath(""));
        }

        // ─── GetFileName ───

        [Fact]
        public void GetFileName_WithDirectory_ReturnsFileName()
        {
            Assert.Equal("old_co.paa", ProfileUtils.GetFileName("data/tex/old_co.paa"));
        }

        [Fact]
        public void GetFileName_WithoutDirectory_ReturnsFullString()
        {
            Assert.Equal("old_co.paa", ProfileUtils.GetFileName("old_co.paa"));
        }

        [Fact]
        public void GetFileName_WithBackslashes_NormalizesFirst()
        {
            Assert.Equal("old_co.paa", ProfileUtils.GetFileName("data\\tex\\old_co.paa"));
        }

        // ─── GetDirectoryName ───

        [Fact]
        public void GetDirectoryName_WithDirectory_ReturnsDirectory()
        {
            Assert.Equal("data/tex", ProfileUtils.GetDirectoryName("data/tex/old_co.paa"));
        }

        [Fact]
        public void GetDirectoryName_WithoutDirectory_ReturnsEmpty()
        {
            Assert.Equal("", ProfileUtils.GetDirectoryName("old_co.paa"));
        }

        [Fact]
        public void GetDirectoryName_RootLevel_ReturnsEmpty()
        {
            Assert.Equal("", ProfileUtils.GetDirectoryName("_co.paa"));
        }

        // ─── IsValidPathString ───

        [Fact]
        public void IsValidPathString_AsciiPath_ReturnsTrue()
        {
            Assert.True(ProfileUtils.IsValidPathString("data/tex/old_co.paa"));
        }

        [Fact]
        public void IsValidPathString_ContainsQuestionMark_ReturnsTrue()
        {
            // Wildcards are allowed — they're part of obfuscated config.bin paths
            Assert.True(ProfileUtils.IsValidPathString("data/tex/old_co?x.paa"));
        }

        [Fact]
        public void IsValidPathString_ContainsAsterisk_ReturnsTrue()
        {
            // Wildcards are allowed — they're part of obfuscated config.bin paths
            Assert.True(ProfileUtils.IsValidPathString("data/tex/*.paa"));
        }

        [Fact]
        public void IsValidPathString_NonPrintableChars_ReturnsFalse()
        {
            Assert.False(ProfileUtils.IsValidPathString("data/tex/\x00null.paa"));
        }

        [Fact]
        public void IsValidPathString_EmptyString_ReturnsTrue()
        {
            Assert.True(ProfileUtils.IsValidPathString(""));
        }

        // ─── AddToLookup ───

        [Fact]
        public void AddToLookup_FirstEntry_CreatesList()
        {
            var lookup = new Dictionary<string, List<string>>();
            ProfileUtils.AddToLookup(lookup, "key1", "path_a.paa");

            Assert.Single(lookup);
            Assert.Equal(new List<string> { "path_a.paa" }, lookup["key1"]);
        }

        [Fact]
        public void AddToLookup_DuplicateIgnored()
        {
            var lookup = new Dictionary<string, List<string>>();
            ProfileUtils.AddToLookup(lookup, "key1", "path_a.paa");
            ProfileUtils.AddToLookup(lookup, "key1", "path_a.paa");

            Assert.Single(lookup["key1"]);
        }

        [Fact]
        public void AddToLookup_AsciiInsertedBeforeCyrillic()
        {
            var lookup = new Dictionary<string, List<string>>();
            ProfileUtils.AddToLookup(lookup, "key1", "\u043F\u0443\u0442\u044C.paa"); // Cyrillic
            ProfileUtils.AddToLookup(lookup, "key1", "ascii.paa"); // ASCII

            Assert.Equal(2, lookup["key1"].Count);
            Assert.Equal("ascii.paa", lookup["key1"][0]); // ASCII should be first
        }

        // ─── FindUnusedMatch ───

        [Fact]
        public void FindUnusedMatch_KeyExists_ReturnsFirstUnused()
        {
            var lookup = new Dictionary<string, List<string>>
            {
                ["key1"] = new List<string> { "path_a.paa", "path_b.paa" }
            };
            var used = new HashSet<string>();

            Assert.Equal("path_a.paa", ProfileUtils.FindUnusedMatch(lookup, "key1", used));
        }

        [Fact]
        public void FindUnusedMatch_AllUsed_ReturnsNull()
        {
            var lookup = new Dictionary<string, List<string>>
            {
                ["key1"] = new List<string> { "path_a.paa", "path_b.paa" }
            };
            var used = new HashSet<string> { "path_a.paa", "path_b.paa" };

            Assert.Null(ProfileUtils.FindUnusedMatch(lookup, "key1", used));
        }

        [Fact]
        public void FindUnusedMatch_KeyMissing_ReturnsNull()
        {
            var lookup = new Dictionary<string, List<string>>();
            Assert.Null(ProfileUtils.FindUnusedMatch(lookup, "nonexistent", new HashSet<string>()));
        }

        // ─── ExtractPathsFromRap ───

        [Fact]
        public void ExtractPathsFromRap_CollectsStringValuesWithPathPattern()
        {
            var root = new ParamClass("root", new ParamEntry[]
            {
                new ParamValue("texture", "data\\tex\\wall_co.paa"),
                new ParamValue("model", "data\\models\\car.p3d"),
                new ParamValue("notAPath", "hello_world"),
            });

            var paths = ProfileUtils.ExtractPathsFromRap(root);

            Assert.Contains("data\\tex\\wall_co.paa", paths);
            Assert.Contains("data\\models\\car.p3d", paths);
            Assert.DoesNotContain("hello_world", paths);
        }

        [Fact]
        public void ExtractPathsFromRap_CollectsFromNestedClasses()
        {
            var root = new ParamClass("root", new ParamEntry[]
            {
                new ParamClass("CfgVehicles", new ParamEntry[]
                {
                    new ParamClass("MyCar", new ParamEntry[]
                    {
                        new ParamValue("model", "data\\models\\car.p3d")
                    })
                })
            });

            var paths = ProfileUtils.ExtractPathsFromRap(root);

            Assert.Contains("data\\models\\car.p3d", paths);
        }

        [Fact]
        public void ExtractPathsFromRap_CollectsFromArrays()
        {
            var root = new ParamClass("root", new ParamEntry[]
            {
                new ParamArray("textures", new RawValue[]
                {
                    new RawValue("data\\tex\\a.paa"),
                    new RawValue("data\\tex\\b.paa"),
                    new RawValue("not_a_path")
                })
            });

            var paths = ProfileUtils.ExtractPathsFromRap(root);

            Assert.Contains("data\\tex\\a.paa", paths);
            Assert.Contains("data\\tex\\b.paa", paths);
            Assert.DoesNotContain("not_a_path", paths);
        }

        [Fact]
        public void ExtractPathsFromRap_EmptyRoot_ReturnsEmpty()
        {
            var root = new ParamClass("root", System.Array.Empty<ParamEntry>());
            Assert.Empty(ProfileUtils.ExtractPathsFromRap(root));
        }

        [Fact]
        public void ExtractPathsFromRap_DeduplicatesByIgnoreCase()
        {
            var root = new ParamClass("root", new ParamEntry[]
            {
                new ParamValue("tex1", "data\\tex\\wall.paa"),
                new ParamValue("tex2", "data\\tex\\WALL.paa"),
            });

            var paths = ProfileUtils.ExtractPathsFromRap(root);

            Assert.Single(paths);
        }

        // ─── ExtractClassNames ───

        [Fact]
        public void ExtractClassNames_CollectsNonExcludedClassNames()
        {
            var root = new ParamClass("root", new ParamEntry[]
            {
                new ParamClass("MyClass", System.Array.Empty<ParamEntry>()),
                new ParamClass("CfgPatches", System.Array.Empty<ParamEntry>()), // excluded
                new ParamClass("AB", System.Array.Empty<ParamEntry>()), // too short (<3)
            });

            var names = ProfileUtils.ExtractClassNames(root);

            Assert.Contains("MyClass", names);
            Assert.DoesNotContain("CfgPatches", names);
            Assert.DoesNotContain("AB", names);
        }

        [Fact]
        public void ExtractClassNames_CollectsFromNestedClasses()
        {
            var root = new ParamClass("root", new ParamEntry[]
            {
                new ParamClass("CfgVehicles", new ParamEntry[]
                {
                    new ParamClass("uksf_car_base", System.Array.Empty<ParamEntry>()),
                    new ParamClass("uksf_car_armored", System.Array.Empty<ParamEntry>()),
                })
            });

            var names = ProfileUtils.ExtractClassNames(root);
            Assert.Contains("uksf_car_base", names);
            Assert.Contains("uksf_car_armored", names);
            Assert.DoesNotContain("CfgVehicles", names);
        }

        [Fact]
        public void ExtractClassNames_EmptyRoot_ReturnsEmpty()
        {
            var root = new ParamClass("root", System.Array.Empty<ParamEntry>());
            Assert.Empty(ProfileUtils.ExtractClassNames(root));
        }

        // ─── BuildSuffixToClassMap ───

        [Fact]
        public void BuildSuffixToClassMap_MapsWordsToClassNames()
        {
            var classNames = new List<string> { "avs_assault_vest", "avs_heavy_vest" };

            var map = ProfileUtils.BuildSuffixToClassMap(classNames, "");

            Assert.Contains("avs", map.Keys);
            Assert.Contains("assault", map.Keys);
            Assert.Contains("heavy", map.Keys);
            Assert.Contains("vest", map.Keys);
            // "avs" -> both classes contain "avs"
            Assert.Equal(2, map["avs"].Count);
            Assert.Contains("avs_assault_vest", map["avs"]);
            Assert.Contains("avs_heavy_vest", map["avs"]);
        }

        [Fact]
        public void BuildSuffixToClassMap_StripsPrefix()
        {
            var classNames = new List<string> { "jsoar/avs_assault_vest" };

            var map = ProfileUtils.BuildSuffixToClassMap(classNames, "jsoar/");

            Assert.Contains("avs", map.Keys);
            Assert.Contains("assault", map.Keys);
            Assert.Contains("vest", map.Keys);
            // After stripping prefix "jsoar/", the normalized class name is "avs_assault_vest"
            Assert.Contains("avs_assault_vest", map["avs"]);
        }

        [Fact]
        public void BuildSuffixToClassMap_EmptyClassNames_ReturnsEmpty()
        {
            var map = ProfileUtils.BuildSuffixToClassMap(new List<string>(), "");
            Assert.Empty(map);
        }

        // ─── StripColorSuffixes ───

        [Fact]
        public void StripColorSuffixes_RemovesTrailingColorTokens()
        {
            Assert.Equal("ADAMS_AVS_BELT", ProfileUtils.StripColorSuffixes("ADAMS_AVS_BELT_MC_TAN"));
        }

        [Fact]
        public void StripColorSuffixes_RemovesMultipleTrailingTokens()
        {
            Assert.Equal("JSOAR_AVS", ProfileUtils.StripColorSuffixes("JSOAR_AVS_MC_BLK"));
        }

        [Fact]
        public void StripColorSuffixes_NoTokens_ReturnsOriginal()
        {
            Assert.Equal("JSOAR_AVS", ProfileUtils.StripColorSuffixes("JSOAR_AVS"));
        }

        [Fact]
        public void StripColorSuffixes_MidTokenNotColor_StopsEarly()
        {
            Assert.Equal("UKSF_KS1_CTR", ProfileUtils.StripColorSuffixes("UKSF_KS1_CTR"));
        }

        [Fact]
        public void StripColorSuffixes_OnlyOneToken()
        {
            Assert.Equal("vest", ProfileUtils.StripColorSuffixes("vest"));
        }

        [Fact]
        public void BuildSuffixToClassMap_SkipsShortAndNumericWords()
        {
            var classNames = new List<string> { "ab_vest_123", "cd_helmet_456" };

            var map = ProfileUtils.BuildSuffixToClassMap(classNames, "");

            // "ab" is length 2 (<3), "cd" is length 2 (<3) - skipped
            // "123" and "456" are all digits - skipped
            Assert.Contains("vest", map.Keys);
            Assert.Contains("helmet", map.Keys);
            Assert.DoesNotContain("ab", map.Keys);
            Assert.DoesNotContain("cd", map.Keys);
        }
    }
}
