#nullable enable
using System;
using System.Collections.Generic;

namespace BIS.SQF;

/// <summary>
/// Recursive-descent parser for Arma 3 SQF source.
/// Converts a token stream from SqfTokenizer into an AST (SqfAst.cs).
///
/// SQF is a postfix language — commands typically appear after their arguments.
/// This parser uses a greedy atomic-collection strategy with two-token lookahead
/// to disambiguate prefix-unary (cmd arg) from postfix-binary (arg1 arg2 cmd).
/// </summary>
public class SqfParser
{
    private readonly List<SqfToken> _tokens;
    private int _pos;

    public SqfParser(List<SqfToken> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    // ─── Helpers ───

    private SqfToken Peek(int offset = 0)
    {
        var idx = _pos + offset;
        return idx < _tokens.Count ? _tokens[idx] : _tokens[^1]; // last is always Eof
    }

    private SqfToken Consume()
    {
        if (_pos >= _tokens.Count)
            throw Error("Unexpected end of token stream");
        return _tokens[_pos++];
    }

    private SqfToken Expect(SqfTokenType type, string message)
    {
        var token = Peek();
        if (token.Type != type)
            throw Error(message);
        return Consume();
    }

    private FormatException Error(string message, SqfToken? token = null)
    {
        var t = token ?? Peek();
        return new FormatException($"{message} at {t}");
    }

    /// <summary>Identifier tokens that act as structural delimiters, not postfix commands.</summary>
    private static bool IsStructuralKeyword(string text) =>
        text is "then" or "else" or "do" or "from" or "to" or "step" or "catch";

    /// <summary>Token types that can start an atomic expression.</summary>
    private static bool CanStartAtom(SqfTokenType type) => type switch
    {
        SqfTokenType.Identifier => true,
        SqfTokenType.Number => true,
        SqfTokenType.String => true,
        SqfTokenType.LParen => true,
        SqfTokenType.LBracket => true,
        SqfTokenType.LBrace => true,
        SqfTokenType.Not => true,
        SqfTokenType.Tilde => true,
        _ => false,
    };

    /// <summary>Identifier tokens that are binary operator keywords (and, or, mod).</summary>
    private static bool IsBinaryOperatorKeyword(string text) =>
        text is "and" or "or" or "mod";

    /// <summary>
    /// Get SQF operator precedence (1 = lowest, 9 = highest). Returns 0 for non-operators.
    /// Precedence levels:
    ///   9: # (hash-select)
    ///   8: ^ (power)
    ///   7: * / % mod (multiplicative)
    ///   6: + - (additive)
    ///   4: infix commands (handled at expression level)
    ///   3: == != &lt; &gt; &lt;= &gt;= &gt;&gt; (comparison / config-path)
    ///   2: &amp;&amp; and (logical AND)
    ///   1: || or (logical OR)
    /// </summary>
    private static int GetPrecedence(SqfToken token)
    {
        // Word-form operator keywords are Identifier tokens
        if (token.Type == SqfTokenType.Identifier)
        {
            return token.Text switch
            {
                "or" => 1,
                "and" => 2,
                "mod" => 7,
                "select" => 5,
                "at" => 5,
                _ => 0,
            };
        }

        return token.Type switch
        {
            SqfTokenType.Or => 1,
            SqfTokenType.And => 2,
            SqfTokenType.Equal or SqfTokenType.NotEqual or
                SqfTokenType.Less or SqfTokenType.Greater or
                SqfTokenType.LessEqual or SqfTokenType.GreaterEqual => 3,
            SqfTokenType.Plus or SqfTokenType.Minus => 6,
            SqfTokenType.Star or SqfTokenType.Slash or SqfTokenType.Percent => 7,
            SqfTokenType.Caret => 8,
            SqfTokenType.Hash => 9,
            _ => 0,
        };
    }

    // ─── Top Level ───

    /// <summary>Parse a complete SQF file.</summary>
    public SqfFile ParseFile(string sourceText = "")
    {
        var statements = new List<SqfStatement>();
        while (true)
        {
            // Skip stray semicolons (empty statements at file level)
            while (Peek().Type is SqfTokenType.Semicolon or SqfTokenType.Preprocessor)
                Consume();
            if (Peek().Type == SqfTokenType.Eof)
                break;
            statements.Add(ParseStatement());
        }
        var file = statements.Count > 0 ? statements[0].File : "";
        return new SqfFile(statements, file, sourceText) { File = file };
    }

    // ─── Statements ───

    /// <summary>Parse a single statement (separated by semicolons).</summary>
    private SqfStatement ParseStatement()
    {
        // Skip stray semicolons and preprocessor directives
        while (Peek().Type is SqfTokenType.Semicolon or SqfTokenType.Preprocessor)
            Consume();

        if (Peek().Type == SqfTokenType.Eof)
            throw Error("Unexpected end of file in statement");

        var token = Peek();

        // Keyword statements
        if (token.Type == SqfTokenType.Identifier)
        {
            switch (token.Text)
            {
                case "if":     return ParseIf();
                case "while":  return ParseWhile();
                case "for":    return ParseFor();
                case "switch": return ParseSwitch();
                case "private": return ParsePrivate();
                case "params": return ParseParams();
                case "try":    return ParseTry();
                case "throw":  return ParseThrow();
                case "break":       return ParseBreak();
                case "continue":    return ParseContinue();
                case "breakWith":   return ParseBreakWith();
                case "continueWith": return ParseContinueWith();
                case "scopeName":   return ParseScopeName();
                case "case":    return ParseCase();
                case "default": return ParseDefault();
            }
        }

        // Expression statement (may be assignment at top level)
        var expr = ParseExpression();
        // Semicolons are optional before a closing brace (common in SQF code blocks)
        if (Peek().Type != SqfTokenType.RBrace)
            Expect(SqfTokenType.Semicolon, "Expected ';' after statement");
        return WithLoc(new SqfExpressionStatement(expr), token);
    }

    // ─── Expression Parsing ───

    /// <summary>
    /// Parse a full expression using Pratt parsing (precedence climbing).
    /// Handles postfix chains, optional assignment, and binary operators with
    /// proper SQF 11-level precedence.
    /// </summary>
    private SqfExpression ParseExpression(int minPrecedence = 0)
    {
        var token = Peek();
        var left = ParsePostfix();

        // Assignment: identifier = expr (not part of precedence climbing)
        if (Peek().Type == SqfTokenType.Assign)
        {
            Consume(); // skip =
            var right = ParseExpression();
            if (left is SqfIdentifier ident)
                return WithLoc(new SqfAssign(ident.Name, right), token);
            throw Error("Left side of assignment must be an identifier", token);
        }

        // Precedence climbing for binary operators
        while (true)
        {
            var next = Peek();

            // Config path >> (precedence 3, comparison level)
            if (next.Type == SqfTokenType.Greater && Peek(1).Type == SqfTokenType.Greater)
            {
                if (3 < minPrecedence) break;
                Consume(); Consume(); // skip >>
                var right = ParseAtomic();
                var parts = new List<SqfExpression>();
                if (left is SqfConfigPath existingPath)
                    parts.AddRange(existingPath.Parts);
                else
                    parts.Add(left);
                parts.Add(right);
                left = WithLoc(new SqfConfigPath(parts), token);
                continue;
            }

            int prec = GetPrecedence(next);
            if (prec == 0 || prec < minPrecedence)
                break;

            Consume();
            var rhs = ParseExpression(prec + 1);
            left = WithLoc(new SqfBinaryOp(next.Text, left, rhs), token);
        }

        return left;
    }

    /// <summary>
    /// Parse a postfix expression chain: one or more atoms optionally terminated
    /// by a command identifier.
    ///
    /// SQF has both prefix-unary (cmd arg) and postfix-binary (arg1 arg2 cmd).
    /// We use two-token lookahead to disambiguate two-atom vs three-atom patterns,
    /// and fall back to prefix-unary for cmd + non-identifier-arg (e.g. hint "ok").
    /// </summary>
    private SqfExpression ParsePostfix()
    {
        var token = Peek();
        var first = ParseAtomic();

        var next = Peek();

        // Delimiter — expression is just the first atom (e.g. player;)
        if (IsExpressionDelimiter(next))
            return first;

        // Identifier after first atom — could be 2-atom (cmd arg) or 3+-atom (arg1 cmd arg2).
        // Operator keywords (and, or, mod) are NOT postfix commands; they are binary operators
        // handled by ParseExpression's precedence climbing.
        if (next.Type == SqfTokenType.Identifier && !IsStructuralKeyword(next.Text)
            && !IsBinaryOperatorKeyword(next.Text))
        {
            var cmdToken = Consume();
            var after = Peek();

            if (IsExpressionDelimiter(after))
            {
                // 2-atom: if first is an identifier, it is the command (prefix unary).
                // If first is non-identifier, the just-consumed identifier is the command (postfix unary).
                if (first is SqfIdentifier firstId)
                {
                    return WithLoc(new SqfCall(firstId, new List<SqfExpression>
                    {
                        WithLoc(new SqfIdentifier(cmdToken.Text), cmdToken)
                    }), token);
                }
                else
                {
                    return WithLoc(new SqfCall(
                        WithLoc(new SqfIdentifier(cmdToken.Text), cmdToken),
                        new List<SqfExpression> { first }), token);
                }
            }

            // 3+ atoms: the consumed identifier IS the command, first is arg1.
            var args = new List<SqfExpression> { first };
            while (CanStartAtom(Peek().Type) && !IsRestrictedAtomStart(Peek()))
            {
                args.Add(ParseAtomic());
            }

            return WithLoc(new SqfCall(
                WithLoc(new SqfIdentifier(cmdToken.Text), cmdToken),
                args), token);
        }

        // Non-identifier atom after first — prefix command with non-id argument.
        // e.g. hint "ok", typeName 42, str _x
        if (CanStartAtom(next.Type))
        {
            var arg = ParseAtomic();
            if (first is SqfIdentifier firstId)
            {
                return WithLoc(new SqfCall(firstId, new List<SqfExpression> { arg }), token);
            }
        }

        // Cannot combine — let ParseExpression handle binary operators etc.
        return first;
    }

    /// <summary>
    /// Parse a single atomic expression: literal, identifier, parenthesized,
    /// array, code block, unary operator, or config-path starter.
    /// </summary>
    private SqfExpression ParseAtomic()
    {
        var token = Peek();

        switch (token.Type)
        {
            case SqfTokenType.Number:
                Consume();
                return WithLoc(new SqfNumberLiteral(token.Text), token);

            case SqfTokenType.String:
                Consume();
                return WithLoc(new SqfStringLiteral(token.Text), token);

            case SqfTokenType.Identifier:
                Consume();
                if (token.Text == "true")
                    return WithLoc(new SqfBooleanLiteral(true), token);
                if (token.Text == "false")
                    return WithLoc(new SqfBooleanLiteral(false), token);
                if (token.Text == "if")
                    return ParseIfExpression(token);
                return WithLoc(new SqfIdentifier(token.Text), token);

            case SqfTokenType.LParen:
                Consume(); // (
                var inner = ParseExpression();
                Expect(SqfTokenType.RParen, "Expected ')'");
                return WithLoc(new SqfParenExpr(inner), token);

            case SqfTokenType.LBracket:
                return ParseArray();

            case SqfTokenType.LBrace:
                return ParseCodeBlock();

            // Unary prefix operators
            case SqfTokenType.Not:
            case SqfTokenType.Tilde:
            {
                Consume();
                var opText = token.Text;
                var operand = ParseAtomic();
                return WithLoc(new SqfUnaryOp(opText, operand), token);
            }

            case SqfTokenType.Minus:
            {
                Consume();
                var operand = ParseAtomic();
                return WithLoc(new SqfUnaryOp("-", operand), token);
            }

            case SqfTokenType.Plus:
            {
                Consume();
                var operand = ParseAtomic();
                return WithLoc(new SqfUnaryOp("+", operand), token);
            }

            // Config path starting with >>
            case SqfTokenType.Greater:
                if (Peek(1).Type == SqfTokenType.Greater)
                {
                    Consume(); Consume(); // skip >>
                    var parts = new List<SqfExpression> { ParseAtomic() };
                    while (Peek().Type == SqfTokenType.Greater && Peek(1).Type == SqfTokenType.Greater)
                    {
                        Consume(); Consume();
                        parts.Add(ParseAtomic());
                    }
                    return WithLoc(new SqfConfigPath(parts), token);
                }
                throw Error($"Unexpected token '{token.Text}'", token);

            default:
                throw Error($"Unexpected token '{token.Text}' in expression", token);
        }
    }

    // ─── Compound Atoms ───

    private SqfArrayLiteral ParseArray()
    {
        var token = Consume(); // [
        var elements = new List<SqfExpression>();

        if (Peek().Type != SqfTokenType.RBracket)
        {
            elements.Add(ParseExpression());
            while (Peek().Type == SqfTokenType.Comma)
            {
                Consume(); // ,
                elements.Add(ParseExpression());
            }
        }

        Expect(SqfTokenType.RBracket, "Expected ']'");
        return WithLoc(new SqfArrayLiteral(elements), token);
    }

    private SqfCodeBlock ParseCodeBlock()
    {
        var token = Consume(); // {
        var statements = new List<SqfStatement>();

        while (Peek().Type != SqfTokenType.RBrace && Peek().Type != SqfTokenType.Eof)
        {
            statements.Add(ParseStatement());
        }

        Expect(SqfTokenType.RBrace, "Expected '}'");
        return WithLoc(new SqfCodeBlock(statements), token);
    }

    // ─── Compound Statements ───

    private SqfIfStatement ParseIf()
    {
        var token = Consume(); // if
        SqfExpression condition;
        if (Peek().Type == SqfTokenType.LParen)
        {
            Consume(); // (
            condition = ParseExpression();
            Expect(SqfTokenType.RParen, "Expected ')' after if-condition");
        }
        else
        {
            condition = ParseExpression();
        }

        // then { ... } or command body
        SqfCodeBlock thenBlock;
        SqfCodeBlock? elseBlock = null;

        var next = Peek();
        if (next.Type == SqfTokenType.Identifier && next.Text == "then")
        {
            Consume();
            thenBlock = ParseCodeBlock();

            // Optional else { ... }
            if (Peek().Type == SqfTokenType.Identifier && Peek().Text == "else")
            {
                Consume(); // else
                elseBlock = ParseCodeBlock();
            }
        }
        else
        {
            // No "then" — parse as command body: if (cond) exitWith { ... };
            thenBlock = new SqfCodeBlock(new List<SqfStatement>
            {
                new SqfExpressionStatement(ParseExpression())
            });
        }

        // if statements end with semicolon
        if (Peek().Type == SqfTokenType.Semicolon)
            Consume();

        return WithLoc(new SqfIfStatement(condition, thenBlock, elseBlock), token);
    }

    /// <summary>Parse an inline if-expression. Called when 'if' is inside ParseAtomic.</summary>
    private SqfExpression ParseIfExpression(SqfToken token)
    {
        SqfExpression condition;
        if (Peek().Type == SqfTokenType.LParen)
        {
            Consume(); // (
            condition = ParseExpression();
            Expect(SqfTokenType.RParen, "Expected ')' after if-condition");
        }
        else
        {
            condition = ParseExpression();
        }

        var thenExpr = Peek();
        if (thenExpr.Type != SqfTokenType.Identifier || thenExpr.Text != "then")
            throw Error("Expected 'then' after if-condition", thenExpr);
        Consume();
        var thenBlock = ParseCodeBlock();

        SqfExpression? elseBlock = null;
        var next = Peek();
        if (next.Type == SqfTokenType.Identifier && next.Text == "else")
        {
            Consume();
            elseBlock = ParseCodeBlock();
        }

        return WithLoc(new SqfIfExpression(condition, thenBlock, elseBlock), token);
    }

    private SqfWhileStatement ParseWhile()
    {
        var token = Consume(); // while
        var condition = ParseCodeBlock(); // while uses {code} for condition

        var doToken = Peek();
        if (doToken.Type == SqfTokenType.Identifier && doToken.Text == "do")
            Consume();
        else
            throw Error("Expected 'do' after while-condition", doToken);

        var body = ParseCodeBlock();

        if (Peek().Type == SqfTokenType.Semicolon)
            Consume();

        return WithLoc(new SqfWhileStatement(condition, body), token);
    }

    private SqfForStatement ParseFor()
    {
        var token = Consume(); // for

        // SQF alternative syntax: for "_varName" from startExpr to endExpr [step stepExpr] do {body}
        if (Peek().Type == SqfTokenType.String)
        {
            var varToken = Consume();
            var varName = varToken.Text;

            if (Peek().Type == SqfTokenType.Identifier && Peek().Text == "from")
                Consume();
            else
                throw Error("Expected 'from' after for-variable", Peek());

            var startExpr = ParseExpression();

            if (Peek().Type == SqfTokenType.Identifier && Peek().Text == "to")
                Consume();
            else
                throw Error("Expected 'to' after for-start", Peek());

            var endExpr = ParseExpression();

            SqfExpression? stepExpr = null;
            if (Peek().Type == SqfTokenType.Identifier && Peek().Text == "step")
            {
                Consume();
                stepExpr = ParseExpression();
            }

            var doToken = Peek();
            if (doToken.Type == SqfTokenType.Identifier && doToken.Text == "do")
                Consume();
            else
                throw Error("Expected 'do' after for-spec", doToken);

            var body = ParseCodeBlock();

            if (Peek().Type == SqfTokenType.Semicolon)
                Consume();

            // Synthesize init/cond/step code blocks to match SqfForStatement shape
            var varId = WithLoc(new SqfIdentifier(varName), varToken);
            var stepVal = stepExpr ?? WithLoc(new SqfNumberLiteral("1"), varToken);
            var stepAssign = WithLoc(new SqfAssign(varName, WithLoc(new SqfBinaryOp("+", varId, stepVal), varToken)), varToken);
            var stepBlock = new SqfCodeBlock(new List<SqfStatement>
            {
                new SqfExpressionStatement(stepAssign) { File = varToken.File, Line = varToken.Line, Column = varToken.Column }
            }) { File = varToken.File, Line = varToken.Line, Column = varToken.Column };

            var initAssign = WithLoc(new SqfAssign(varName, startExpr), varToken);
            var initBlock = new SqfCodeBlock(new List<SqfStatement>
            {
                new SqfExpressionStatement(initAssign) { File = varToken.File, Line = varToken.Line, Column = varToken.Column }
            }) { File = varToken.File, Line = varToken.Line, Column = varToken.Column };

            var condExpr = WithLoc(new SqfBinaryOp("<=", varId, endExpr), varToken);
            var condBlock = new SqfCodeBlock(new List<SqfStatement>
            {
                new SqfExpressionStatement(condExpr) { File = varToken.File, Line = varToken.Line, Column = varToken.Column }
            }) { File = varToken.File, Line = varToken.Line, Column = varToken.Column };

            return WithLoc(new SqfForStatement(initBlock, condBlock, stepBlock, body), token);
        }

        // Standard C-style for: for [{init}, {cond}, {step}] do {body}
        Expect(SqfTokenType.LBracket, "Expected '[' after 'for'");
        var init = ParseForClause();
        Expect(SqfTokenType.Comma, "Expected ',' after for-init");
        var cond = ParseForClause();
        Expect(SqfTokenType.Comma, "Expected ',' after for-condition");
        var step = ParseForClause();
        Expect(SqfTokenType.RBracket, "Expected ']' after for-step");

        var do2 = Peek();
        if (do2.Type == SqfTokenType.Identifier && do2.Text == "do")
            Consume();
        else
            throw Error("Expected 'do' after for-spec", do2);

        var body2 = ParseCodeBlock();

        if (Peek().Type == SqfTokenType.Semicolon)
            Consume();

        return WithLoc(new SqfForStatement(init, cond, step, body2), token);
    }

    /// <summary>Parse one clause of a for-spec array. Can be a code block or bare expression.</summary>
    private SqfCodeBlock ParseForClause()
    {
        if (Peek().Type == SqfTokenType.LBrace)
            return ParseCodeBlock();
        // Bare expression: wrap in a code block for AST consistency
        var expr = ParseExpression();
        return new SqfCodeBlock(new List<SqfStatement>
        {
            new SqfExpressionStatement(expr) { File = expr.File, Line = expr.Line, Column = expr.Column }
        }) { File = expr.File, Line = expr.Line, Column = expr.Column };
    }

    private SqfSwitchStatement ParseSwitch()
    {
        var token = Consume(); // switch
        SqfExpression value;
        if (Peek().Type == SqfTokenType.LParen)
        {
            Consume(); // (
            value = ParseExpression();
            Expect(SqfTokenType.RParen, "Expected ')' after switch-value");
        }
        else
        {
            value = ParseExpression();
        }

        var doToken = Peek();
        if (doToken.Type == SqfTokenType.Identifier && doToken.Text == "do")
            Consume();
        else
            throw Error("Expected 'do' after switch-value", doToken);

        var body = ParseCodeBlock();

        if (Peek().Type == SqfTokenType.Semicolon)
            Consume();

        return WithLoc(new SqfSwitchStatement(value, body), token);
    }

    private SqfPrivateStatement ParsePrivate()
    {
        var token = Consume(); // private
        var variables = new List<SqfExpression>();

        if (Peek().Type == SqfTokenType.LBracket)
        {
            // private ["_a", "_b"]: array of string variable names
            var arr = ParseArray();
            variables.AddRange(arr.Elements);
        }
        else if (Peek().Type == SqfTokenType.String)
        {
            // private "_x": single string variable name
            var strToken = Consume();
            variables.Add(WithLoc(new SqfStringLiteral(strToken.Text), strToken));
        }
        else if (Peek().Type == SqfTokenType.Identifier)
        {
            // private _x  or  private _x = expr
            var nameToken = Consume();
            if (Peek().Type == SqfTokenType.Assign)
            {
                Consume(); // =
                var value = ParseExpression();
                variables.Add(WithLoc(new SqfAssign(nameToken.Text, value), nameToken));
            }
            else
            {
                variables.Add(WithLoc(new SqfIdentifier(nameToken.Text), nameToken));
            }

            // Multiple variables with commas
            while (Peek().Type == SqfTokenType.Comma)
            {
                Consume(); // ,
                var nextToken = Expect(SqfTokenType.Identifier, "Expected variable name after ','");
                if (Peek().Type == SqfTokenType.Assign)
                {
                    Consume();
                    var val = ParseExpression();
                    variables.Add(WithLoc(new SqfAssign(nextToken.Text, val), nextToken));
                }
                else
                {
                    variables.Add(WithLoc(new SqfIdentifier(nextToken.Text), nextToken));
                }
            }
        }
        else
        {
            throw Error("Expected variable name or array after 'private'");
        }

        Expect(SqfTokenType.Semicolon, "Expected ';' after private declaration");
        return WithLoc(new SqfPrivateStatement(variables), token);
    }

    private SqfParamsStatement ParseParams()
    {
        var token = Consume(); // params
        var parameters = new List<SqfExpression>();

        if (Peek().Type == SqfTokenType.LBracket)
        {
            // params ["_a", "_b"] or params [["_a", defaultValue], ...]
            var arr = ParseArray();
            parameters.AddRange(arr.Elements);
        }
        else
        {
            throw Error("Expected '[' after 'params'");
        }

        Expect(SqfTokenType.Semicolon, "Expected ';' after params declaration");
        return WithLoc(new SqfParamsStatement(parameters), token);
    }

    private SqfTryCatchStatement ParseTry()
    {
        var token = Consume(); // try
        var tryBlock = ParseCodeBlock();

        if (Peek().Type == SqfTokenType.Identifier && Peek().Text == "catch")
            Consume();
        else
            throw Error("Expected 'catch' after try block", Peek());

        var catchBlock = ParseCodeBlock();

        if (Peek().Type == SqfTokenType.Semicolon)
            Consume();

        return WithLoc(new SqfTryCatchStatement(tryBlock, catchBlock), token);
    }

    private SqfThrowStatement ParseThrow()
    {
        var token = Consume(); // throw
        var value = ParseExpression();

        if (Peek().Type != SqfTokenType.RBrace)
            Expect(SqfTokenType.Semicolon, "Expected ';' after throw");

        return WithLoc(new SqfThrowStatement(value), token);
    }

    private SqfBreakStatement ParseBreak()
    {
        var token = Consume(); // break

        if (Peek().Type != SqfTokenType.RBrace)
            Expect(SqfTokenType.Semicolon, "Expected ';' after break");

        return WithLoc(new SqfBreakStatement(), token);
    }

    private SqfContinueStatement ParseContinue()
    {
        var token = Consume(); // continue

        if (Peek().Type != SqfTokenType.RBrace)
            Expect(SqfTokenType.Semicolon, "Expected ';' after continue");

        return WithLoc(new SqfContinueStatement(), token);
    }

    private SqfBreakWithStatement ParseBreakWith()
    {
        var token = Consume(); // breakWith
        var value = ParseExpression();

        if (Peek().Type != SqfTokenType.RBrace)
            Expect(SqfTokenType.Semicolon, "Expected ';' after breakWith");

        return WithLoc(new SqfBreakWithStatement(value), token);
    }

    private SqfContinueWithStatement ParseContinueWith()
    {
        var token = Consume(); // continueWith
        var value = ParseExpression();

        if (Peek().Type != SqfTokenType.RBrace)
            Expect(SqfTokenType.Semicolon, "Expected ';' after continueWith");

        return WithLoc(new SqfContinueWithStatement(value), token);
    }

    private SqfScopeNameStatement ParseScopeName()
    {
        var token = Consume(); // scopeName
        var nameToken = Expect(SqfTokenType.String, "Expected string after scopeName");

        if (Peek().Type != SqfTokenType.RBrace)
            Expect(SqfTokenType.Semicolon, "Expected ';' after scopeName");

        return WithLoc(new SqfScopeNameStatement(nameToken.Text), token);
    }

    private SqfCaseStatement ParseCase()
    {
        var token = Consume(); // case
        var value = ParseExpression();

        SqfCodeBlock? body = null;
        if (Peek().Type == SqfTokenType.Colon)
        {
            Consume(); // :
            if (Peek().Type == SqfTokenType.LBrace)
                body = ParseCodeBlock();
        }
        // else: fallthrough — no colon, no body; just 'case value;'

        if (Peek().Type != SqfTokenType.RBrace)
            Expect(SqfTokenType.Semicolon, "Expected ';' after case");

        return WithLoc(new SqfCaseStatement(value, body), token);
    }

    private SqfCaseStatement ParseDefault()
    {
        var token = Consume(); // default

        SqfCodeBlock? body = null;
        if (Peek().Type == SqfTokenType.LBrace)
            body = ParseCodeBlock();

        if (Peek().Type != SqfTokenType.RBrace)
            Expect(SqfTokenType.Semicolon, "Expected ';' after default");

        return WithLoc(new SqfCaseStatement(null, body), token);
    }

    // ─── Helpers ───

    /// <summary>Check if a token acts as an expression delimiter.</summary>
    private static bool IsExpressionDelimiter(SqfToken t) => t.Type switch
    {
        SqfTokenType.Semicolon => true,
        SqfTokenType.RParen => true,
        SqfTokenType.RBracket => true,
        SqfTokenType.RBrace => true,
        SqfTokenType.Comma => true,
        SqfTokenType.Colon => true,
        SqfTokenType.Eof => true,
        _ => t.Type == SqfTokenType.Identifier && IsStructuralKeyword(t.Text),
    };

    /// <summary>
    /// Some identifiers should not be treated as atom-starts in postfix chains
    /// because they are part of compound statement syntax.
    /// </summary>
    private static bool IsRestrictedAtomStart(SqfToken t) =>
        t.Type == SqfTokenType.Identifier && IsStructuralKeyword(t.Text);

    /// <summary>Attach source location to an AST node from a token.</summary>
    private static T WithLoc<T>(T node, SqfToken token) where T : SqfAstNode =>
        node with { File = token.File, Line = token.Line, Column = token.Column };
}
