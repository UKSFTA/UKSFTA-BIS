#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BIS.SQF;

/// <summary>
/// Tokenizer for Arma 3 SQF source files.
/// Breaks source text into tokens for the SQF parser.
/// </summary>
public class SqfTokenizer
{
    private readonly string _source;
    private readonly string _file;
    private int _pos;
    private int _line;
    private int _column;

    public SqfTokenizer(string source, string file = "")
    {
        _source = source;
        _file = file;
        _pos = 0;
        _line = 1;
        _column = 1;
    }

    /// <summary>Tokenize the entire source into a list of tokens.</summary>
    public List<SqfToken> Tokenize()
    {
        var tokens = new List<SqfToken>();
        SqfToken token;
        while ((token = NextToken()).Type != SqfTokenType.Eof)
        {
            tokens.Add(token);
        }
        tokens.Add(token); // Eof
        return tokens;
    }

    /// <summary>Read the next token from source.</summary>
    public SqfToken NextToken()
    {
        SkipWhitespaceAndComments();

        if (_pos >= _source.Length)
            return MakeToken(SqfTokenType.Eof, "");

        var ch = Peek();
        var startLine = _line;
        var startCol = _column;

        // Single-character tokens
        switch (ch)
        {
            case ';': return Advance(SqfTokenType.Semicolon, ";");
            case '(': return Advance(SqfTokenType.LParen, "(");
            case ')': return Advance(SqfTokenType.RParen, ")");
            case '[': return Advance(SqfTokenType.LBracket, "[");
            case ']': return Advance(SqfTokenType.RBracket, "]");
            case '{': return Advance(SqfTokenType.LBrace, "{");
            case '}': return Advance(SqfTokenType.RBrace, "}");
            case ',': return Advance(SqfTokenType.Comma, ",");
            case ':': return Advance(SqfTokenType.Colon, ":");
            case '~': return Advance(SqfTokenType.Tilde, "~");
            case '#':
                if (IsAtLineStart())
                {
                    // Preprocessor directive (e.g. #include, #define) — consume entire line
                    var ppLine = _line;
                    var ppCol = _column;
                    var start = _pos;
                    while (_pos < _source.Length && _source[_pos] != '\n')
                    {
                        _pos++;
                        _column++;
                    }
                    return new SqfToken(SqfTokenType.Preprocessor, _source.Substring(start, _pos - start), _file, ppLine, ppCol);
                }
                return Advance(SqfTokenType.Hash, "#");
            case '?': return Advance(SqfTokenType.Question, "?");
            case '%': return Advance(SqfTokenType.Percent, "%");
            case '^': return Advance(SqfTokenType.Caret, "^");
        }

        // Dot (but not start of a number like .5)
        if (ch == '.' && !(_pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1])))
            return Advance(SqfTokenType.Dot, ".");

        // Multi-character operators
        if (ch == '!')
        {
            if (MatchNext('='))
                return Advance(SqfTokenType.NotEqual, "!=", startLine, startCol);
            return Advance(SqfTokenType.Not, "!", startLine, startCol);
        }

        if (ch == '=')
        {
            if (MatchNext('='))
                return Advance(SqfTokenType.Equal, "==", startLine, startCol);
            return Advance(SqfTokenType.Assign, "=", startLine, startCol);
        }

        if (ch == '<')
        {
            if (MatchNext('='))
                return Advance(SqfTokenType.LessEqual, "<=", startLine, startCol);
            return Advance(SqfTokenType.Less, "<", startLine, startCol);
        }

        if (ch == '>')
        {
            if (MatchNext('='))
                return Advance(SqfTokenType.GreaterEqual, ">=", startLine, startCol);
            return Advance(SqfTokenType.Greater, ">", startLine, startCol);
        }

        if (ch == '&')
        {
            ExpectNext('&', "Expected &&");
            return Advance(SqfTokenType.And, "&&", startLine, startCol);
        }

        if (ch == '|')
        {
            ExpectNext('|', "Expected ||");
            return Advance(SqfTokenType.Or, "||", startLine, startCol);
        }

        // Arithmetic: + - *
        if (ch == '+') return Advance(SqfTokenType.Plus, "+");
        if (ch == '-') return Advance(SqfTokenType.Minus, "-");
        if (ch == '*') return Advance(SqfTokenType.Star, "*");
        if (ch == '/') return Advance(SqfTokenType.Slash, "/");

        // String
        if (ch == '"' || ch == '\'')
            return ReadString(ch);

        // Number ($ hex or decimal)
        if (ch == '$' || char.IsDigit(ch) || (ch == '.' && _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1])))
            return ReadNumber();

        // Identifier or keyword
        if (char.IsLetter(ch) || ch == '_')
            return ReadIdentifier();

        throw new FormatException($"Unexpected character '{ch}' at {_file}({_line},{_column})");
    }

    private SqfToken MakeToken(SqfTokenType type, string text, int? line = null, int? col = null)
    {
        return new SqfToken(type, text, _file, line ?? _line, col ?? _column);
    }

    private SqfToken Advance(SqfTokenType type, string text, int? line = null, int? col = null)
    {
        _pos += text.Length;
        _column += text.Length;
        return new SqfToken(type, text, _file, line ?? _line, col ?? _column);
    }

    private char Peek(int offset = 0)
    {
        var idx = _pos + offset;
        return idx < _source.Length ? _source[idx] : '\0';
    }

    private bool MatchNext(char expected)
    {
        return _pos + 1 < _source.Length && _source[_pos + 1] == expected;
    }

    private void ExpectNext(char expected, string errorMsg)
    {
        if (_pos + 1 >= _source.Length || _source[_pos + 1] != expected)
            throw new FormatException($"{errorMsg} at {_file}({_line},{_column})");
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _source.Length)
        {
            var ch = _source[_pos];

            // Whitespace
            if (ch == ' ' || ch == '\t' || ch == '\r')
            {
                _pos++;
                _column++;
                continue;
            }

            if (ch == '\n')
            {
                _pos++;
                _line++;
                _column = 1;
                continue;
            }

            // Line comment
            if (ch == '/' && _pos + 1 < _source.Length && _source[_pos + 1] == '/')
            {
                _pos += 2;
                _column += 2;
                while (_pos < _source.Length && _source[_pos] != '\n')
                {
                    _pos++;
                    _column++;
                }
                continue;
            }

            // Block comment
            if (ch == '/' && _pos + 1 < _source.Length && _source[_pos + 1] == '*')
            {
                _pos += 2;
                _column += 2;
                while (_pos + 1 < _source.Length && !(_source[_pos] == '*' && _source[_pos + 1] == '/'))
                {
                    if (_source[_pos] == '\n')
                    {
                        _line++;
                        _column = 1;
                    }
                    else
                    {
                        _column++;
                    }
                    _pos++;
                }
                if (_pos + 1 < _source.Length)
                {
                    _pos += 2; // skip */
                    _column += 2;
                }
                continue;
            }

            break;
        }
    }

    private SqfToken ReadString(char quote)
    {
        var startLine = _line;
        var startCol = _column;
        var sb = new StringBuilder();
        _pos++; // skip opening quote
        _column++;

        while (_pos < _source.Length)
        {
            var ch = _source[_pos];
            if (ch == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }

            if (ch == quote)
            {
                // Check for escaped quote (doubled)
                if (_pos + 1 < _source.Length && _source[_pos + 1] == quote)
                {
                    sb.Append(quote);
                    _pos += 2;
                    _column++;
                    continue;
                }
                _pos++; // skip closing quote
                return new SqfToken(SqfTokenType.String, sb.ToString(), _file, startLine, startCol);
            }
            sb.Append(ch);
            _pos++;
        }

        throw new FormatException($"Unterminated string at {_file}({startLine},{startCol})");
    }

    private SqfToken ReadNumber()
    {
        var startLine = _line;
        var startCol = _column;
        var start = _pos;

        // $ hex literal: $FF, $5C, $FFFFFF
        if (_source[_pos] == '$')
        {
            _pos++;
            _column++;
            while (_pos < _source.Length && IsHexDigit(_source[_pos]))
            {
                _pos++;
                _column++;
            }
            var hexText = _source.Substring(start, _pos - start);
            return new SqfToken(SqfTokenType.Number, hexText, _file, startLine, startCol);
        }

        // 0x hex literal: 0xFF
        if (_source[_pos] == '0' && _pos + 1 < _source.Length &&
            (_source[_pos + 1] == 'x' || _source[_pos + 1] == 'X'))
        {
            _pos += 2;
            _column += 2;
            while (_pos < _source.Length && IsHexDigit(_source[_pos]))
            {
                _pos++;
                _column++;
            }
            var hexText = _source.Substring(start, _pos - start);
            return new SqfToken(SqfTokenType.Number, hexText, _file, startLine, startCol);
        }

        // Decimal or float
        while (_pos < _source.Length && char.IsDigit(_source[_pos]))
        {
            _pos++;
            _column++;
        }

        // Fractional part
        if (_pos < _source.Length && _source[_pos] == '.' && _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1]))
        {
            _pos++; // skip .
            _column++;
            while (_pos < _source.Length && char.IsDigit(_source[_pos]))
            {
                _pos++;
                _column++;
            }
        }

        // Scientific notation
        if (_pos < _source.Length && (_source[_pos] == 'e' || _source[_pos] == 'E'))
        {
            _pos++;
            _column++;
            if (_pos < _source.Length && (_source[_pos] == '+' || _source[_pos] == '-'))
            {
                _pos++;
                _column++;
            }
            while (_pos < _source.Length && char.IsDigit(_source[_pos]))
            {
                _pos++;
                _column++;
            }
        }

        var text = _source.Substring(start, _pos - start);
        return new SqfToken(SqfTokenType.Number, text, _file, startLine, startCol);
    }

    private SqfToken ReadIdentifier()
    {
        var startLine = _line;
        var startCol = _column;
        var start = _pos;

        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_'))
        {
            _pos++;
            _column++;
        }

        var text = _source.Substring(start, _pos - start);
        return new SqfToken(SqfTokenType.Identifier, text, _file, startLine, startCol);
    }

    /// <summary>Check if current position is at the start of a line (preceded only by whitespace since last newline).</summary>
    private bool IsAtLineStart()
    {
        if (_pos == 0) return true;
        for (int i = _pos - 1; i >= 0; i--)
        {
            if (_source[i] == '\n') return true;
            if (_source[i] != ' ' && _source[i] != '\t') return false;
        }
        return true; // start of file
    }

    private static bool IsHexDigit(char c) =>
        char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
