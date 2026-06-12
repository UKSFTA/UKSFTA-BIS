using BIS.Core.Config;
using BIS.Core.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BIS.Core.Test.Config
{
    public class ParamFileRoundtripTest
    {
        private static ParamFile CreateTestParamFile()
        {
            var file = new ParamFile
            {
                OfpVersion = 55,
                Version = 21,
            };

            file.EnumValues.Add(new KeyValuePair<string, int>("__ARMA_BLANK_ENUM__", 0));
            file.EnumValues.Add(new KeyValuePair<string, int>("eSomeFlag", 1));

            file.Root = new ParamClass("rootClass", new ParamEntry[]
            {
                new ParamValue("version", 11),
                new ParamValue("name", "TestConfig"),

                new ParamClass("VehiclePrefab", "Vehicle", new ParamEntry[]
                {
                    new ParamValue("displayName", "Test Vehicle"),
                    new ParamValue("maxSpeed", 120.0f),
                    new ParamValue("weight", 2500),
                    new ParamValue("id64", 0x123456789ABCDEFL),

                    new ParamArray("cargoSlots", new RawValue(4), new RawValue(8), new RawValue(2)),
                    new ParamArraySpec("attachments", 1, new RawValue("scope"), new RawValue("supressor")),

                    new ParamClass("damage", new ParamEntry[]
                    {
                        new ParamValue("hull", 1000),
                        new ParamValue("engine", 500.0f),
                    }),

                    new ParamExternClass("SomeExternalClass"),
                    new ParamDeleteClass("ObsoleteClass"),
                }),
            });

            return file;
        }

        private static void AssertParamFilesEqual(ParamFile original, ParamFile roundtripped)
        {
            Assert.Equal(original.OfpVersion, roundtripped.OfpVersion);
            Assert.Equal(original.Version, roundtripped.Version);

            Assert.Equal(original.EnumValues.Count, roundtripped.EnumValues.Count);
            for (int i = 0; i < original.EnumValues.Count; i++)
            {
                Assert.Equal(original.EnumValues[i].Key, roundtripped.EnumValues[i].Key);
                Assert.Equal(original.EnumValues[i].Value, roundtripped.EnumValues[i].Value);
            }

            AssertParamClassesEqual(original.Root, roundtripped.Root);
        }

        private static void AssertParamClassesEqual(ParamClass expected, ParamClass actual)
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.BaseClassName, actual.BaseClassName);
            Assert.Equal(expected.Entries.Count, actual.Entries.Count);

            for (int i = 0; i < expected.Entries.Count; i++)
            {
                AssertParamEntriesEqual(expected.Entries[i], actual.Entries[i]);
            }
        }

        private static void AssertParamEntriesEqual(ParamEntry expected, ParamEntry actual)
        {
            Assert.Equal(expected.GetType(), actual.GetType());
            Assert.Equal(expected.Name, actual.Name);

            switch (expected)
            {
                case ParamClass expectedCls:
                    AssertParamClassesEqual(expectedCls, (ParamClass)actual);
                    break;

                case ParamValue expectedVal:
                    var actualVal = (ParamValue)actual;
                    Assert.Equal(expectedVal.Value.Type, actualVal.Value.Type);
                    Assert.Equal(expectedVal.Value.Value, actualVal.Value.Value);
                    break;

                case ParamArray expectedArr:
                    var actualArr = (ParamArray)actual;
                    AssertRawArraysEqual(expectedArr.Array, actualArr.Array);
                    break;

                case ParamArraySpec expectedSpec:
                    var actualSpec = (ParamArraySpec)actual;
                    Assert.Equal(expectedSpec.Flag, actualSpec.Flag);
                    AssertRawArraysEqual(expectedSpec.Array, actualSpec.Array);
                    break;

                case ParamExternClass _:
                case ParamDeleteClass _:
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected entry type: {expected.GetType()}");
            }
        }

        private static void AssertRawArraysEqual(RawArray expected, RawArray actual)
        {
            Assert.Equal(expected.Entries.Count, actual.Entries.Count);
            for (int i = 0; i < expected.Entries.Count; i++)
            {
                Assert.Equal(expected.Entries[i].Type, actual.Entries[i].Type);
                Assert.Equal(expected.Entries[i].Value, actual.Entries[i].Value);
            }
        }

        [Fact]
        public void Roundtrip_ConstructedParamFile_ProducesIdenticalData()
        {
            var original = CreateTestParamFile();

            using var writeStream = new MemoryStream();
            using (var writer = new BinaryWriterEx(writeStream, true))
            {
                original.Write(writer);
            }

            var written = writeStream.ToArray();
            Assert.NotEmpty(written);
            Assert.True(written.Length > 16);

            using var readStream = new MemoryStream(written);
            var roundtripped = new ParamFile(readStream);

            AssertParamFilesEqual(original, roundtripped);
        }

        [Fact]
        public void Roundtrip_EmptyRoot_ProducesIdenticalData()
        {
            var original = new ParamFile
            {
                OfpVersion = 55,
                Version = 21,
                Root = new ParamClass("rootClass"),
            };

            using var writeStream = new MemoryStream();
            using (var writer = new BinaryWriterEx(writeStream, true))
            {
                original.Write(writer);
            }

            using var readStream = new MemoryStream(writeStream.ToArray());
            var roundtripped = new ParamFile(readStream);

            AssertParamFilesEqual(original, roundtripped);
        }

        [Fact]
        public void Roundtrip_DeeplyNestedClasses_ProducesIdenticalData()
        {
            var original = new ParamFile
            {
                OfpVersion = 55,
                Version = 21,
            };

            original.Root = new ParamClass("rootClass", new ParamEntry[]
            {
                new ParamClass("level1", new ParamEntry[]
                {
                    new ParamValue("l1_val", 1),
                    new ParamClass("level2", new ParamEntry[]
                    {
                        new ParamValue("l2_val", "deep"),
                        new ParamClass("level3", new ParamEntry[]
                        {
                            new ParamValue("l3_val", 99.9f),
                        }),
                        new ParamValue("l2_val2", "also deep"),
                    }),
                    new ParamValue("l1_val2", 2),
                }),
                new ParamValue("rootVal", "at root"),
            });

            using var writeStream = new MemoryStream();
            using (var writer = new BinaryWriterEx(writeStream, true))
            {
                original.Write(writer);
            }

            using var readStream = new MemoryStream(writeStream.ToArray());
            var roundtripped = new ParamFile(readStream);

            AssertParamFilesEqual(original, roundtripped);
        }

        [Fact]
        public void Roundtrip_AllValueTypesAndArrays_ProducesIdenticalData()
        {
            var original = new ParamFile
            {
                OfpVersion = 55,
                Version = 21,
            };

            original.Root = new ParamClass("rootClass", new ParamEntry[]
            {
                new ParamValue("strVal", "hello world"),
                new ParamValue("intVal", -42),
                new ParamValue("floatVal", 3.14159f),
                new ParamValue("int64Val", long.MaxValue),

                new ParamArray("intArr", new RawValue(1), new RawValue(2), new RawValue(3)),
                new ParamArray("strArr", new RawValue("a"), new RawValue("b")),
                new ParamArray("mixedArr",
                    new RawValue("text"),
                    new RawValue(42),
                    new RawValue(3.14f)),

                new ParamArraySpec("specArr", 1,
                    new RawValue("opt1"),
                    new RawValue("opt2")),
            });

            using var writeStream = new MemoryStream();
            using (var writer = new BinaryWriterEx(writeStream, true))
            {
                original.Write(writer);
            }

            using var readStream = new MemoryStream(writeStream.ToArray());
            var roundtripped = new ParamFile(readStream);

            AssertParamFilesEqual(original, roundtripped);
        }
    }
}
