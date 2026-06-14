using Xunit;
using BIS.SQF;

namespace BIS.SQF.Test;

public class SqfFormatterTest
{
    // ─── 1. Simple statement ─────────────────────────────────────────

    [Fact]
    public void Format_SimpleStatement_SpacesAroundOperators()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("_x = 1+2;");
        Assert.Equal("_x = 1 + 2;\n", result);
    }

    // ─── 2. If-then-else block with indentation ──────────────────────

    [Fact]
    public void Format_IfThenElse_IndentsBlocks()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("if (alive _unit) then {_x = 1;} else {_x = 2;};");
        var expected =
            "if (alive _unit) then {\n" +
            "    _x = 1;\n" +
            "} else {\n" +
            "    _x = 2;\n" +
            "};\n";
        Assert.Equal(expected, result);
    }

    // ─── 3. Nested braces (for loop inside if) ───────────────────────

    [Fact]
    public void Format_NestedBraces_IndentsCorrectly()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("for \"_i\" from 0 to 10 do {if (_i > 5) then {_x = _x + 1;};};");
        var expected =
            "for \"_i\" from 0 to 10 do {\n" +
            "    if (_i > 5) then {\n" +
            "        _x = _x + 1;\n" +
            "    };\n" +
            "};\n";
        Assert.Equal(expected, result);
    }

    // ─── 4. Array literal spacing ────────────────────────────────────

    [Fact]
    public void Format_ArrayLiteral_SpacesAfterCommas()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("[1,2,3]");
        Assert.Equal("[1, 2, 3]\n", result);
    }

    // ─── 5. Comments preserved (line comment before code) ────────────

    [Fact]
    public void Format_LineCommentBeforeCode_Preserved()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("// init\n_x = 1;");
        Assert.Equal("// init\n_x = 1;\n", result);
    }

    // ─── 6. Preprocessor directive preserved ─────────────────────────

    [Fact]
    public void Format_PreprocessorDirective_Preserved()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("#include \"foo.h\"\n_x = 1;");
        Assert.Equal("#include \"foo.h\"\n_x = 1;\n", result);
    }

    // ─── 7. Empty braces ─────────────────────────────────────────────

    [Fact]
    public void Format_EmptyBraces_StaysOneLine()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("{}");
        Assert.Equal("{}\n", result);
    }

    // ─── 8. Allman brace style option ────────────────────────────────

    [Fact]
    public void Format_AllmanStyle_BraceOnOwnLine()
    {
        var options = new SqfFormatterOptions { Style = BraceStyle.Allman };
        var formatter = new SqfFormatter(options);
        var result = formatter.Format("if (cond) then {_x = 1;};");
        var expected =
            "if (cond) then\n" +
            "{\n" +
            "    _x = 1;\n" +
            "};\n";
        Assert.Equal(expected, result);
    }

    // ─── 9. Tab indentation option ───────────────────────────────────

    [Fact]
    public void Format_TabIndentation_UsesTabs()
    {
        var options = new SqfFormatterOptions { UseTabs = true, IndentSize = 4 };
        var formatter = new SqfFormatter(options);
        var result = formatter.Format("if (cond) then {_x = 1;};");
        var expected =
            "if (cond) then {\n" +
            "\t_x = 1;\n" +
            "};\n";
        Assert.Equal(expected, result);
    }

    // ─── 10. No trailing whitespace ──────────────────────────────────

    [Fact]
    public void Format_NoTrailingWhitespace_Stripped()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("_x = 1;   \n   \n_y = 2;");
        Assert.Equal("_x = 1;\n_y = 2;\n", result);
    }

    // ─── 11. Operator spacing with ==, !=, &&, || ────────────────────

    [Fact]
    public void Format_ComparisonAndLogicalOperators_SpacesAround()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("_a==_b&&_c!=_d||_e");
        Assert.Equal("_a == _b && _c != _d || _e\n", result);
    }

    // ─── 12. Parenthesized expression spacing ────────────────────────

    [Fact]
    public void Format_ParenthesizedExpression_SpacesInsideFromOperators()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("(a+b)");
        Assert.Equal("(a + b)\n", result);
    }

    // ─── 13. Block comment preservation ──────────────────────────────

    [Fact]
    public void Format_BlockComment_Preserved()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("/* init */\n_x = 1;");
        Assert.Equal("/* init */\n_x = 1;\n", result);
    }

    [Fact]
    public void Format_MultiLineBlockComment_Preserved()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("/*\n * comment\n */\n_x = 1;");
        Assert.Equal("/*\n * comment\n */\n_x = 1;\n", result);
    }

    // ─── 14. Negation operator (no space after !) ────────────────────

    [Fact]
    public void Format_NegationOperator_NoSpaceAfter()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("if (!_x) then {hint \"a\";};");
        var expected = "if (!_x) then {\n    hint \"a\";\n};\n";
        Assert.Equal(expected, result);
    }

    // ─── 15. Ternary operator ────────────────────────────────────────

    [Fact]
    public void Format_TernaryOperator_SpacesAround()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("_x = cond?a:b;");
        Assert.Equal("_x = cond ? a : b;\n", result);
    }

    // ─── 16. Preprocessor among code ─────────────────────────────────

    [Fact]
    public void Format_PreprocessorAmongCode_Preserved()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("#define MACRO 1\nif (cond) then {};");
        Assert.Equal("#define MACRO 1\nif (cond) then {};\n", result);
    }

    // ─── 17. Empty source ────────────────────────────────────────────

    [Fact]
    public void Format_EmptySource_ReturnsNewline()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("");
        Assert.Equal("\n", result);
    }

    // ─── 18. Allman style with nested blocks ─────────────────────────

    [Fact]
    public void Format_AllmanStyle_NestedBlocks()
    {
        var options = new SqfFormatterOptions { Style = BraceStyle.Allman };
        var formatter = new SqfFormatter(options);
        var result = formatter.Format("for \"_i\" from 0 to 10 do {if (_i > 5) then {_x = _x + 1;};};");
        var expected =
            "for \"_i\" from 0 to 10 do\n" +
            "{\n" +
            "    if (_i > 5) then\n" +
            "    {\n" +
            "        _x = _x + 1;\n" +
            "    };\n" +
            "};\n";
        Assert.Equal(expected, result);
    }

    // ─── 19. While loop ─────────────────────────────────────────────

    [Fact]
    public void Format_WhileLoop_IndentsBody()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("while {_x > 0} do {_x = _x - 1;};");
        var expected =
            "while {\n    _x > 0\n} do {\n    _x = _x - 1;\n};\n";
        Assert.Equal(expected, result);
    }

    // ─── 20. ForEach loop ────────────────────────────────────────────

    [Fact]
    public void Format_ForEachLoop_IndentsBody()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("{_x = _x + 1;} forEach _arr;");
        Assert.Equal("{\n    _x = _x + 1;\n} forEach _arr;\n", result);
    }

    // ─── 21. Inline comment after code ───────────────────────────────

    [Fact]
    public void Format_InlineComment_PreservedOnOwnLine()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("_x = 1; // set x\n_y = 2;");
        Assert.Equal("_x = 1;\n// set x\n_y = 2;\n", result);
    }

    // ─── 22. Multiple blank lines collapsed ──────────────────────────

    [Fact]
    public void Format_MultipleBlankLines_Collapsed()
    {
        var formatter = new SqfFormatter();
        var result = formatter.Format("_x = 1;\n\n\n\n_y = 2;");
        // Blank lines between statements are collapsed (semicolons produce newlines)
        Assert.Equal("_x = 1;\n_y = 2;\n", result);
    }
}
