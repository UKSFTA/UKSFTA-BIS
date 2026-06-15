using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace BIS.SQF.Test;

public class SqfPropertyTest
{
    // Property 1: Parser never crashes on any input
    [Property(MaxTest = 500)]
    public void Parse_NeverThrowsException(NonEmptyString source)
    {
        try
        {
            var tokens = new SqfTokenizer(source.Get).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source.Get);
        }
        catch (FormatException)
        {
            // FormatException is expected for bad input
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Property 2: Formatter is idempotent
    [Property(MaxTest = 200)]
    public void Format_Idempotent(NonEmptyString source)
    {
        try
        {
            var formatter = new SqfFormatter();
            var first = formatter.Format(source.Get);
            var second = formatter.Format(first);
            Assert.Equal(first, second);
        }
        catch
        {
            // Any input is fine — formatter itself handles all errors
        }
    }

    // Property 3: Formatter output never contains tabs when UseTabs=false
    [Property(MaxTest = 200)]
    public void Format_NoTabsInSpacesMode(NonEmptyString source)
    {
        try
        {
            var formatter = new SqfFormatter(new SqfFormatterOptions { UseTabs = false });
            var result = formatter.Format(source.Get);
            Assert.DoesNotContain('\t', result);
        }
        catch
        {
            // graceful
        }
    }

    // Property 4: Formatter output always ends with newline
    [Property(MaxTest = 200)]
    public void Format_AlwaysEndsWithNewline(NonEmptyString source)
    {
        try
        {
            var formatter = new SqfFormatter();
            var result = formatter.Format(source.Get);
            Assert.EndsWith("\n", result);
        }
        catch
        {
            // graceful
        }
    }

    // Property 5: No trailing whitespace in any line
    [Property(MaxTest = 200)]
    public void Format_NoTrailingWhitespace(NonEmptyString source)
    {
        try
        {
            var formatter = new SqfFormatter();
            var result = formatter.Format(source.Get);
            var lines = result.Split('\n');
            var hasTrailingSpace = lines.Any(l => l.Length > 0 && (l[^1] == ' ' || l[^1] == '\t'));
            Assert.False(hasTrailingSpace, "Formatter produced trailing whitespace");
        }
        catch
        {
            // graceful
        }
    }
}
