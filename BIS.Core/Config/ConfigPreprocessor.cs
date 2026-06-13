#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIS.Core.Config
{
    /// <summary>
    /// Resolves #include directives to source text.
    /// </summary>
    public interface IIncludeResolver
    {
        /// <summary>
        /// Attempt to resolve an include path. Returns null if not found.
        /// </summary>
        string? Resolve(string includePath, string currentFile);
    }

    /// <summary>
    /// Default include resolver that searches relative to the current file
    /// and a set of additional search directories.
    /// </summary>
    public class DefaultIncludeResolver : IIncludeResolver
    {
        private readonly List<string> _searchDirs;

        public DefaultIncludeResolver(IEnumerable<string> searchDirs)
        {
            _searchDirs = searchDirs.ToList();
        }

        public string? Resolve(string includePath, string currentFile)
        {
            // Try relative to current file first
            if (!string.IsNullOrEmpty(currentFile))
            {
                var currentDir = Path.GetDirectoryName(currentFile);
                if (!string.IsNullOrEmpty(currentDir))
                {
                    var candidate = Path.GetFullPath(Path.Combine(currentDir, includePath));
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            // Try search directories
            foreach (var dir in _searchDirs)
            {
                var candidate = Path.GetFullPath(Path.Combine(dir, includePath));
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }

    /// <summary>
    /// Config preprocessor that resolves includes, defines, and conditionals.
    /// </summary>
    public class ConfigPreprocessor
    {
        private readonly IIncludeResolver _resolver;
        private readonly Dictionary<string, string?> _defines;
        private readonly Stack<string> _includeStack;
        private readonly HashSet<string> _includedFiles; // guard against circular includes

        public ConfigPreprocessor(IIncludeResolver resolver)
        {
            _resolver = resolver;
            _defines = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            _includeStack = new Stack<string>();
            _includedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public ConfigPreprocessor(IIncludeResolver resolver, Dictionary<string, string?> defines)
            : this(resolver)
        {
            foreach (var kvp in defines)
                _defines[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Preprocess a source file and return a token stream.
        /// </summary>
        public List<ConfigToken> Preprocess(string sourcePath)
        {
            _includedFiles.Clear();
            _includeStack.Clear();
            var allTokens = new List<ConfigToken>();
            PreprocessFile(sourcePath, allTokens);
            return allTokens;
        }

        private void PreprocessFile(string filePath, List<ConfigToken> output)
        {
            string absPath;
            try
            {
                absPath = Path.GetFullPath(filePath);
            }
            catch
            {
                // If path resolution fails, try reading it directly
                absPath = filePath;
            }

            // Circular include guard
            if (!_includedFiles.Add(absPath))
                return;

            _includeStack.Push(absPath);

            string source;
            try
            {
                source = File.ReadAllText(absPath);
            }
            catch
            {
                Console.Error.WriteLine($"ConfigPreprocessor: Cannot open '{filePath}'");
                _includeStack.Pop();
                return;
            }

            var tokens = ConfigTokenizer.Tokenize(source, absPath);
            ProcessTokens(tokens, output);
            _includeStack.Pop();
        }

        private void ProcessTokens(List<ConfigToken> tokens, List<ConfigToken> output)
        {
            var conditionalStack = new Stack<ConditionalState>();
            // default: emitting
            conditionalStack.Push(ConditionalState.Emit);

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                // Check if we're at a preprocessor directive (# at start of logical line)
                if (token.Type == ConfigTokenType.Identifier &&
                    token.Value.Length > 0 && token.Value[0] == '#')
                {
                    i = HandleDirective(token, tokens, i, conditionalStack, output);
                    continue;
                }

                // Emit or skip based on current conditional state
                if (conditionalStack.Peek() == ConditionalState.Emit)
                {
                    // Try macro expansion for identifiers
                    if (token.Type == ConfigTokenType.Identifier && _defines.TryGetValue(token.Value, out var macroValue))
                    {
                        if (macroValue != null)
                        {
                            // Expand macro value as tokens
                            var expandedTokens = ConfigTokenizer.Tokenize(macroValue, token.File);
                            foreach (var et in expandedTokens)
                            {
                                if (et.Type != ConfigTokenType.Eof)
                                    output.Add(et);
                            }
                        }
                        // macro with no value → expand to nothing
                    }
                    else
                    {
                        output.Add(token);
                    }
                }
            }
        }

        private enum ConditionalState
        {
            Emit,
            Skip,
            SkipDone,   // we already found the active branch
        }

        private int HandleDirective(
            ConfigToken hashToken, List<ConfigToken> tokens, int idx,
            Stack<ConditionalState> conditionalStack, List<ConfigToken> output)
        {
            // hashToken is the #identifier token - the directive name follows (or it's just "#" as identifier)
            var directive = hashToken.Value;

            switch (directive.ToLowerInvariant())
            {
                case "#include":
                    return HandleInclude(tokens, idx, output);

                case "#define":
                    return HandleDefine(tokens, idx);

                case "#undef":
                    return HandleUndef(tokens, idx);

                case "#ifdef":
                    return HandleIfdef(tokens, idx, conditionalStack, false);

                case "#ifndef":
                    return HandleIfdef(tokens, idx, conditionalStack, true);

                case "#if":
                    return HandleIf(tokens, idx, conditionalStack);

                case "#else":
                    return HandleElse(tokens, idx, conditionalStack);

                case "#elif":
                    return HandleElif(tokens, idx, conditionalStack);

                case "#endif":
                    return HandleEndif(tokens, idx, conditionalStack);

                case "#error":
                    return HandleError(tokens, idx);

                case "#pragma":
                    return SkipLine(tokens, idx); // ignore pragma

                default:
                    // Unknown directive - skip it
                    return SkipLine(tokens, idx);
            }
        }

        private int HandleInclude(List<ConfigToken> tokens, int idx, List<ConfigToken> output)
        {
            int i = idx + 1;
            if (i >= tokens.Count || tokens[i].Type == ConfigTokenType.Eof)
                return idx;

            // Skip whitespace/newlines (all tokens are non-whitespace, but directives
            // may have the include path on the same logical line)
            // Include path can be: "file" or <file>
            var pathToken = tokens[i];

            if (pathToken.Type == ConfigTokenType.StringLiteral)
            {
                var resolved = _resolver.Resolve(pathToken.Value, _includeStack.Peek());
                if (resolved != null)
                    PreprocessFile(resolved, output);
                else
                    Console.Error.WriteLine($"ConfigPreprocessor: Include not found: \"{pathToken.Value}\" ({_includeStack.Peek()})");
                return SkipLine(tokens, idx);
            }

            // Angle-bracket include via identifier
            if (pathToken.Type == ConfigTokenType.Identifier && pathToken.Value.StartsWith("<"))
            {
                // The lexer may have tokenized <file> as identifier "<file>"
                var incPath = pathToken.Value.Trim('<', '>');
                var resolved = _resolver.Resolve(incPath, _includeStack.Peek());
                if (resolved != null)
                    PreprocessFile(resolved, output);
                else
                    Console.Error.WriteLine($"ConfigPreprocessor: Include not found: <{incPath}> ({_includeStack.Peek()})");
                return SkipLine(tokens, idx);
            }

            // If we get here, it's likely that #include "file.h" was tokenized as:
            // Identifier("#include"), StringLiteral("file.h") — handle normally
            return SkipLine(tokens, idx);
        }

        private int HandleDefine(List<ConfigToken> tokens, int idx)
        {
            int i = idx + 1;

            // Skip whitespace tokens (there shouldn't be any in tokenized output)
            while (i < tokens.Count && tokens[i].Type == ConfigTokenType.Semicolon)
                i++;

            if (i >= tokens.Count || tokens[i].Type != ConfigTokenType.Identifier)
                return SkipLine(tokens, idx);

            var macroName = tokens[i].Value;
            i++;

            // Check if there's a value (anything before next newline/Eof/another directive)
            var valueParts = new List<string>();
            while (i < tokens.Count && tokens[i].Type != ConfigTokenType.Eof)
            {
                var t = tokens[i];
                if (t.Type == ConfigTokenType.Identifier && t.Value.Length > 0 && t.Value[0] == '#')
                    break; // next directive
                if (t.Type == ConfigTokenType.Semicolon)
                    break; // end of line marker
                valueParts.Add(t.Value);
                i++;
            }

            var macroValue = valueParts.Count > 0 ? string.Join("", valueParts) : null;
            _defines[macroName] = macroValue;
            return i - 1;
        }

        private int HandleUndef(List<ConfigToken> tokens, int idx)
        {
            int i = idx + 1;

            while (i < tokens.Count && tokens[i].Type == ConfigTokenType.Semicolon)
                i++;

            if (i < tokens.Count && tokens[i].Type == ConfigTokenType.Identifier)
                _defines.Remove(tokens[i].Value);

            return SkipLine(tokens, idx);
        }

        private int HandleIfdef(List<ConfigToken> tokens, int idx, Stack<ConditionalState> stack, bool negate)
        {
            int i = idx + 1;

            while (i < tokens.Count && tokens[i].Type == ConfigTokenType.Semicolon)
                i++;

            bool defined = i < tokens.Count && tokens[i].Type == ConfigTokenType.Identifier &&
                           _defines.ContainsKey(tokens[i].Value);
            bool emit = negate ? !defined : defined;

            var currentState = stack.Peek();
            if (currentState == ConditionalState.Emit)
            {
                stack.Push(emit ? ConditionalState.Emit : ConditionalState.Skip);
            }
            else
            {
                // already skipping at a higher level
                stack.Push(ConditionalState.SkipDone);
            }

            return SkipLine(tokens, idx);
        }

        private int HandleIf(List<ConfigToken> tokens, int idx, Stack<ConditionalState> stack)
        {
            // Simplified #if handling: evaluate constant expression
            // For now, supports: defined(MACRO), !defined(MACRO), simple integer comparison
            int i = idx + 1;

            while (i < tokens.Count && tokens[i].Type == ConfigTokenType.Semicolon)
                i++;

            var exprTokens = new List<ConfigToken>();
            while (i < tokens.Count && tokens[i].Type != ConfigTokenType.Eof)
            {
                var t = tokens[i];
                if (t.Type == ConfigTokenType.Identifier && t.Value.Length > 0 && t.Value[0] == '#')
                    break;
                exprTokens.Add(t);
                i++;
            }

            bool result = EvaluateIfExpression(exprTokens);

            var currentState = stack.Peek();
            if (currentState == ConditionalState.Emit)
            {
                stack.Push(result ? ConditionalState.Emit : ConditionalState.Skip);
            }
            else
            {
                stack.Push(ConditionalState.SkipDone);
            }

            return SkipLine(tokens, idx);
        }

        private bool EvaluateIfExpression(List<ConfigToken> tokens)
        {
            // Basic expression evaluator for #if
            // Supports: defined(MACRO), !defined(MACRO), integers, ==, !=, ||, &&
            if (tokens.Count == 0)
                return false;

            // Simple cases: defined(MACRO)
            if (TryMatchDefined(tokens, out var result))
                return result;

            // Simple: single integer (0=false, non-zero=true)
            if (tokens.Count == 1 && tokens[0].Type == ConfigTokenType.IntegerLiteral)
                return tokens[0].Value != "0";

            // Simple: identifier (check if defined and non-zero)
            if (tokens.Count == 1 && tokens[0].Type == ConfigTokenType.Identifier)
            {
                if (_defines.TryGetValue(tokens[0].Value, out var val) && val != null)
                {
                    if (int.TryParse(val, out var num))
                        return num != 0;
                    return !string.IsNullOrEmpty(val);
                }
                return _defines.ContainsKey(tokens[0].Value);
            }

            // Not: !defined(MACRO) or !identifier
            if (tokens.Count >= 2 && tokens[0].Type == ConfigTokenType.Identifier && tokens[0].Value == "!")
            {
                var rest = tokens.Skip(1).ToList();
                return !EvaluateIfExpression(rest);
            }

            // Logical OR: expr || expr
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Type == ConfigTokenType.Identifier && tokens[i].Value == "||")
                {
                    var left = tokens.Take(i).ToList();
                    var right = tokens.Skip(i + 1).ToList();
                    return EvaluateIfExpression(left) || EvaluateIfExpression(right);
                }
            }

            // Logical AND: expr && expr
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Type == ConfigTokenType.Identifier && tokens[i].Value == "&&")
                {
                    var left = tokens.Take(i).ToList();
                    var right = tokens.Skip(i + 1).ToList();
                    return EvaluateIfExpression(left) && EvaluateIfExpression(right);
                }
            }

            // Comparison: left == right or left != right
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Type == ConfigTokenType.Identifier &&
                    (tokens[i].Value == "==" || tokens[i].Value == "!="))
                {
                    var left = tokens.Take(i).ToList();
                    var right = tokens.Skip(i + 1).ToList();
                    var lVal = GetExpressionValue(left);
                    var rVal = GetExpressionValue(right);
                    return tokens[i].Value == "==" ? lVal == rVal : lVal != rVal;
                }
            }

            return false;
        }

        private bool TryMatchDefined(List<ConfigToken> tokens, out bool result)
        {
            result = false;
            // defined(MACRO)
            if (tokens.Count >= 4 &&
                tokens[0].Type == ConfigTokenType.Identifier && tokens[0].Value.Equals("defined", StringComparison.OrdinalIgnoreCase) &&
                tokens[1].Type == ConfigTokenType.OpenBrace &&
                tokens[2].Type == ConfigTokenType.Identifier &&
                tokens[3].Type == ConfigTokenType.CloseBrace)
            {
                result = _defines.ContainsKey(tokens[2].Value);
                return true;
            }

            // defined MACRO (without parentheses - GCC extension, also supported by Armake)
            if (tokens.Count >= 2 &&
                tokens[0].Type == ConfigTokenType.Identifier && tokens[0].Value.Equals("defined", StringComparison.OrdinalIgnoreCase) &&
                tokens[1].Type == ConfigTokenType.Identifier)
            {
                result = _defines.ContainsKey(tokens[1].Value);
                return true;
            }

            return false;
        }

        private string GetExpressionValue(List<ConfigToken> tokens)
        {
            if (tokens.Count == 0)
                return "0";

            if (tokens[0].Type == ConfigTokenType.IntegerLiteral)
                return tokens[0].Value;

            if (tokens[0].Type == ConfigTokenType.Identifier)
            {
                // Check if defined
                if (_defines.TryGetValue(tokens[0].Value, out var val))
                    return val ?? "1"; // defined but empty = 1
                return "0"; // not defined
            }

            return tokens[0].Value;
        }

        private int HandleElse(List<ConfigToken> tokens, int idx, Stack<ConditionalState> stack)
        {
            if (stack.Count <= 1) return SkipLine(tokens, idx); // unbalanced #else

            var state = stack.Pop();
            // If we were emitting the matching if-block, now skip. If we were skipping, now emit.
            var parentState = stack.Peek();
            if (parentState != ConditionalState.Emit)
            {
                // Parent is skipping, so this else stays skipped
                stack.Push(ConditionalState.SkipDone);
            }
            else
            {
                // Only switch to emit if the previous block was actually being emitted
                if (state == ConditionalState.Emit)
                    stack.Push(ConditionalState.Skip);
                else if (state == ConditionalState.Skip)
                    stack.Push(ConditionalState.Emit);
                else
                    stack.Push(ConditionalState.SkipDone);
            }

            return SkipLine(tokens, idx);
        }

        private int HandleElif(List<ConfigToken> tokens, int idx, Stack<ConditionalState> stack)
        {
            // #elif is like #else + #if — handle conditionally
            if (stack.Count <= 1) return SkipLine(tokens, idx);

            var state = stack.Pop();
            var parentState = stack.Peek();

            if (parentState != ConditionalState.Emit)
            {
                stack.Push(ConditionalState.SkipDone);
                return SkipLine(tokens, idx);
            }

            // Only evaluate the #elif condition if we were skipping the previous block
            if (state == ConditionalState.Emit)
            {
                // Previous block was taken — skip the elif
                stack.Push(ConditionalState.Skip);
            }
            else if (state == ConditionalState.Skip)
            {
                // Previous block was NOT taken — check elif condition
                int i = idx + 1;
                var exprTokens = new List<ConfigToken>();
                while (i < tokens.Count && tokens[i].Type != ConfigTokenType.Eof)
                {
                    var t = tokens[i];
                    if (t.Type == ConfigTokenType.Identifier && t.Value.Length > 0 && t.Value[0] == '#')
                        break;
                    exprTokens.Add(t);
                    i++;
                }

                bool result = EvaluateIfExpression(exprTokens);
                stack.Push(result ? ConditionalState.Emit : ConditionalState.Skip);
            }
            else
            {
                stack.Push(ConditionalState.SkipDone);
            }

            return SkipLine(tokens, idx);
        }

        private int HandleEndif(List<ConfigToken> tokens, int idx, Stack<ConditionalState> stack)
        {
            if (stack.Count > 1)
                stack.Pop();
            return SkipLine(tokens, idx);
        }

        private int HandleError(List<ConfigToken> tokens, int idx)
        {
            var msgParts = new List<string>();
            int i = idx + 1;
            while (i < tokens.Count && tokens[i].Type != ConfigTokenType.Eof)
            {
                var t = tokens[i];
                if (t.Type == ConfigTokenType.Identifier && t.Value.Length > 0 && t.Value[0] == '#')
                    break;
                msgParts.Add(t.Value);
                i++;
            }

            Console.Error.WriteLine($"ConfigPreprocessor: #error: {string.Join(" ", msgParts)}");
            return SkipLine(tokens, idx);
        }

        private static int SkipLine(List<ConfigToken> tokens, int idx)
        {
            // Skip to Eof — since tokenizer strips whitespace, we just return the current position
            // The outer loop will advance i by 1
            return idx;
        }
    }
}
