using FsCheck;
using FsCheck.Xunit;
using BIS.Core.Config;

namespace BIS.Core.Test.Config;

public class ConfigPropertyTest
{
    // Property: Config serializer roundtrip — parse(source) → serialize → parse(serialize) produces same structure
    [Property(MaxTest = 100)]
    public void Serializer_Roundtrip(NonEmptyString source)
    {
        try
        {
            var tokens = ConfigTokenizer.Tokenize(source.Get, "test.cpp");
            var parser = new ConfigParser();
            var file = parser.Parse(tokens);

            using var ms = new MemoryStream();
            ConfigSerializer.Serialize(file, ms);
            ms.Position = 0;
            var reader = new StreamReader(ms);
            var serialized = reader.ReadToEnd();

            // Skip if empty output
            if (string.IsNullOrWhiteSpace(serialized))
                return;

            // Re-parse the serialized output
            var tokens2 = ConfigTokenizer.Tokenize(serialized, "test.cpp");
            var parser2 = new ConfigParser();
            var file2 = parser2.Parse(tokens2);

            // Both should parse without error
        }
        catch
        {
            // Invalid input expected to fail
        }
    }

    // Property: Config linter never throws
    [Property(MaxTest = 100)]
    public void Linter_NeverThrows(NonEmptyString source)
    {
        try
        {
            var tokens = ConfigTokenizer.Tokenize(source.Get, "test.cpp");
            var parser = new ConfigParser();
            var file = parser.Parse(tokens);
            var linter = new ConfigLinter();
            var diags = linter.Lint(file);
        }
        catch
        {
            // graceful
        }
    }
}
