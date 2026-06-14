using BIS.Core.Config;
using System.Globalization;
using System.Text;
using Xunit;

namespace BIS.Core.Test.Config
{
    public class ConfigSerializerTest
    {
        private static string Serialize(ParamFile config) =>
            ConfigSerializer.SerializeToConfigText(config);

        // ─── Roundtrip: parse → serialize → parse → identical output ───

        [Fact]
        public void Roundtrip_SimpleConfig_ProducesStableOutput()
        {
            var source = @"
class CfgVehicles {
    displayName = ""Test Vehicle"";
    maxSpeed = 120;
    scope = 2;
};
";
            var parsed = ParseConfigSource(source);
            var output1 = Serialize(parsed);
            var reparsed = ParseConfigSource(output1);
            var output2 = Serialize(reparsed);

            Assert.Equal(NormalizeLineEndings(output1), NormalizeLineEndings(output2));
        }

        [Fact]
        public void Roundtrip_AllValueTypes_PreservesValues()
        {
            var source = @"
class TestAll {
    strVal = ""hello"";
    intVal = 42;
    floatVal = 3.14159;
    bigIntVal = 1234567890123;
};
";
            var parsed = ParseConfigSource(source);
            var output = Serialize(parsed);

            Assert.Contains("strVal = \"hello\"", output);
            // Positive ints are stored as strings by the parser and serialized quoted
            Assert.Contains("intVal = \"42\"", output);
            Assert.Contains("floatVal = \"3.14159\"", output);
            Assert.Contains("bigIntVal = \"1234567890123\"", output);
        }

        // ─── Value type tests ───

        [Fact]
        public void Serialize_IntValues_OutputsPlainNumbers()
        {
            var config = SimpleConfig(new ParamValue("count", 42), new ParamValue("negVal", -7));
            var output = Serialize(config);

            Assert.Contains("count = 42;", output);
            Assert.Contains("negVal = -7;", output);
        }

        [Fact]
        public void Serialize_FloatValues_UsesInvariantCulture()
        {
            var config = SimpleConfig(new ParamValue("speed", 120.5f), new ParamValue("pi", 3.14159f));
            var output = Serialize(config);

            Assert.Contains("speed = 120.5;", output);
            Assert.Contains("pi = 3.14159;", output);
            // Invariant culture must use '.' not ','
            Assert.DoesNotContain("120,5", output);
        }

        [Fact]
        public void Serialize_FloatWholeNumber_OmitsDecimal()
        {
            var config = SimpleConfig(new ParamValue("whole", 42.0f));
            var output = Serialize(config);
            // 42.0f.ToString(CultureInfo.InvariantCulture) → "42"
            // Actually float 42.0f → "42" in InvariantCulture? Let's check: 42.0f.ToString(CultureInfo.InvariantCulture)
            // yields "42", not "42.0". That's fine for Arma config syntax.
            Assert.Contains("whole = 42;", output);
        }

        [Fact]
        public void Serialize_Int64Values_OutputsPlainNumbers()
        {
            var config = SimpleConfig(new ParamValue("big", 1234567890123L));
            var output = Serialize(config);

            Assert.Contains("big = 1234567890123;", output);
        }

        [Fact]
        public void Serialize_StringValues_EscapesSpecialChars()
        {
            var config = SimpleConfig(new ParamValue("desc", "Hello World"));
            var output = Serialize(config);

            Assert.Contains("desc = \"Hello World\";", output);
        }

        [Fact]
        public void Serialize_StringWithQuotes_EscapesQuotes()
        {
            var config = SimpleConfig(new ParamValue("text", "He said \"hello\""));
            var output = Serialize(config);

            Assert.Contains("text = \"He said \\\"hello\\\"\";", output);
        }

        [Fact]
        public void Serialize_StringWithBackslashes_EscapesBackslashes()
        {
            var config = SimpleConfig(new ParamValue("path", "C:\\Games\\Arma3"));
            var output = Serialize(config);

            Assert.Contains("path = \"C:\\\\Games\\\\Arma3\";", output);
        }

        [Fact]
        public void Serialize_StringWithNewlines_EscapesNewlines()
        {
            var config = SimpleConfig(new ParamValue("multiline", "line1\nline2\nline3"));
            var output = Serialize(config);

            Assert.Contains("multiline = \"line1\\nline2\\nline3\";", output);
        }

