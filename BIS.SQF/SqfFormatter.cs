#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace BIS.SQF;

/// <summary>Brace placement style for SQF code blocks.</summary>
public enum BraceStyle
{
    /// <summary>Opening brace on same line as preceding keyword (K&amp;R).</summary>
    KAndR,
    /// <summary>Opening brace on its own line (Allman).</summary>
    Allman,
}

/// <summary>Configuration options for SQF source formatting.</summary>
public class SqfFormatterOptions
{
    /// <summary>Number of spaces per indent level. Default: 4.</summary>
    public int IndentSize { get; set; } = 4;

    /// <summary>Use tab characters instead of spaces for indentation. Default: false.</summary>
    public bool UseTabs { get; set; } = false;

    /// <summary>Brace placement style. Default: KAndR.</summary>
    public BraceStyle Style { get; set; } = BraceStyle.KAndR;
}

/// <summary>
/// Formatter for SQF (Arma 3 scripting) source code.
/// Takes source text and returns consistently formatted source text.
/// </summary>
public class SqfFormatter
{
    private readonly SqfFormatterOptions _options;

    public SqfFormatter(SqfFormatterOptions? options = null)
    {
        _options = options ?? new SqfFormatterOptions();
    }

    /// <summary>Format the given SQF source code and return the formatted result.</summary>
    public string Format(string source)
    {
        if (string.IsNullOrEmpty(source))
            return "\n";

        // 1. Build line-start offset table for position conversion
        var lineStarts = BuildLineStarts(source);

        // 2. Extract comments (with string-awareness)
        var comments = ExtractComments(source);

        // 3. Tokenize
        var tokens = new SqfTokenizer(source).Tokenize();
        // Remove trailing Eof
        if (tokens.Count > 0 && tokens[^1].Type == SqfTokenType.Eof)
            tokens.RemoveAt(tokens.Count - 1);

        // 4. Merge tokens and comments into a single ordered stream
        var items = MergeStream(source, lineStarts, tokens, comments);

        // 5. Walk the merged stream and emit formatted text
        var sb = new StringBuilder();
        int indentLevel = 0;
        SqfTokenType? lastType = null;
        bool needsNewline = false;    // pending newline+indent before next token
        bool lineIsEmpty = true;      // current output line has no content yet

        for (int idx = 0; idx < items.Count; idx++)
        {
            var item = items[idx];

            if (item.IsComment)
            {
                // Determine if comment is inline (same line as previous token) or standalone
                bool isInline = IsInlineComment(source, item, idx, items);

                if (isInline)
                {
                    sb.Append(' ');
                    sb.Append(item.Text);
                    // Line comments end the line; block comments on inline do not
                    if (item.Text.StartsWith("//", StringComparison.Ordinal))
                    {
                        sb.Append('\n');
                        lineIsEmpty = true;
                        needsNewline = true;
                        lastType = null;
                    }
                    else
                    {
                        lineIsEmpty = false;
                    }
                }
                else
                {
                    // Standalone comment: emit on its own line
                    if (!lineIsEmpty)
                    {
                        sb.Append('\n');
                    }
                    sb.Append(GetIndent(indentLevel));
                    sb.Append(item.Text);
                    sb.Append('\n');
                    lineIsEmpty = true;
                    needsNewline = false;
                    lastType = null;
                }
                continue;
            }

            // ── Token processing ──
            var tok = item.Token!.Value;

            // Preprocessor directives: emit verbatim on own line, no indentation
            if (tok.Type == SqfTokenType.Preprocessor)
            {
                if (!lineIsEmpty)
                    sb.Append('\n');
                sb.Append(tok.Text);
                sb.Append('\n');
                lineIsEmpty = true;
                needsNewline = false;
                lastType = null;
                continue;
            }

            // ── Empty braces: { immediately followed by } ──
            if (tok.Type == SqfTokenType.LBrace && IsNextTokenRBrace(items, idx))
            {
                // Space before { if needed (K&R)
                if (lastType != null && !lineIsEmpty && NeedsSpaceBetween(lastType.Value, SqfTokenType.LBrace))
                    sb.Append(' ');
                sb.Append("{}");
                lineIsEmpty = false;
                lastType = SqfTokenType.RBrace;
                // Skip the } that follows
                idx++;
                continue;
            }

            // ── Closing brace: decrement indent, emit at start of line ──
            if (tok.Type == SqfTokenType.RBrace)
            {
                indentLevel = Math.Max(0, indentLevel - 1);
                if (!lineIsEmpty)
                    sb.Append('\n');
                sb.Append(GetIndent(indentLevel));
                sb.Append('}');
                lineIsEmpty = false;
                needsNewline = false;
                lastType = SqfTokenType.RBrace;
                continue;
            }

            // ── Allman-style opening brace: newline before { ──
            if (tok.Type == SqfTokenType.LBrace && _options.Style == BraceStyle.Allman)
            {
                if (!lineIsEmpty)
                    sb.Append('\n');
                sb.Append(GetIndent(indentLevel));
                sb.Append('{');
                indentLevel++;
                sb.Append('\n');
                lineIsEmpty = true;
                needsNewline = true;
                lastType = SqfTokenType.LBrace;
                continue;
            }

            // ── Emit pending newline + indent ──
            bool justIndented = false;
            if (needsNewline)
            {
                if (!lineIsEmpty)
                    sb.Append('\n');
                sb.Append(GetIndent(indentLevel));
                lineIsEmpty = false;
                needsNewline = false;
                justIndented = true;
            }

            // ── Space before token ──
            if (lastType != null && !lineIsEmpty && !justIndented)
            {
                if (NeedsSpaceBetween(lastType.Value, tok.Type))
                {
                    sb.Append(' ');
                }
            }

            // ── Emit token text ──
            if (tok.Type == SqfTokenType.String)
            {
                // Tokenizer strips quotes from strings; reconstruct from source position
                char quote = source[item.Offset];
                sb.Append(quote);
                sb.Append(tok.Text);
                sb.Append(quote);
            }
            else
            {
                sb.Append(tok.Text);
            }
            lineIsEmpty = false;

            // ── Post-token actions ──
            if (tok.Type == SqfTokenType.LBrace)
            {
                // K&R: { already on same line, now increase indent and newline
                indentLevel++;
                sb.Append('\n');
                lineIsEmpty = true;
                needsNewline = true;
            }
            else if (tok.Type == SqfTokenType.Semicolon)
            {
                sb.Append('\n');
                lineIsEmpty = true;
                needsNewline = true;
            }
            // else: no automatic newline; next token decides

            lastType = tok.Type;
        }

        // ── Post-processing ──
        var result = sb.ToString();

        // Trim trailing whitespace from each line
        var lines = result.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd();

        // Collapse multiple consecutive blank lines to a single blank line
        var collapsed = new List<string>(lines.Length);
        bool prevBlank = false;
        for (int i = 0; i < lines.Length; i++)
        {
            bool isBlank = lines[i].Length == 0;
            if (isBlank && prevBlank)
                continue;
            collapsed.Add(lines[i]);
            prevBlank = isBlank;
        }

        // Trim trailing empty lines from the end
        while (collapsed.Count > 0 && collapsed[^1].Length == 0)
            collapsed.RemoveAt(collapsed.Count - 1);

        // Ensure exactly one trailing newline
        result = string.Join('\n', collapsed);
        if (!result.EndsWith('\n'))
            result += '\n';

        return result;
    }

