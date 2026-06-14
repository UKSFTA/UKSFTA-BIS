#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BIS.Core.Config
{
    /// <summary>
    /// Recursive descent parser for Arma config.cpp source files.
    /// Produces a ParamFile AST that can be serialized with ParamFile.Write().
    /// </summary>
    public class ConfigParser
    {
        private List<ConfigToken> _tokens;
        private int _pos;

        public ConfigParser()
        {
            _tokens = new List<ConfigToken>();
            _pos = 0;
        }

        /// <summary>
        /// Preprocessor diagnostics collected during the last call to ParseFile.
        /// Null if Parse was used directly without preprocessing.
        /// </summary>
        public IReadOnlyList<PreprocessorDiagnostic>? PreprocessorDiagnostics { get; private set; }

        /// <summary>
        /// Parse a list of preprocessed config tokens into a ParamFile.
        /// </summary>
        public ParamFile Parse(IEnumerable<ConfigToken> tokens)
        {
            _tokens = tokens.ToList();
            _pos = 0;

            var paramFile = new ParamFile();
            var rootClass = new ParamClass("rootClass", ParseEntries());
            paramFile.Root = rootClass;
            return paramFile;
        }

        /// <summary>
        /// Preprocess and parse a config.cpp file, collecting preprocessor diagnostics
        /// that can be accessed via PreprocessorDiagnostics.
        /// </summary>
        public ParamFile ParseFile(string sourcePath, IIncludeResolver? resolver = null)
        {
            resolver ??= new DefaultIncludeResolver(Enumerable.Empty<string>());
            var preprocessor = new ConfigPreprocessor(resolver);
            var tokens = preprocessor.Preprocess(sourcePath);
            PreprocessorDiagnostics = preprocessor.Diagnostics;
            return Parse(tokens);
        }

        // ── Parser Methods ──

        private List<ParamEntry> ParseEntries()
        {
            var entries = new List<ParamEntry>();

            while (Peek().Type != ConfigTokenType.Eof &&
                   Peek().Type != ConfigTokenType.CloseBrace)
            {
                var entry = ParseEntry();
                if (entry != null)
                    entries.Add(entry);
            }

            return entries;
        }

        private ParamEntry? ParseEntry()
        {
            // Skip stray semicolons
            if (Peek().Type == ConfigTokenType.Semicolon)
            {
                Advance();
                return null;
            }

            // Must be an identifier at this point
            if (Peek().Type != ConfigTokenType.Identifier)
            {
                // Skip unexpected token
                Advance();
                return null;
            }

            var keyword = Peek().Value;

            // class definition
            if (keyword.Equals("class", StringComparison.OrdinalIgnoreCase))
                return ParseClass();

            // delete class
            if (keyword.Equals("delete", StringComparison.OrdinalIgnoreCase))
                return ParseDeleteClass();

            // enum declaration
            if (keyword.Equals("enum", StringComparison.OrdinalIgnoreCase))
            {
                SkipEnum();
                return null;
            }

            // Regular assignment or sub-class
            return ParseAssignmentOrProperty();
        }

        private ParamEntry ParseClass()
        {
            // "class" Identifier [":" Identifier] (";" | "{" Entries "}" ";")
            var classToken = Peek();
            Advance(); // consume "class"

            // Expect class name
            var nameToken = Consume(ConfigTokenType.Identifier, "Expected class name");

            // Forward declaration: class X;
            if (Peek().Type == ConfigTokenType.Semicolon)
            {
                Advance();
                return new ParamExternClass(nameToken.Value) { Line = nameToken.Line, File = nameToken.File };
            }

            string baseClass = "";
            int baseLine = nameToken.Line;

            // Optional base class
            if (Peek().Type == ConfigTokenType.Colon)
            {
                Advance(); // consume ":"
                var baseToken = Consume(ConfigTokenType.Identifier, "Expected base class name");
                baseClass = baseToken.Value;
            }

            Consume(ConfigTokenType.OpenBrace, "Expected '{' after class declaration");

            var entries = ParseEntries();

            Consume(ConfigTokenType.CloseBrace, "Expected '}' after class body");

            // Optional semicolon
            if (Peek().Type == ConfigTokenType.Semicolon)
                Advance();

            var cls = new ParamClass(nameToken.Value, baseClass, entries);
            cls.Line = baseLine;
            cls.File = nameToken.File;
            return cls;
        }

        private ParamDeleteClass ParseDeleteClass()
        {
            // "delete" Identifier ";"
            Advance(); // consume "delete"
            var nameToken = Consume(ConfigTokenType.Identifier, "Expected class name after 'delete'");
            Consume(ConfigTokenType.Semicolon, "Expected ';' after delete statement");
            return new ParamDeleteClass(nameToken.Value) { Line = nameToken.Line, File = nameToken.File };
        }

        private ParamEntry? ParseAssignmentOrProperty()
        {
            // This could be:
            //   Identifier "=" value ";"
            //   Identifier "[" "]" ("="|"+=") "{" values "}" ";"
            //   Identifier "=" "{" values "}" ";"
            //   Identifier "=" Identifier ";"

            var nameToken = Consume(ConfigTokenType.Identifier, "Expected identifier");

            // If followed by ';', it's a bare identifier (enum value or something) — skip
            if (Peek().Type == ConfigTokenType.Semicolon)
            {
                Advance();
                return null;
            }

            // If followed by ':', it might be a class with missing 'class' keyword or enum
            if (Peek().Type == ConfigTokenType.Colon)
            {
                // Treat as class without 'class' keyword (some mods do this)
                Advance(); // consume ":"
                var baseToken = Consume(ConfigTokenType.Identifier, "Expected base class name");
                Consume(ConfigTokenType.OpenBrace, "Expected '{'");
                var entries = ParseEntries();
                Consume(ConfigTokenType.CloseBrace, "Expected '}'");
                if (Peek().Type == ConfigTokenType.Semicolon)
                    Advance();
                return new ParamClass(nameToken.Value, baseToken.Value, entries) { Line = nameToken.Line, File = nameToken.File };
            }

            // Check for array: Identifier "[" "]"
            if (Peek().Type == ConfigTokenType.OpenBracket)
            {
                Advance(); // consume "["
                Consume(ConfigTokenType.CloseBracket, "Expected ']'");

                // += or =
                bool plusAssign = false;
                if (Peek().Type == ConfigTokenType.PlusAssign)
                {
                    plusAssign = true;
                    Advance();
                }
                else
                {
                    Consume(ConfigTokenType.Assign, "Expected '=' or '+=' after '[]'");
                }

                // Array value can be: "{" values "}" or value
                var values = ParseArrayValues();
                Consume(ConfigTokenType.Semicolon, "Expected ';' after array assignment");

                if (plusAssign)
                {
                    return new ParamArraySpec(nameToken.Value, 1, values) { Line = nameToken.Line, File = nameToken.File };
                }
                return new ParamArray(nameToken.Value, values) { Line = nameToken.Line, File = nameToken.File };
            }

            // Regular assignment: Identifier "=" value ";"
            if (Peek().Type == ConfigTokenType.Assign)
            {
                Advance(); // consume "="
                var value = ParseValue();
                Consume(ConfigTokenType.Semicolon, "Expected ';' after value assignment");
                return new ParamValue(nameToken.Value, value) { Line = nameToken.Line, File = nameToken.File };
            }

            // If followed by '{', treat as inline class without 'class' keyword
            if (Peek().Type == ConfigTokenType.OpenBrace)
            {
                Advance(); // consume "{"
                var entries = ParseEntries();
                Consume(ConfigTokenType.CloseBrace, "Expected '}'");
                if (Peek().Type == ConfigTokenType.Semicolon)
                    Advance();
                return new ParamClass(nameToken.Value, entries) { Line = nameToken.Line, File = nameToken.File };
            }

            // Unknown construct — skip to next semicolon or brace
            SkipToNextStatement();
            return null;
        }

        private string ParseValue()
        {
            var token = Peek();

            switch (token.Type)
            {
                case ConfigTokenType.StringLiteral:
                    Advance();
                    return token.Value;

                case ConfigTokenType.IntegerLiteral:
                case ConfigTokenType.FloatLiteral:
                    Advance();
                    return token.Value;

                case ConfigTokenType.Identifier:
                    // Could be a macro-expanded value, a boolean constant, or an enum reference
                    Advance();
                    return token.Value;

                case ConfigTokenType.OpenBrace:
                    // Inline array: { values }
                    var values = ParseArrayValues();
                    // Return array as string representation
                    var valStr = string.Join(", ", values.Select(v => v.Value));
                    return $"{{{valStr}}}";

                default:
                    Advance();
                    return token.Value;
            }
        }

        private List<RawValue> ParseArrayValues()
        {
            var values = new List<RawValue>();

            Consume(ConfigTokenType.OpenBrace, "Expected '{' for array values");

            while (Peek().Type != ConfigTokenType.CloseBrace && Peek().Type != ConfigTokenType.Eof)
            {
                values.Add(ParseRawValue());

                if (Peek().Type == ConfigTokenType.Comma)
                    Advance();
                else
                    break;
            }

            Consume(ConfigTokenType.CloseBrace, "Expected '}' to close array");
            return values;
        }

        private RawValue ParseRawValue()
        {
            var token = Peek();

            switch (token.Type)
            {
                case ConfigTokenType.StringLiteral:
                    Advance();
                    return new RawValue(token.Value);

                case ConfigTokenType.IntegerLiteral:
                    Advance();
                    if (long.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
                    {
                        if (longVal >= int.MinValue && longVal <= int.MaxValue)
                            return new RawValue((int)longVal);
                        return new RawValue(longVal);
                    }
                    return new RawValue(token.Value);

                case ConfigTokenType.FloatLiteral:
                    Advance();
                    if (float.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fVal))
                        return new RawValue(fVal);
                    return new RawValue(token.Value);

                case ConfigTokenType.Identifier:
                    Advance();
                    // Check for true/false
                    if (token.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                        return new RawValue(1);
                    if (token.Value.Equals("false", StringComparison.OrdinalIgnoreCase))
                        return new RawValue(0);
                    // Enum reference — store as string
                    return new RawValue(token.Value);

                default:
                    Advance();
                    return new RawValue(token.Value);
            }
        }

        private void SkipEnum()
        {
            // enum X { ... };
            Advance(); // consume "enum"
            if (Peek().Type == ConfigTokenType.Identifier)
                Advance();
            if (Peek().Type == ConfigTokenType.Colon)
            {
                Advance();
                if (Peek().Type == ConfigTokenType.Identifier)
                    Advance();
            }
            if (Peek().Type == ConfigTokenType.OpenBrace)
            {
                int depth = 1;
                Advance();
                while (depth > 0 && Peek().Type != ConfigTokenType.Eof)
                {
                    if (Peek().Type == ConfigTokenType.OpenBrace) depth++;
                    if (Peek().Type == ConfigTokenType.CloseBrace) depth--;
                    Advance();
                }
            }
            if (Peek().Type == ConfigTokenType.Semicolon)
                Advance();
        }

        private void SkipToNextStatement()
        {
            int depth = 0;
            while (Peek().Type != ConfigTokenType.Eof)
            {
                if (Peek().Type == ConfigTokenType.OpenBrace) depth++;
                if (Peek().Type == ConfigTokenType.CloseBrace)
                {
                    if (depth == 0) break;
                    depth--;
                }
                if (Peek().Type == ConfigTokenType.Semicolon && depth == 0)
                {
                    Advance();
                    return;
                }
                Advance();
            }
        }

        // ── Token Helpers ──

        private ConfigToken Peek() => _pos < _tokens.Count ? _tokens[_pos] : new ConfigToken(ConfigTokenType.Eof, "", 0, "");

        private ConfigToken Advance()
        {
            var token = Peek();
            if (_pos < _tokens.Count) _pos++;
            return token;
        }

        private ConfigToken Consume(ConfigTokenType expected, string errorMessage)
        {
            var token = Peek();
            if (token.Type != expected)
                throw new FormatException($"ConfigParser: {errorMessage} at {token.File}:{token.Line} " +
                    $"(got {token.Type}(\"{token.Value}\"), expected {expected})");
            return Advance();
        }
    }
}
