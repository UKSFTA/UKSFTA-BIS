using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using BIS.Core.Config;
using BIS.Core.Streams;
using ConfigValueType = BIS.Core.Config.ValueType;

namespace BIS.PBO.Deobfuscator.Test
{
    public class RVMATReferenceUpdaterTest
    {
        [Fact]
        public void UpdateReferences_ReplacesExactPathMatch()
        {
            var config = MakeRvmatWithTexture("data/abav/old_co.paa");
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("material.rvmat", binary);
            var updater = new RVMATReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/abav/old_co.paa"] = "data/abav/new_co.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var text = Encoding.UTF8.GetString(result);
            Assert.Contains("data/abav/new_co.paa", text);
            Assert.DoesNotContain("data/abav/old_co.paa", text);
        }

        [Fact]
        public void UpdateReferences_ReplacesFuzzyPathMatch()
        {
            var config = MakeRvmatWithTexture("jsoar/data/abav/old_co.paa");
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("material.rvmat", binary);
            var updater = new RVMATReferenceUpdater();

            // pathMap key WITHOUT the jsoar/ prefix -> fuzzy match via EndsWith
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/abav/old_co.paa"] = "data/abav/new_co.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var text = Encoding.UTF8.GetString(result);
            Assert.Contains("data/abav/new_co.paa", text);
        }

        [Fact]
        public void UpdateReferences_ReplacesMultipleStages()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamClass("Stage0", new ParamEntry[]
                    {
                        new ParamValue("texture", "data/abav/old_co.paa"),
                        new ParamValue("uvSource", "tex")
                    }),
                    new ParamClass("Stage1", new ParamEntry[]
                    {
                        new ParamValue("texture", "data/abav/old_nohq.paa"),
                        new ParamValue("uvSource", "tex")
                    })
                })
            };
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("material.rvmat", binary);
            var updater = new RVMATReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/abav/old_co.paa"] = "data/abav/new_co.paa",
                ["data/abav/old_nohq.paa"] = "data/abav/new_nohq.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var text = Encoding.UTF8.GetString(result);
            Assert.Contains("data/abav/new_co.paa", text);
            Assert.Contains("data/abav/new_nohq.paa", text);
            Assert.DoesNotContain("old_", text);
        }

        [Fact]
        public void UpdateReferences_ReplacesInArray()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamArray("textures", new RawValue[]
                    {
                        new RawValue("a3/data_f/rock.paa"),
                        new RawValue("a3/data_f/grass.paa"),
                        new RawValue("unrelated_stuff")
                    })
                })
            };
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("material.rvmat", binary);
            var updater = new RVMATReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["a3/data_f/rock.paa"] = "myaddon/tex/rock.paa",
                ["a3/data_f/grass.paa"] = "myaddon/tex/grass.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var text = Encoding.UTF8.GetString(result);
            Assert.Contains("myaddon/tex/rock.paa", text);
            Assert.Contains("myaddon/tex/grass.paa", text);
            Assert.Contains("unrelated_stuff", text);
        }

        [Fact]
        public void UpdateReferences_NormalizesBackslashes()
        {
            // Path stored with backslashes should match forward-slash pathMap
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamValue("texture", "data\\abav\\old_co.paa")
                })
            };
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("material.rvmat", binary);
            var updater = new RVMATReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/abav/old_co.paa"] = "data/abav/new_co.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var text = Encoding.UTF8.GetString(result);
            Assert.Contains("data/abav/new_co.paa", text);
        }

        [Fact]
        public void UpdateReferences_ReturnsNullWhenNoPathsChange()
        {
            var config = MakeRvmatWithTexture("data/abav/old_co.paa");
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("material.rvmat", binary);
            var updater = new RVMATReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["nonexistent/path.paa"] = "other/nope.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.Null(result);
        }

        [Fact]
        public void UpdateReferences_NestedPath_Replaced()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamClass("Stage0", new ParamEntry[]
                    {
                        new ParamClass("uvTransform", new ParamEntry[]
                        {
                            new ParamValue("texture", "data/abav/old_co.paa")
                        })
                    })
                })
            };
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("material.rvmat", binary);
            var updater = new RVMATReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/abav/old_co.paa"] = "data/abav/new_co.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var text = Encoding.UTF8.GetString(result);
            Assert.Contains("data/abav/new_co.paa", text);
        }

        [Fact]
        public void UpdateReferences_NonRVMATExtension_ReturnsNull()
        {
            var entry = new DummyFileEntry("file.txt", new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F });
            var updater = new RVMATReferenceUpdater();
            Assert.Null(updater.UpdateReferences(entry, new Dictionary<string, string>()));
        }

        [Fact]
        public void UpdateReferences_InvalidBinaryData_ReturnsNull()
        {
            var entry = new DummyFileEntry("material.rvmat", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
            var updater = new RVMATReferenceUpdater();
            Assert.Null(updater.UpdateReferences(entry, new Dictionary<string, string>()));
        }

        [Fact]
        public void UpdateReferences_EmptyConfig_ReturnsNull()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", Array.Empty<ParamEntry>())
            };
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("material.rvmat", binary);
            var updater = new RVMATReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data/abav/old_co.paa"] = "data/abav/new_co.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.Null(result);
        }

        // ─── Helpers ───

        private static byte[] SerializeParamFile(ParamFile config)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriterEx(ms, true))
            {
                config.Write(writer);
            }
            return ms.ToArray();
        }

        private static ParamFile MakeRvmatWithTexture(string texturePath)
        {
            return new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamClass("Stage0", new ParamEntry[]
                    {
                        new ParamValue("texture", texturePath),
                        new ParamValue("uvSource", "tex")
                    })
                })
            };
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