    // ─── Comment extraction ──────────────────────────────────────────

    /// <summary>Stores a comment found in source with its offset and text.</summary>
    private readonly record struct CommentInfo(int Offset, string Text);

    /// <summary>Extract all line and block comments from source, avoiding string literals.</summary>
    private static List<CommentInfo> ExtractComments(string source)
    {
        var comments = new List<CommentInfo>();
        int i = 0;

        while (i < source.Length)
        {
            char c = source[i];

            // String literals — skip entirely
            if (c == '"' || c == '\'')
            {
                char quote = c;
                i++;
                while (i < source.Length)
                {
                    if (source[i] == quote)
                    {
                        // Doubled quote = escaped quote
                        if (i + 1 < source.Length && source[i + 1] == quote)
                        {
                            i += 2;
                            continue;
                        }
                        i++; // closing quote
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Line comment
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                int start = i;
                i += 2;
                while (i < source.Length && source[i] != '\n')
                    i++;
                comments.Add(new CommentInfo(start, source[start..i]));
                continue;
            }

            // Block comment
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                    i++;
                if (i + 1 < source.Length)
                    i += 2; // skip */
                else
                    i = source.Length; // unterminated: consume to end
                comments.Add(new CommentInfo(start, source[start..i]));
                continue;
            }

            i++;
        }

        return comments;
    }

    // ─── Stream merging ───────────────────────────────────────────────

    /// <summary>An item in the merged token+comment stream.</summary>
    private readonly record struct StreamItem(
        int Offset,
        int EndOffset,
        string Text,
        bool IsComment,
        SqfToken? Token
    );

    /// <summary>Merge tokens and comments into a single stream sorted by source position.</summary>
    private static List<StreamItem> MergeStream(
        string source,
        int[] lineStarts,
        List<SqfToken> tokens,
        List<CommentInfo> comments)
    {
        var items = new List<StreamItem>(tokens.Count + comments.Count);

        foreach (var token in tokens)
        {
            int offset = LineColToOffset(lineStarts, token.Line, token.Column);
            items.Add(new StreamItem(offset, offset + token.Text.Length, token.Text, false, token));
        }

        foreach (var comment in comments)
        {
            items.Add(new StreamItem(comment.Offset, comment.Offset + comment.Text.Length, comment.Text, true, null));
        }

        // Sort by offset; if same offset, comments come after tokens
        items.Sort((a, b) =>
        {
            int cmp = a.Offset.CompareTo(b.Offset);
            if (cmp != 0)
                return cmp;
            // At same offset: token before comment
            if (a.IsComment != b.IsComment)
                return a.IsComment ? 1 : -1;
            return 0;
        });

        return items;
    }

    // ─── Inline comment detection ─────────────────────────────────────

    /// <summary>
    /// Determine if a comment is inline (on the same line as the previous token)
    /// by checking if there is a newline between them in the original source.
    /// </summary>
    private static bool IsInlineComment(string source, StreamItem commentItem, int idx, List<StreamItem> items)
    {
        // Find the previous non-comment item
        int prevEnd = 0;
        bool foundPrev = false;
        for (int j = idx - 1; j >= 0; j--)
        {
            if (!items[j].IsComment)
            {
                prevEnd = items[j].EndOffset;
                foundPrev = true;
                break;
            }
        }

        if (!foundPrev)
            return false; // standalone at start of file

        // Check the source text between previous token end and comment start
        int commentStart = commentItem.Offset;
        if (commentStart <= prevEnd)
            return false;

        for (int i = prevEnd; i < commentStart; i++)
        {
            if (source[i] == '\n')
                return false; // newline found → standalone
        }

        return true; // no newline → inline
    }

    // ─── Look-ahead helpers ───────────────────────────────────────────

    /// <summary>Check if the next non-comment token after current index is RBrace (for empty brace detection).</summary>
    private static bool IsNextTokenRBrace(List<StreamItem> items, int idx)
    {
        for (int j = idx + 1; j < items.Count; j++)
        {
            if (items[j].IsComment)
                continue;
            return items[j].Token!.Value.Type == SqfTokenType.RBrace;
        }
        return false;
    }

    // ─── Line-start helpers ───────────────────────────────────────────

    private static int[] BuildLineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
                starts.Add(i + 1);
        }
        return starts.ToArray();
    }

    private static int LineColToOffset(int[] lineStarts, int line, int column)
    {
        if (line < 1)
            return 0;
        int idx = line - 1;
        if (idx >= lineStarts.Length)
            return lineStarts[^1];
        return lineStarts[idx] + (column - 1);
    }

    // ─── Spacing rules ────────────────────────────────────────────────

    /// <summary>Token types that are binary operators (get a space on both sides).</summary>
    private static bool IsBinaryOperator(SqfTokenType type) => type switch
    {
        SqfTokenType.Plus or SqfTokenType.Minus or SqfTokenType.Star or
        SqfTokenType.Slash or SqfTokenType.Percent or SqfTokenType.Caret or
        SqfTokenType.Equal or SqfTokenType.NotEqual or
        SqfTokenType.Less or SqfTokenType.Greater or
        SqfTokenType.LessEqual or SqfTokenType.GreaterEqual or
        SqfTokenType.And or SqfTokenType.Or or SqfTokenType.Assign or
        SqfTokenType.Colon or SqfTokenType.Question => true,
        _ => false,
    };

    /// <summary>
    /// Determine whether a space should be inserted between two adjacent tokens.
    /// </summary>
    private static bool NeedsSpaceBetween(SqfTokenType prev, SqfTokenType curr)
    {
        // No space before these tokens
        if (curr is SqfTokenType.Semicolon or SqfTokenType.Comma or
                     SqfTokenType.RParen or SqfTokenType.RBracket or
                     SqfTokenType.Dot)
            return false;

        // No space after these tokens
        if (prev is SqfTokenType.LParen or SqfTokenType.LBracket or
                     SqfTokenType.Not or SqfTokenType.Dot)
            return false;

        // Binary operators: space on both sides
        if (IsBinaryOperator(prev) || IsBinaryOperator(curr))
            return true;

        // Opening brace gets a space before it (K&R: "keyword {")
        if (curr is SqfTokenType.LBrace)
            return true;

        // Default: space between most tokens
        return true;
    }

    // ─── Indent string ────────────────────────────────────────────────

    private string GetIndent(int level)
    {
        if (level <= 0)
            return "";
        if (_options.UseTabs)
            return new string('\t', level);
        return new string(' ', level * _options.IndentSize);
    }
}
