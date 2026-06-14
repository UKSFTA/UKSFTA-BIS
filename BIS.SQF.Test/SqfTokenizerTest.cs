using Xunit;
using BIS.SQF;

namespace BIS.SQF.Test;

public class SqfTokenizerTest
{
    [Fact]
    public void Tokenize_EmptySource_ReturnsEof()
    {
        var tokens = new SqfTokenizer("").Tokenize();
        Assert.Single(tokens);
        Assert.Equal(SqfTokenType.Eof, tokens[0].Type);
    }

    [Fact]
    public void Tokenize_Identifiers()
    {
        var tokens = new SqfTokenizer("player _myVar").Tokenize();
        Assert.Equal(3, tokens.Count);
        Assert.Equal("player", tokens[0].Text);
        Assert.Equal("_myVar", tokens[1].Text);
    }

    [Fact]
    public void Tokenize_Numbers()
    {
        var tokens = new SqfTokenizer("42 3.14 0xFF 1e10").Tokenize();
        Assert.Equal(5, tokens.Count); // 4 nums + eof
        Assert.Equal("42", tokens[0].Text);
        Assert.Equal("3.14", tokens[1].Text);
        Assert.Equal("0xFF", tokens[2].Text);
        Assert.Equal("1e10", tokens[3].Text);
    }

    [Fact]
    public void Tokenize_Strings()
    {
        var tokens = new SqfTokenizer("\"hello\" 'world'").Tokenize();
        Assert.Equal(3, tokens.Count);
        Assert.Equal("hello", tokens[0].Text);
        Assert.Equal("world", tokens[1].Text);
    }

    [Fact]
    public void Tokenize_EscapedQuotes()
    {
        var tokens = new SqfTokenizer("\"it\"\"s\"").Tokenize();
        Assert.Equal(2, tokens.Count);
        Assert.Equal("it\"s", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_Operators()
    {
        var tokens = new SqfTokenizer("+ - * / % ^ == != < > <= >= && || ! = ~").Tokenize();
        var types = tokens.Select(t => t.Type).ToList();
        Assert.Contains(SqfTokenType.Plus, types);
        Assert.Contains(SqfTokenType.Minus, types);
        Assert.Contains(SqfTokenType.Star, types);
        Assert.Contains(SqfTokenType.Slash, types);
        Assert.Contains(SqfTokenType.Percent, types);
        Assert.Contains(SqfTokenType.Caret, types);
        Assert.Contains(SqfTokenType.Equal, types);
        Assert.Contains(SqfTokenType.NotEqual, types);
        Assert.Contains(SqfTokenType.Less, types);
        Assert.Contains(SqfTokenType.Greater, types);
        Assert.Contains(SqfTokenType.LessEqual, types);
        Assert.Contains(SqfTokenType.GreaterEqual, types);
        Assert.Contains(SqfTokenType.And, types);
        Assert.Contains(SqfTokenType.Or, types);
        Assert.Contains(SqfTokenType.Not, types);
        Assert.Contains(SqfTokenType.Assign, types);
        Assert.Contains(SqfTokenType.Tilde, types);
    }

    [Fact]
    public void Tokenize_Punctuation()
    {
        var tokens = new SqfTokenizer("; ( ) [ ] { } , : # ?").Tokenize();
        var types = tokens.Select(t => t.Type).ToList();
        Assert.Contains(SqfTokenType.Semicolon, types);
        Assert.Contains(SqfTokenType.LParen, types);
        Assert.Contains(SqfTokenType.RParen, types);
        Assert.Contains(SqfTokenType.LBracket, types);
        Assert.Contains(SqfTokenType.RBracket, types);
        Assert.Contains(SqfTokenType.LBrace, types);
        Assert.Contains(SqfTokenType.RBrace, types);
        Assert.Contains(SqfTokenType.Comma, types);
        Assert.Contains(SqfTokenType.Colon, types);
        Assert.Contains(SqfTokenType.Hash, types);
        Assert.Contains(SqfTokenType.Question, types);
    }

    [Fact]
    public void Tokenize_Comments_AreSkipped()
    {
        var tokens = new SqfTokenizer("a // line comment\nb /* block */ c").Tokenize();
        Assert.Equal(4, tokens.Count); // a, b, c, eof
        Assert.Equal("a", tokens[0].Text);
        Assert.Equal("b", tokens[1].Text);
        Assert.Equal("c", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_BlockComment_Multiline()
    {
        var tokens = new SqfTokenizer("a /* multi\nline */ b").Tokenize();
        Assert.Equal(3, tokens.Count);
        Assert.Equal("a", tokens[0].Text);
        Assert.Equal("b", tokens[1].Text);
    }

    [Fact]
    public void Tokenize_Dot()
    {
        var tokens = new SqfTokenizer(".").Tokenize();
        Assert.Equal(2, tokens.Count);
        Assert.Equal(SqfTokenType.Dot, tokens[0].Type);
    }

    [Fact]
    public void Tokenize_NumberStartingWithDot()
    {
        var tokens = new SqfTokenizer(".5").Tokenize();
        Assert.Equal(2, tokens.Count);
        Assert.Equal(SqfTokenType.Number, tokens[0].Type);
        Assert.Equal(".5", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_String_Unterminated_Throws()
    {
        Assert.Throws<FormatException>(() => new SqfTokenizer("\"hello").Tokenize());
    }

    [Fact]
    public void Tokenize_ComplexExpression()
    {
        var src = "private _x = (player distance _target) > 10;";
        var tokens = new SqfTokenizer(src).Tokenize();
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Contains("private", texts);
        Assert.Contains("_x", texts);
        Assert.Contains("=", texts);
        Assert.Contains("(", texts);
        Assert.Contains(")", texts);
        Assert.Contains(">", texts);
        Assert.Contains(";", texts);
    }

    [Fact]
    public void Tokenize_HexDollar()
    {
        var tokens = new SqfTokenizer("$FF $5C $FFFFFF").Tokenize();
        Assert.Equal(4, tokens.Count); // 3 nums + eof
        Assert.Equal(SqfTokenType.Number, tokens[0].Type);
        Assert.Equal("$FF", tokens[0].Text);
        Assert.Equal(SqfTokenType.Number, tokens[1].Type);
        Assert.Equal("$5C", tokens[1].Text);
        Assert.Equal(SqfTokenType.Number, tokens[2].Type);
        Assert.Equal("$FFFFFF", tokens[2].Text);
    }

    [Fact]
    public void Tokenize_ScientificNotation()
    {
        var tokens = new SqfTokenizer("5e-2 1E+10 .5e2 3.14e0").Tokenize();
        Assert.Equal(5, tokens.Count); // 4 nums + eof
        Assert.Equal("5e-2", tokens[0].Text);
        Assert.Equal("1E+10", tokens[1].Text);
        Assert.Equal(".5e2", tokens[2].Text);
        Assert.Equal("3.14e0", tokens[3].Text);
    }

    [Fact]
    public void Tokenize_LineTracking()
    {
        var src = "a\nb\nc";
        var tokens = new SqfTokenizer(src).Tokenize();
        Assert.Equal(1, tokens[0].Line);
        Assert.Equal(2, tokens[1].Line);
        Assert.Equal(3, tokens[2].Line);
    }
}
