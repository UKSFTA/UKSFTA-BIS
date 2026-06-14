#nullable enable
using System.Collections.Generic;

namespace BIS.SQF;

/// <summary>Base type for all SQF AST nodes.</summary>
public abstract record SqfAstNode
{
    public string File { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
}

/// <summary>An SQF expression (produces a value).</summary>
public abstract record SqfExpression : SqfAstNode;

/// <summary>An SQF statement (a complete unit of execution).</summary>
public abstract record SqfStatement : SqfAstNode;

// ─── Literal Expressions ───

/// <summary>String literal: "hello" or 'world'.</summary>
public sealed record SqfStringLiteral(string Value) : SqfExpression;

/// <summary>Number literal: 42, 3.14, 0xFF, 1e10.</summary>
public sealed record SqfNumberLiteral(string Text) : SqfExpression;

/// <summary>Boolean literal: true / false.</summary>
public sealed record SqfBooleanLiteral(bool Value) : SqfExpression;

/// <summary>Identifier expression: player, _myVar, myFunction.</summary>
public sealed record SqfIdentifier(string Name) : SqfExpression;

// ─── Complex Expressions ───

/// <summary>String literal expression: "text" or 'text'.</summary>
public sealed record SqfStringExpr(string Value) : SqfExpression;

/// <summary>Array literal: [expr, expr, ...].</summary>
public sealed record SqfArrayLiteral(List<SqfExpression> Elements) : SqfExpression;

/// <summary>Code block expression: { statements }.</summary>
public sealed record SqfCodeBlock(List<SqfStatement> Statements) : SqfExpression;

/// <summary>Parenthesized expression: (expr).</summary>
public sealed record SqfParenExpr(SqfExpression Inner) : SqfExpression;

/// <summary>Config path expression: >> "path" or >> class.</summary>
public sealed record SqfConfigPath(List<SqfExpression> Parts) : SqfExpression;

// ─── Operator Expressions ───

/// <summary>Unary operation: !expr, -expr.</summary>
public sealed record SqfUnaryOp(string Operator, SqfExpression Operand) : SqfExpression;

/// <summary>Binary operation: lhs OP rhs (e.g. a + b, a > b).</summary>
public sealed record SqfBinaryOp(string Operator, SqfExpression Left, SqfExpression Right) : SqfExpression;

/// <summary>Assignment: var = expr (also +=, -= etc. as general assignment).</summary>
public sealed record SqfAssign(string Name, SqfExpression Value) : SqfExpression;

/// <summary>Array access: arr[index].</summary>
public sealed record SqfArrayAccess(SqfExpression Array, SqfExpression Index) : SqfExpression;

/// <summary>Function call: fnc(arg1, arg2) — in SQF this is typically postfix.</summary>
public sealed record SqfCall(SqfExpression Target, List<SqfExpression> Arguments) : SqfExpression;

/// <summary>Inline if-expression: if (cond) then { val1 } else { val2 }.</summary>
public sealed record SqfIfExpression(
    SqfExpression Condition,
    SqfExpression ThenExpr,
    SqfExpression? ElseExpr) : SqfExpression;

// ─── Statements ───

/// <summary>Expression statement: expr;</summary>
public sealed record SqfExpressionStatement(SqfExpression Expression) : SqfStatement;

/// <summary>If statement: if (cond) then { ... } else { ... }.</summary>
public sealed record SqfIfStatement(
    SqfExpression Condition,
    SqfCodeBlock ThenBlock,
    SqfCodeBlock? ElseBlock) : SqfStatement;

/// <summary>While loop: while { cond } do { ... }.</summary>
public sealed record SqfWhileStatement(
    SqfCodeBlock Condition,
    SqfCodeBlock Body) : SqfStatement;

/// <summary>For loop: for [{ init }, { cond }, { step }] do { ... }.</summary>
public sealed record SqfForStatement(
    SqfCodeBlock Init,
    SqfCodeBlock Condition,
    SqfCodeBlock Step,
    SqfCodeBlock Body) : SqfStatement;

/// <summary>ForEach loop: { ... } forEach array; or forEach { ... } array.</summary>
public sealed record SqfForEachStatement(
    SqfCodeBlock Body,
    SqfExpression Collection) : SqfStatement;

/// <summary>Switch statement: switch (expr) do { ... }.</summary>
public sealed record SqfSwitchStatement(
    SqfExpression Value,
    SqfCodeBlock Body) : SqfStatement;

/// <summary>Private declaration: private _var or private ["_a","_b"].</summary>
public sealed record SqfPrivateStatement(List<SqfExpression> Variables) : SqfStatement;

/// <summary>Params declaration: params ["_a", "_b"] or params [["_a", 0]].</summary>
public sealed record SqfParamsStatement(List<SqfExpression> Parameters) : SqfStatement;

/// <summary>Try/catch statement: try { ... } catch { ... }.</summary>
public sealed record SqfTryCatchStatement(SqfCodeBlock TryBlock, SqfCodeBlock CatchBlock) : SqfStatement;

/// <summary>Throw statement: throw expr.</summary>
public sealed record SqfThrowStatement(SqfExpression Value) : SqfStatement;

/// <summary>Break statement: exits current loop.</summary>
public sealed record SqfBreakStatement() : SqfStatement;

/// <summary>Continue statement: skips to next iteration.</summary>
public sealed record SqfContinueStatement() : SqfStatement;

/// <summary>BreakWith statement: exits loop with return value.</summary>
public sealed record SqfBreakWithStatement(SqfExpression Value) : SqfStatement;

/// <summary>ContinueWith statement: skips iteration with return value.</summary>
public sealed record SqfContinueWithStatement(SqfExpression Value) : SqfStatement;

/// <summary>Scope name declaration: scopeName "name".</summary>
public sealed record SqfScopeNameStatement(string Name) : SqfStatement;

/// <summary>Case/default entry inside a switch body. Value=null means default.</summary>
public sealed record SqfCaseStatement(SqfExpression? Value, SqfCodeBlock? Body) : SqfStatement;

// ─── Top-Level ───

/// <summary>An entire SQF file.</summary>
public sealed record SqfFile(List<SqfStatement> Statements, string FilePath = "", string SourceText = "") : SqfAstNode;