        [Fact]
        public void Serialize_StringWithTabs_EscapesTabs()
        {
            var config = SimpleConfig(new ParamValue("indented", "col1\tcol2\tcol3"));
            var output = Serialize(config);

            Assert.Contains("indented = \"col1\\tcol2\\tcol3\";", output);
        }

        [Fact]
        public void Serialize_StringWithCombinedEscapes_HandlesAllCorrectly()
        {
            var config = SimpleConfig(new ParamValue("complex", "back\\slash \"quote\"\nnewline\ttab"));
            var output = Serialize(config);

            Assert.Contains("complex = \"back\\\\slash \\\"quote\\\"\\nnewline\\ttab\";", output);
        }

        [Fact]
        public void Serialize_EmptyString_OutputsEmptyQuotes()
        {
            var config = SimpleConfig(new ParamValue("empty", ""));
            var output = Serialize(config);

            Assert.Contains("empty = \"\";", output);
        }

        [Fact]
        public void Serialize_NullStringInGeneric_OutputsEmptyQuotes()
        {
            // Edge case: Generic type with null value — EscapeString handles null → ""
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamValue("nullVal", (string)null!),
                }),
            };
            var output = Serialize(config);

            Assert.Contains("nullVal = \"\";", output);
        }

        [Fact]
        public void Serialize_BoolValue_OutputsAsInt()
        {
            // In Arma config, bools are stored as int 0/1
            var config = SimpleConfig(new ParamValue("enabled", true));
            var output = Serialize(config);

            Assert.Contains("enabled = 1;", output);
        }

        // ─── Array tests ───

        [Fact]
        public void Serialize_IntArray_OutputsBraceSyntax()
        {
            var config = SimpleConfig(
                new ParamArray("ids", new RawValue(1), new RawValue(2), new RawValue(3)));
            var output = Serialize(config);

            Assert.Contains("ids[] = { 1, 2, 3 };", output);
        }

        [Fact]
        public void Serialize_StringArray_OutputsQuotedStrings()
        {
            var config = SimpleConfig(
                new ParamArray("names", new RawValue("alpha"), new RawValue("bravo")));
            var output = Serialize(config);

            Assert.Contains("names[] = { \"alpha\", \"bravo\" };", output);
        }

        [Fact]
        public void Serialize_MixedArray_OutputsCorrectTypes()
        {
            var config = SimpleConfig(
                new ParamArray("mixed",
                    new RawValue("text"),
                    new RawValue(42),
                    new RawValue(3.14f)));
            var output = Serialize(config);

            Assert.Contains("mixed[] = { \"text\", 42, 3.14 };", output);
        }

        [Fact]
        public void Serialize_FloatArray_UsesInvariantCulture()
        {
            var config = SimpleConfig(
                new ParamArray("coords", new RawValue(1.5f), new RawValue(2.75f)));
            var output = Serialize(config);

            Assert.Contains("coords[] = { 1.5, 2.75 };", output);
            Assert.DoesNotContain("1,5", output);
        }

        [Fact]
        public void Serialize_EmptyArray_OutputsEmptyBraces()
        {
            var config = SimpleConfig(
                new ParamArray("empty", Array.Empty<RawValue>()));
            var output = Serialize(config);

            Assert.Contains("empty[] = {  };", output);
        }

        [Fact]
        public void Serialize_ArraySpec_OutputsCorrectSyntax()
        {
            var config = SimpleConfig(
                new ParamArraySpec("modes", 1, new RawValue("safe"), new RawValue("auto")));
            var output = Serialize(config);

            Assert.Contains("modes[] = { \"safe\", \"auto\" };", output);
        }

        [Fact]
        public void Serialize_LargeIntArray_SerializesAllElements()
        {
            var values = Enumerable.Range(0, 50).Select(i => new RawValue(i));
            var config = SimpleConfig(new ParamArray("range", values));
            var output = Serialize(config);

            Assert.Contains("0", output);
            Assert.Contains("49", output);
            var commaCount = output.Count(c => c == ',');
            Assert.Equal(49, commaCount);
        }

        // ─── Class tests ───

        [Fact]
        public void Serialize_ClassWithBaseClass_OutputsColonSyntax()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamClass("MyVehicle", "Car", new ParamEntry[]
                    {
                        new ParamValue("speed", 100),
                    }),
                }),
            };
            var output = Serialize(config);

            Assert.Contains("class MyVehicle : Car", output);
            Assert.Contains("speed = 100;", output);
        }

        [Fact]
        public void Serialize_ExternClass_OutputsClassDeclaration()
        {
            var config = SimpleConfig(new ParamExternClass("SomeExternalClass"));
            var output = Serialize(config);

            Assert.Contains("class SomeExternalClass;", output);
        }

        [Fact]
        public void Serialize_DeleteClass_OutputsDeleteDeclaration()
        {
            var config = SimpleConfig(new ParamDeleteClass("ObsoleteClass"));
            var output = Serialize(config);

            Assert.Contains("delete ObsoleteClass;", output);
        }

        [Fact]
        public void Serialize_NestedClasses_OutputsCorrectIndentation()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamClass("level1", new ParamEntry[]
                    {
                        new ParamValue("a", 1),
                        new ParamClass("level2", new ParamEntry[]
                        {
                            new ParamValue("b", 2),
                            new ParamClass("level3", new ParamEntry[]
                            {
                                new ParamValue("c", 3),
                            }),
                        }),
                    }),
                }),
            };
            var output = Serialize(config);

            Assert.Contains("class level1", output);
            Assert.Contains("\tclass level2", output);
            Assert.Contains("\t\tclass level3", output);
            Assert.Contains("\t\t\tc = 3;", output);
        }

        [Fact]
        public void Serialize_DeeplyNestedClasses_RoundtripsCorrectly()
        {
            var source = @"
class A {
    class B {
        class C {
            class D {
                value = 1;
            };
        };
    };
};
";
            var parsed = ParseConfigSource(source);
            var output = Serialize(parsed);
            var reparsed = ParseConfigSource(output);
            var output2 = Serialize(reparsed);

            Assert.Equal(NormalizeLineEndings(output), NormalizeLineEndings(output2));
        }

        // ─── Empty / edge case tests ───

        [Fact]
        public void Serialize_EmptyRoot_OutputsEmptyString()
        {
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass"),
            };
            var output = Serialize(config);

            Assert.Equal("", output);
        }

        [Fact]
        public void Serialize_RootWithOnlyClass_OutputsClassBraces()
        {
            var config = SimpleConfig(
                new ParamClass("Empty", new ParamEntry[0]));
            var output = Serialize(config);

            Assert.Contains("class Empty", output);
            // Serializer puts { and }; on separate lines
            Assert.Contains("{\n", output);
            Assert.Contains("};", output);
        }

        [Fact]
        public void Serialize_MultipleTopLevelEntries_OutputsAllEntries()
        {
            var config = SimplestConfig(
                new ParamValue("a", 1),
                new ParamClass("B", new ParamValue("x", "y")),
                new ParamValue("c", 2));
            var output = Serialize(config);

            Assert.Contains("a = 1;", output);
            Assert.Contains("class B", output);
            Assert.Contains("x = \"y\";", output);
            Assert.Contains("c = 2;", output);
        }

        // ─── Roundtrip via ConfigParser ───

        [Fact]
        public void Roundtrip_ConfigParserInput_ProducesStableSerialization()
        {
            var source = @"
class CfgPatches {
    units[] = { ""Soldier_W"", ""Soldier_E"" };
    requiredVersion = 1.0;
    requiredAddons[] = { ""A3_Characters_F"" };
};
";
            var parsed = ParseConfigSource(source);
            var output1 = Serialize(parsed);
            var reparsed = ParseConfigSource(output1);
            var output2 = Serialize(reparsed);

            Assert.Equal(NormalizeLineEndings(output1), NormalizeLineEndings(output2));
        }

        [Fact]
        public void Roundtrip_ExpressionType_PreservesRawValue()
        {
            // Expression-like values are stored as Generic strings by the parser.
            // The serializer preserves them as quoted strings. This tests that
            // values that look like macros/expressions survive roundtrip.
            var source = @"
class TestExpr {
    value = ""__EVAL(3)"";
};
";
            var parsed = ParseConfigSource(source);
            var output = Serialize(parsed);

            Assert.Contains("value = \"__EVAL(3)\"", output);
        }

        [Fact]
        public void Roundtrip_NegativeNumbers_PreservesSign()
        {
            // Parser stores all values as strings (Generic type), so serializer
            // outputs them quoted. The sign is preserved in the string.
            var source = @"
class TestNeg {
    intNeg = -100;
    floatNeg = ""-3.5"";
};
";
            var parsed = ParseConfigSource(source);
            var output = Serialize(parsed);

            Assert.Contains("intNeg = \"-100\"", output);
            Assert.Contains("floatNeg = \"-3.5\"", output);
        }

        [Fact]
        public void Roundtrip_LargeConfig_StableOutput()
        {
            var sb = new StringBuilder();
            sb.AppendLine("class LargeConfig {");
            for (int i = 0; i < 100; i++)
            {
                sb.AppendLine($"\tclass Item{i} {{");
                sb.AppendLine($"\t\tname = \"Item_{i}\";");
                sb.AppendLine($"\t\tvalue = {i * 2};");
                sb.AppendLine($"\t\tfactor = {i * 1.5:F1};");
                sb.AppendLine("\t};");
            }
            sb.AppendLine("};");

            var source = sb.ToString();
            var parsed = ParseConfigSource(source);
            var output1 = Serialize(parsed);
            var reparsed = ParseConfigSource(output1);
            var output2 = Serialize(reparsed);

            Assert.Equal(NormalizeLineEndings(output1), NormalizeLineEndings(output2));
        }

        [Fact]
        public void Serialize_MultipleSerialsProduceIdenticalOutput()
        {
            // Verify serialization is deterministic
            var config = new ParamFile
            {
                Root = new ParamClass("rootClass", new ParamEntry[]
                {
                    new ParamValue("version", 2),
                    new ParamArray("items",
                        new RawValue("rifle"),
                        new RawValue("pistol"),
                        new RawValue(3)),
                    new ParamClass("nested", new ParamEntry[]
                    {
                        new ParamValue("inner", 1.5f),
                    }),
                }),
            };

            var output1 = Serialize(config);
            var output2 = Serialize(config);
            var output3 = Serialize(config);

            Assert.Equal(output1, output2);
            Assert.Equal(output2, output3);
        }

        // ─── Stream serialization tests ───

        [Fact]
        public void Serialize_StreamToStream_ProducesCorrectOutput()
        {
            // Build a ParamFile, write to binary, then use Serialize(Stream, Stream)
            var config = SimplestConfig(
                new ParamValue("test", "stream test"),
                new ParamValue("count", 99));

            using var binStream = new MemoryStream();
            using (var writer = new BIS.Core.Streams.BinaryWriterEx(binStream, true))
            {
                config.Write(writer);
            }

            using var input = new MemoryStream(binStream.ToArray());
            using var output = new MemoryStream();
            ConfigSerializer.Serialize(input, output);

            output.Position = 0;
            using var reader = new StreamReader(output);
            var text = reader.ReadToEnd();

            Assert.Contains("test = \"stream test\"", text);
            Assert.Contains("count = 99", text);
        }

        [Fact]
        public void Serialize_ParamFileToStream_ProducesCorrectOutput()
        {
            var config = SimplestConfig(
                new ParamValue("key", "direct stream"));

            using var output = new MemoryStream();
            ConfigSerializer.Serialize(config, output);

            output.Position = 0;
            using var reader = new StreamReader(output);
            var text = reader.ReadToEnd();

            Assert.Contains("key = \"direct stream\"", text);
        }

        // ─── Helpers ───

        private static ParamFile SimpleConfig(params ParamEntry[] entries)
        {
            return SimplestConfig(entries);
        }

        private static ParamFile SimplestConfig(params ParamEntry[] entries)
        {
            return new ParamFile
            {
                Root = new ParamClass("rootClass", entries),
            };
        }

        private static ParamFile ParseConfigSource(string source)
        {
            var tokens = ConfigTokenizer.Tokenize(source, "test.cpp");
            var parser = new ConfigParser();
            return parser.Parse(tokens);
        }

        private static string NormalizeLineEndings(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
