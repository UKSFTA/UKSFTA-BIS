#nullable enable
namespace BIS.SQF;

/// <summary>Token types for SQF lexical analysis.</summary>
public enum SqfTokenType
{
    Eof,
    Identifier,
    Number,
    String,
    Semicolon,
    LParen, RParen,
    LBracket, RBracket,
    LBrace, RBrace,
    Comma,
    Colon,
    Plus, Minus, Star, Slash, Percent, Caret,
    Equal, NotEqual,
    Less, Greater, LessEqual, GreaterEqual,
    And, Or, Not,
    Assign,
    Tilde,
    Hash,
    Preprocessor,
    Dot,
    Question,
}

/// <summary>A single token from SQF source.</summary>
public readonly struct SqfToken
{
    public SqfTokenType Type { get; }
    public string Text { get; }
    public string File { get; }
    public int Line { get; }
    public int Column { get; }

    public SqfToken(SqfTokenType type, string text, string file = "", int line = 0, int column = 0)
    {
        Type = type;
        Text = text;
        File = file;
        Line = line;
        Column = column;
    }

    public override string ToString()
    {
        var loc = string.IsNullOrEmpty(File) ? "" : $"{File}({Line},{Column})";
        return $"{Type}: '{Text}' {loc}".Trim();
    }
}
