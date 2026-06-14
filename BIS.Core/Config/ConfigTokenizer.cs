#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BIS.Core.Config
{
    public enum ConfigTokenType
    {
        Eof,
        Identifier,
        StringLiteral,
        IntegerLiteral,
        FloatLiteral,
        OpenBrace,      // {
        CloseBrace,     // }
        Semicolon,      // ;
        Assign,         // =
        OpenBracket,    // [
        CloseBracket,   // ]
        Comma,          // ,
        Colon,          // :
        PlusAssign,     // +=
        Newline,        // \n (preserved for preprocessor line tracking)
    }

    public readonly struct ConfigToken
    {
        public ConfigTokenType Type { get; }
        public string Value { get; }
        public int Line { get; }
        public string File { get; }

        public ConfigToken(ConfigTokenType type, string value, int line, string file)
        {
            Type = type;
            Value = value;
            Line = line;
            File = file;
        }

        public override string ToString() =>
            Type == ConfigTokenType.Identifier || Type == ConfigTokenType.StringLiteral
                ? $"{Type}(\"{Value}\" at {File}:{Line})"
                : $"{Type}(\"{Value}\" at {File}:{Line})";
    }

    public static class ConfigTokenizer
    {
        public static List<ConfigToken> Tokenize(string source, string fileName)
        {
            var tokens = new List<ConfigToken>();
            int i = 0;
            int line = 1;

            while (i < source.Length)
            {
                char c = source[i];

                // Whitespace — preserve newlines for preprocessor line ending detection
                if (char.IsWhiteSpace(c))
                {
                    if (c == '\n')
                    {
                        tokens.Add(new ConfigToken(ConfigTokenType.Newline, "\n", line, fileName));
                        line++;
                    }
                    i++;
                    continue;
                }

                // Single-line comment
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    i += 2;
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }

                // Multi-line comment
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                    {
                        if (source[i] == '\n') line++;
                        i++;
                    }
                    if (i + 1 < source.Length) i += 2; // skip */
                    continue;
                }

                // String literal (double or single quoted)
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    var sb = new StringBuilder();
                    while (i < source.Length && source[i] != quote)
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            i++;
                            switch (source[i])
                            {
                                case 'n': sb.Append('\n'); break;
                                case 't': sb.Append('\t'); break;
                                case 'r': sb.Append('\r'); break;
                                case '\\': sb.Append('\\'); break;
                                case '"': sb.Append('"'); break;
                                case '\'': sb.Append('\''); break;
                                default: sb.Append('\\'); sb.Append(source[i]); break;
                            }
                        }
                        else
                        {
                            sb.Append(source[i]);
                        }
                        i++;
                    }
                    if (i < source.Length) i++; // skip closing quote
                    tokens.Add(new ConfigToken(ConfigTokenType.StringLiteral, sb.ToString(), line, fileName));
                    continue;
                }

                // Numbers
                if (char.IsDigit(c) || (c == '-' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
                {
                    int start = i;
                    if (c == '-') i++;
                    bool isFloat = false;
                    while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.'))
                    {
                        if (source[i] == '.') isFloat = true;
                        i++;
                    }
                    var numStr = source[start..i];
                    tokens.Add(isFloat
                        ? new ConfigToken(ConfigTokenType.FloatLiteral, numStr, line, fileName)
                        : new ConfigToken(ConfigTokenType.IntegerLiteral, numStr, line, fileName));
                    continue;
                }

                // Two-character operators
                if (c == '+' && i + 1 < source.Length && source[i + 1] == '=')
                {
                    tokens.Add(new ConfigToken(ConfigTokenType.PlusAssign, "+=", line, fileName));
                    i += 2;
                    continue;
                }

                // Single-character tokens
                switch (c)
                {
                    case '{': tokens.Add(new ConfigToken(ConfigTokenType.OpenBrace, "{", line, fileName)); i++; continue;
                    case '}': tokens.Add(new ConfigToken(ConfigTokenType.CloseBrace, "}", line, fileName)); i++; continue;
                    case ';': tokens.Add(new ConfigToken(ConfigTokenType.Semicolon, ";", line, fileName)); i++; continue;
                    case '=': tokens.Add(new ConfigToken(ConfigTokenType.Assign, "=", line, fileName)); i++; continue;
                    case '[': tokens.Add(new ConfigToken(ConfigTokenType.OpenBracket, "[", line, fileName)); i++; continue;
                    case ']': tokens.Add(new ConfigToken(ConfigTokenType.CloseBracket, "]", line, fileName)); i++; continue;
                    case ',': tokens.Add(new ConfigToken(ConfigTokenType.Comma, ",", line, fileName)); i++; continue;
                    case ':': tokens.Add(new ConfigToken(ConfigTokenType.Colon, ":", line, fileName)); i++; continue;
                }

                // Identifier (or keyword)
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                        i++;
                    var word = source[start..i];
                    tokens.Add(new ConfigToken(ConfigTokenType.Identifier, word, line, fileName));
                    continue;
                }

                // Skip unknown characters (should not happen in valid config)
                i++;
            }

            tokens.Add(new ConfigToken(ConfigTokenType.Eof, "", line, fileName));
            return tokens;
        }
    }
}
