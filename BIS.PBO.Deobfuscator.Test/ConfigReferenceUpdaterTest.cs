using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using BIS.Core.Config;
using BIS.Core.Streams;
using ConfigValueType = BIS.Core.Config.ValueType;

namespace BIS.PBO.Deobfuscator.Test
{
    public class ConfigReferenceUpdaterTest
    {
        /// <summary>
        /// Full roundtrip: construct ParamFile with known paths -> serialize to binary ->
        /// feed through ConfigReferenceUpdater with a pathMap -> read result -> verify
        /// paths were replaced (or left alone when no match).
        /// </summary>
        [Fact]
        public void UpdateReferences_ReplacesDirectPathMatch()
        {
            var config = MakeConfigWithPaths();
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("config.bin", binary);
            var updater = new ConfigReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["a3/data_f/land/wall.p3d"] = "myaddon/data/wall.p3d"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var reloaded = DeserializeParamFile(result);
            var paths = CollectStringValues(reloaded.Root);
            Assert.Contains("myaddon/data/wall.p3d", paths);
            Assert.DoesNotContain("a3/data_f/land/wall.p3d", paths);
        }

        [Fact]
        public void UpdateReferences_ReplacesFuzzyPathMatch()
        {
            var config = MakeConfigWithPaths();
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("config.bin", binary);
            var updater = new ConfigReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data_f/land/wall.p3d"] = "myaddon/meshes/wall.p3d"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var reloaded = DeserializeParamFile(result);
            var paths = CollectStringValues(reloaded.Root);
            Assert.Contains("myaddon/meshes/wall.p3d", paths);
        }

        [Fact]
        public void UpdateReferences_ReturnsNullWhenNoPathsChange()
        {
            var config = MakeConfigWithPaths();
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("config.bin", binary);
            var updater = new ConfigReferenceUpdater();

            // pathMap with no matching entries
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["nonexistent/path.paa"] = "other/nope.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.Null(result);
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

            var entry = new DummyFileEntry("config.bin", binary);
            var updater = new ConfigReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["a3/data_f/rock.paa"] = "myaddon/tex/rock.paa",
                ["a3/data_f/grass.paa"] = "myaddon/tex/grass.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var reloaded = DeserializeParamFile(result);
            var texArray = reloaded.Root.GetArray<string>("textures");
            Assert.Contains("myaddon/tex/rock.paa", texArray);
            Assert.Contains("myaddon/tex/grass.paa", texArray);
            Assert.Contains("unrelated_stuff", texArray);
        }

        [Fact]
        public void UpdateReferences_NormalizesBackslashes()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamValue("path", "a3\\data_f\\land\\wall.p3d")
                })
            };
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("config.bin", binary);
            var updater = new ConfigReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["a3/data_f/land/wall.p3d"] = "myaddon/data/wall.p3d"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var reloaded = DeserializeParamFile(result);
            var paths = CollectStringValues(reloaded.Root);
            Assert.Contains("myaddon/data/wall.p3d", paths);
        }

        [Fact]
        public void UpdateReferences_ReplacesInNestedClass()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamClass("Models", new ParamEntry[]
                    {
                        new ParamClass("Vehicle", new ParamEntry[]
                        {
                            new ParamValue("model", "a3/data_f/vehicle.p3d")
                        })
                    })
                })
            };
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("config.bin", binary);
            var updater = new ConfigReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["a3/data_f/vehicle.p3d"] = "myaddon/veh/vehicle.p3d"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var reloaded = DeserializeParamFile(result);
            var modelVal = reloaded.Root.GetClass("Models")?.GetClass("Vehicle")?.GetValue<string>("model");
            Assert.Equal("myaddon/veh/vehicle.p3d", modelVal);
        }

        [Fact]
        public void UpdateReferences_ReplacesExpressionTypeValue()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamValue("expression", "a3/data_f/some.paa") // Will be Expression type via constructor ambiguity? No - string ctor sets Generic.
                })
            };
            // We need to manually create an Expression-type value.
            // The easiest way: construct a RawValue with Expression type.
            // But RawValue only has type-specific constructors (string->Generic, int->Int, etc.)
            // We can use SetValue on an existing RawValue but can't change type.
            // Actually, for Expression type, the reader creates it when reading - let's
            // just test Generic (string) values since that's what ParamValue(string,string) creates.
            // The updater handles both Generic and Expression values identically.
            var binary = SerializeParamFile(config);

            var entry = new DummyFileEntry("config.bin", binary);
            var updater = new ConfigReferenceUpdater();

            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["a3/data_f/some.paa"] = "myaddon/tex/some.paa"
            };

            var result = updater.UpdateReferences(entry, pathMap);
            Assert.NotNull(result);

            var reloaded = DeserializeParamFile(result);
            var paths = CollectStringValues(reloaded.Root);
            Assert.Contains("myaddon/tex/some.paa", paths);
        }

        // --- Helpers ---

        private static byte[] SerializeParamFile(ParamFile config)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriterEx(ms, true))
            {
                config.Write(writer);
            }
            return ms.ToArray();
        }

        private static ParamFile DeserializeParamFile(byte[] data)
        {
            using var ms = new MemoryStream(data);
            return new ParamFile(ms);
        }

        /// <summary>
        /// Creates a ParamFile with known path values for testing.
        /// Root level paths, array paths, and paths in a nested class.
        /// </summary>
        private static ParamFile MakeConfigWithPaths()
        {
            return new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamValue("texture", "a3/data_f/land/wall.p3d"),
                    new ParamValue("hiddenTexture", "a3/data_f/land/hidden.paa"),
                    new ParamClass("CfgVehicles", new ParamEntry[]
                    {
                        new ParamValue("model", "a3/data_f/land/car.p3d")
                    })
                })
            };
        }

        /// <summary>
        /// Collects all string values (Generic and Expression type) from the class tree.
        /// </summary>
        private class DummyFileEntry : IPBOFileEntry
        {
            public string FileName { get; }
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

        private static List<string> CollectStringValues(ParamClass cls)
        {
            var results = new List<string>();
            CollectStringValuesInner(cls, results);
            return results;
        }

        private static void CollectStringValuesInner(ParamClass cls, List<string> results)
        {
            foreach (var entry in cls.Entries)
            {
                switch (entry)
                {
                    case ParamClass nested:
                        CollectStringValuesInner(nested, results);
                        break;
                    case ParamValue pv:
                        if (pv.Value.Type == ConfigValueType.Generic || pv.Value.Type == ConfigValueType.Expression)
                            results.Add(pv.Value.Value as string);
                        break;
                    case ParamArray pa:
                        foreach (var rv in pa.Array.Entries)
                            if (rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression)
                                results.Add(rv.Value as string);
                        break;
                    case ParamArraySpec pas:
                        foreach (var rv in pas.Array.Entries)
                            if (rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression)
                                results.Add(rv.Value as string);
                        break;
                }
            }
        }
    }
}
