using Xunit;
using BIS.SQF;

namespace BIS.SQF.Test;

public class SqfParserTest
{
    private static SqfFile Parse(string source)
    {
        var tokens = new SqfTokenizer(source).Tokenize();
        return new SqfParser(tokens).ParseFile(source);
    }

    private static void AssertSingleExprStmt(SqfFile file, System.Action<SqfExpression> assertExpr)
    {
        Assert.Single(file.Statements);
        var stmt = Assert.IsType<SqfExpressionStatement>(file.Statements[0]);
        assertExpr(stmt.Expression);
    }

    // ─── Empty / Simple ───

    [Fact]
    public void ParseFile_Empty_ReturnsNoStatements()
    {
        var file = Parse("");
        Assert.Empty(file.Statements);
    }

    [Fact]
    public void ParseFile_EmptySemicolons_ReturnsNoStatements()
    {
        var file = Parse(";;;");
        Assert.Empty(file.Statements);
    }

    // ─── Literals ───

    [Fact]
    public void Parse_Identifier()
    {
        var file = Parse("player;");
        AssertSingleExprStmt(file, expr =>
        {
            var id = Assert.IsType<SqfIdentifier>(expr);
            Assert.Equal("player", id.Name);
        });
    }

    [Fact]
    public void Parse_LocalVariable()
    {
        var file = Parse("_myVar;");
        AssertSingleExprStmt(file, expr =>
        {
            var id = Assert.IsType<SqfIdentifier>(expr);
            Assert.Equal("_myVar", id.Name);
        });
    }

    [Fact]
    public void Parse_StringLiteral()
    {
        var file = Parse("\"hello\";");
        AssertSingleExprStmt(file, expr =>
        {
            var s = Assert.IsType<SqfStringLiteral>(expr);
            Assert.Equal("hello", s.Value);
        });
    }

    [Fact]
    public void Parse_NumberLiteral_Integer()
    {
        var file = Parse("42;");
        AssertSingleExprStmt(file, expr =>
        {
            var n = Assert.IsType<SqfNumberLiteral>(expr);
            Assert.Equal("42", n.Text);
        });
    }

    [Fact]
    public void Parse_NumberLiteral_Float()
    {
        var file = Parse("3.14;");
        AssertSingleExprStmt(file, expr =>
        {
            var n = Assert.IsType<SqfNumberLiteral>(expr);
            Assert.Equal("3.14", n.Text);
        });
    }

    [Fact]
    public void Parse_NumberLiteral_Hex()
    {
        var file = Parse("0xFF;");
        AssertSingleExprStmt(file, expr =>
        {
            var n = Assert.IsType<SqfNumberLiteral>(expr);
            Assert.Equal("0xFF", n.Text);
        });
    }

    [Fact]
    public void Parse_BooleanLiteral_True()
    {
        var file = Parse("true;");
        AssertSingleExprStmt(file, expr =>
        {
            var b = Assert.IsType<SqfBooleanLiteral>(expr);
            Assert.True(b.Value);
        });
    }

    [Fact]
    public void Parse_BooleanLiteral_False()
    {
        var file = Parse("false;");
        AssertSingleExprStmt(file, expr =>
        {
            var b = Assert.IsType<SqfBooleanLiteral>(expr);
            Assert.False(b.Value);
        });
    }

    // ─── Postfix Commands ───

    [Fact]
    public void Parse_Postfix_BinaryCommand()
    {
        // player distance _target → distance(player, _target)
        var file = Parse("player distance _target;");
        AssertSingleExprStmt(file, expr =>
        {
            var call = Assert.IsType<SqfCall>(expr);
            var cmd = Assert.IsType<SqfIdentifier>(call.Target);
            Assert.Equal("distance", cmd.Name);
            Assert.Equal(2, call.Arguments.Count);
            Assert.IsType<SqfIdentifier>(call.Arguments[0]);
            Assert.Equal("player", ((SqfIdentifier)call.Arguments[0]).Name);
            Assert.IsType<SqfIdentifier>(call.Arguments[1]);
            Assert.Equal("_target", ((SqfIdentifier)call.Arguments[1]).Name);
        });
    }

    [Fact]
    public void Parse_Postfix_SetPosWithArray()
    {
        // player setPos [0,0,0] → setPos(player, [0,0,0])
        var file = Parse("player setPos [0,0,0];");
        AssertSingleExprStmt(file, expr =>
        {
            var call = Assert.IsType<SqfCall>(expr);
            var cmd = Assert.IsType<SqfIdentifier>(call.Target);
            Assert.Equal("setPos", cmd.Name);
            Assert.Equal(2, call.Arguments.Count);
            Assert.IsType<SqfIdentifier>(call.Arguments[0]);
            var arr = Assert.IsType<SqfArrayLiteral>(call.Arguments[1]);
            Assert.Equal(3, arr.Elements.Count);
        });
    }

    [Fact]
    public void Parse_Prefix_UnaryCommand()
    {
        // getPos player → getPos(player)
        var file = Parse("getPos player;");
        AssertSingleExprStmt(file, expr =>
        {
            var call = Assert.IsType<SqfCall>(expr);
            var cmd = Assert.IsType<SqfIdentifier>(call.Target);
            Assert.Equal("getPos", cmd.Name);
            Assert.Single(call.Arguments);
            var arg = Assert.IsType<SqfIdentifier>(call.Arguments[0]);
            Assert.Equal("player", arg.Name);
        });
    }

    [Fact]
    public void Parse_Postfix_ForEach()
    {
        // {_x;} forEach allUnits;
        var file = Parse("{_x;} forEach allUnits;");
        AssertSingleExprStmt(file, expr =>
        {
            var call = Assert.IsType<SqfCall>(expr);
            var cmd = Assert.IsType<SqfIdentifier>(call.Target);
            Assert.Equal("forEach", cmd.Name);
            Assert.Equal(2, call.Arguments.Count);
            Assert.IsType<SqfCodeBlock>(call.Arguments[0]);
            var arg1 = Assert.IsType<SqfIdentifier>(call.Arguments[1]);
            Assert.Equal("allUnits", arg1.Name);
        });
    }

    [Fact]
    public void Parse_Postfix_SelectOnArray()
    {
        // [1,2,3] select 0
        var file = Parse("[1,2,3] select 0;");
        AssertSingleExprStmt(file, expr =>
        {
            var call = Assert.IsType<SqfCall>(expr);
            var cmd = Assert.IsType<SqfIdentifier>(call.Target);
            Assert.Equal("select", cmd.Name);
            Assert.Equal(2, call.Arguments.Count);
            Assert.IsType<SqfArrayLiteral>(call.Arguments[0]);
            var arg1 = Assert.IsType<SqfNumberLiteral>(call.Arguments[1]);
            Assert.Equal("0", arg1.Text);
        });
    }

    // ─── Arrays ───

    [Fact]
    public void Parse_ArrayLiteral_Empty()
    {
        var file = Parse("[];");
        AssertSingleExprStmt(file, expr =>
        {
            var arr = Assert.IsType<SqfArrayLiteral>(expr);
            Assert.Empty(arr.Elements);
        });
    }

    [Fact]
    public void Parse_ArrayLiteral_SingleElement()
    {
        var file = Parse("[42];");
        AssertSingleExprStmt(file, expr =>
        {
            var arr = Assert.IsType<SqfArrayLiteral>(expr);
            Assert.Single(arr.Elements);
            var n = Assert.IsType<SqfNumberLiteral>(arr.Elements[0]);
            Assert.Equal("42", n.Text);
        });
    }

    [Fact]
    public void Parse_ArrayLiteral_MultipleElements()
    {
        var file = Parse("[1, 2, 3];");
        AssertSingleExprStmt(file, expr =>
        {
            var arr = Assert.IsType<SqfArrayLiteral>(expr);
            Assert.Equal(3, arr.Elements.Count);
        });
    }

    [Fact]
    public void Parse_ArrayLiteral_Nested()
    {
        var file = Parse("[[1,2], [3,4]];");
        AssertSingleExprStmt(file, expr =>
        {
            var arr = Assert.IsType<SqfArrayLiteral>(expr);
            Assert.Equal(2, arr.Elements.Count);
            Assert.IsType<SqfArrayLiteral>(arr.Elements[0]);
        });
    }

    // ─── Code Blocks ───

    [Fact]
    public void Parse_CodeBlock_Empty()
    {
        var file = Parse("{};");
        AssertSingleExprStmt(file, expr =>
        {
            var block = Assert.IsType<SqfCodeBlock>(expr);
            Assert.Empty(block.Statements);
        });
    }

    [Fact]
    public void Parse_CodeBlock_SingleStatement()
    {
        var file = Parse("{player;};");
        AssertSingleExprStmt(file, expr =>
        {
            var block = Assert.IsType<SqfCodeBlock>(expr);
            Assert.Single(block.Statements);
            var stmt = Assert.IsType<SqfExpressionStatement>(block.Statements[0]);
            var id = Assert.IsType<SqfIdentifier>(stmt.Expression);
            Assert.Equal("player", id.Name);
        });
    }

    [Fact]
    public void Parse_CodeBlock_MultipleStatements()
    {
        var file = Parse("{player; _x = 5;};");
        AssertSingleExprStmt(file, expr =>
        {
            var block = Assert.IsType<SqfCodeBlock>(expr);
            Assert.Equal(2, block.Statements.Count);
        });
    }

    // ─── Parenthesized Expressions ───

    [Fact]
    public void Parse_ParenExpr()
    {
        var file = Parse("(player);");
        AssertSingleExprStmt(file, expr =>
        {
            var paren = Assert.IsType<SqfParenExpr>(expr);
            var id = Assert.IsType<SqfIdentifier>(paren.Inner);
            Assert.Equal("player", id.Name);
        });
    }

    [Fact]
    public void Parse_ParenExpr_Nested()
    {
        var file = Parse("((42));");
        AssertSingleExprStmt(file, expr =>
        {
            var outer = Assert.IsType<SqfParenExpr>(expr);
            var inner = Assert.IsType<SqfParenExpr>(outer.Inner);
            var n = Assert.IsType<SqfNumberLiteral>(inner.Inner);
            Assert.Equal("42", n.Text);
        });
    }

    // ─── Unary Operators ───

    [Fact]
    public void Parse_UnaryNot()
    {
        var file = Parse("!alive;");
        AssertSingleExprStmt(file, expr =>
        {
            var op = Assert.IsType<SqfUnaryOp>(expr);
            Assert.Equal("!", op.Operator);
            var id = Assert.IsType<SqfIdentifier>(op.Operand);
            Assert.Equal("alive", id.Name);
        });
    }

    [Fact]
    public void Parse_UnaryNegation()
    {
        var file = Parse("-5;");
        AssertSingleExprStmt(file, expr =>
        {
            var op = Assert.IsType<SqfUnaryOp>(expr);
            Assert.Equal("-", op.Operator);
            var n = Assert.IsType<SqfNumberLiteral>(op.Operand);
            Assert.Equal("5", n.Text);
        });
    }

    // ─── Binary Operators ───

    [Fact]
    public void Parse_Binary_Addition()
    {
        var file = Parse("1 + 2;");
        AssertSingleExprStmt(file, expr =>
        {
            var op = Assert.IsType<SqfBinaryOp>(expr);
            Assert.Equal("+", op.Operator);
            var left = Assert.IsType<SqfNumberLiteral>(op.Left);
            Assert.Equal("1", left.Text);
            var right = Assert.IsType<SqfNumberLiteral>(op.Right);
            Assert.Equal("2", right.Text);
        });
    }

    [Fact]
    public void Parse_Binary_Comparison()
    {
        var file = Parse("_x > 5;");
        AssertSingleExprStmt(file, expr =>
        {
            var op = Assert.IsType<SqfBinaryOp>(expr);
            Assert.Equal(">", op.Operator);
        });
    }

    [Fact]
    public void Parse_Binary_Equality()
    {
        var file = Parse("_x == _y;");
        AssertSingleExprStmt(file, expr =>
        {
            var op = Assert.IsType<SqfBinaryOp>(expr);
            Assert.Equal("==", op.Operator);
        });
    }

    [Fact]
    public void Parse_Binary_LogicalAnd()
    {
        var file = Parse("a && b;");
        AssertSingleExprStmt(file, expr =>
        {
            var op = Assert.IsType<SqfBinaryOp>(expr);
            Assert.Equal("&&", op.Operator);
        });
    }

    // ─── Assignment ───

    [Fact]
    public void Parse_Assign_Simple()
    {
        var file = Parse("_x = 5;");
        AssertSingleExprStmt(file, expr =>
        {
            var assign = Assert.IsType<SqfAssign>(expr);
            Assert.Equal("_x", assign.Name);
            var val = Assert.IsType<SqfNumberLiteral>(assign.Value);
            Assert.Equal("5", val.Text);
        });
    }

    [Fact]
    public void Parse_Assign_Expression()
    {
        var file = Parse("_x = player distance _target;");
        AssertSingleExprStmt(file, expr =>
        {
            var assign = Assert.IsType<SqfAssign>(expr);
            Assert.Equal("_x", assign.Name);
            Assert.IsType<SqfCall>(assign.Value);
        });
    }

    // ─── Config Path ───

    [Fact]
    public void Parse_ConfigPath_Simple()
    {
        var file = Parse("configFile >> \"CfgVehicles\";");
        AssertSingleExprStmt(file, expr =>
        {
            var path = Assert.IsType<SqfConfigPath>(expr);
            Assert.Equal(2, path.Parts.Count);
            var first = Assert.IsType<SqfIdentifier>(path.Parts[0]);
            Assert.Equal("configFile", first.Name);
            var second = Assert.IsType<SqfStringLiteral>(path.Parts[1]);
            Assert.Equal("CfgVehicles", second.Value);
        });
    }

    [Fact]
    public void Parse_ConfigPath_Multiple()
    {
        var file = Parse("configFile >> \"CfgVehicles\" >> \"Soldier\";");
        AssertSingleExprStmt(file, expr =>
        {
            var path = Assert.IsType<SqfConfigPath>(expr);
            Assert.Equal(3, path.Parts.Count);
        });
    }

    [Fact]
    public void Parse_ConfigPath_StartWithGreater()
    {
        var file = Parse(">> \"CfgVehicles\";");
        AssertSingleExprStmt(file, expr =>
        {
            var path = Assert.IsType<SqfConfigPath>(expr);
            Assert.Single(path.Parts);
            var part = Assert.IsType<SqfStringLiteral>(path.Parts[0]);
            Assert.Equal("CfgVehicles", part.Value);
        });
    }

    // ─── If / Then / Else ───

    [Fact]
    public void Parse_IfThen()
    {
        var file = Parse("if (alive player) then {hint \"ok\";};");
        Assert.Single(file.Statements);
        var ifStmt = Assert.IsType<SqfIfStatement>(file.Statements[0]);
        Assert.NotNull(ifStmt.Condition);
        Assert.NotNull(ifStmt.ThenBlock);
        Assert.Null(ifStmt.ElseBlock);
    }

    [Fact]
    public void Parse_IfThenElse()
    {
        var file = Parse("if (alive player) then {hint \"ok\";} else {hint \"dead\";};");
        Assert.Single(file.Statements);
        var ifStmt = Assert.IsType<SqfIfStatement>(file.Statements[0]);
        Assert.NotNull(ifStmt.Condition);
        Assert.NotNull(ifStmt.ThenBlock);
        Assert.NotNull(ifStmt.ElseBlock);
    }

    [Fact]
    public void Parse_IfThen_NestedExpr()
    {
        var file = Parse("if (_x > 5) then {hint str _x;};");
        Assert.Single(file.Statements);
        var ifStmt = Assert.IsType<SqfIfStatement>(file.Statements[0]);
        var cond = Assert.IsType<SqfBinaryOp>(ifStmt.Condition);
        Assert.Equal(">", cond.Operator);
    }

    // ─── While ───

    [Fact]
    public void Parse_While()
    {
        var file = Parse("while {alive player} do {player setDamage 1;};");
        Assert.Single(file.Statements);
        var whileStmt = Assert.IsType<SqfWhileStatement>(file.Statements[0]);
        Assert.NotNull(whileStmt.Condition);
        Assert.NotNull(whileStmt.Body);
    }

    [Fact]
    public void Parse_While_Complex()
    {
        var file = Parse("while {_i < 10} do {_i = _i + 1;};");
        Assert.Single(file.Statements);
        var whileStmt = Assert.IsType<SqfWhileStatement>(file.Statements[0]);
        Assert.Single(whileStmt.Condition.Statements);
    }

    // ─── For ───

    [Fact]
    public void Parse_For()
    {
        var file = Parse("for [{_i = 0}, {_i < 10}, {_i = _i + 1}] do {hint str _i;};");
        Assert.Single(file.Statements);
        var forStmt = Assert.IsType<SqfForStatement>(file.Statements[0]);
        Assert.NotNull(forStmt.Init);
        Assert.NotNull(forStmt.Condition);
        Assert.NotNull(forStmt.Step);
        Assert.NotNull(forStmt.Body);
    }

    [Fact]
    public void Parse_For_WithExpressions()
    {
        // for with bare expressions instead of code blocks
        var file = Parse("for [{_i=0}, {alive player}, {_i=_i+1}] do {sleep 1;};");
        Assert.Single(file.Statements);
        Assert.IsType<SqfForStatement>(file.Statements[0]);
    }

    // ─── Switch ───

    [Fact]
    public void Parse_Switch()
    {
        var file = Parse("switch (_x) do {hint \"one\";};");
        Assert.Single(file.Statements);
        var switchStmt = Assert.IsType<SqfSwitchStatement>(file.Statements[0]);
        Assert.IsType<SqfIdentifier>(switchStmt.Value);
        Assert.NotNull(switchStmt.Body);
    }

    // ─── Private ───

    [Fact]
    public void Parse_Private_Single()
    {
        var file = Parse("private _x;");
        Assert.Single(file.Statements);
        var priv = Assert.IsType<SqfPrivateStatement>(file.Statements[0]);
        Assert.Single(priv.Variables);
        var id = Assert.IsType<SqfIdentifier>(priv.Variables[0]);
        Assert.Equal("_x", id.Name);
    }

    [Fact]
    public void Parse_Private_WithAssignment()
    {
        var file = Parse("private _x = 5;");
        Assert.Single(file.Statements);
        var priv = Assert.IsType<SqfPrivateStatement>(file.Statements[0]);
        Assert.Single(priv.Variables);
        var assign = Assert.IsType<SqfAssign>(priv.Variables[0]);
        Assert.Equal("_x", assign.Name);
        var val = Assert.IsType<SqfNumberLiteral>(assign.Value);
        Assert.Equal("5", val.Text);
    }

    [Fact]
    public void Parse_Private_Array()
    {
        var file = Parse("private [\"_a\", \"_b\"];");
        Assert.Single(file.Statements);
        var priv = Assert.IsType<SqfPrivateStatement>(file.Statements[0]);
        Assert.Equal(2, priv.Variables.Count);
        var a = Assert.IsType<SqfStringLiteral>(priv.Variables[0]);
        Assert.Equal("_a", a.Value);
        var b = Assert.IsType<SqfStringLiteral>(priv.Variables[1]);
        Assert.Equal("_b", b.Value);
    }

    [Fact]
    public void Parse_Private_MultipleWithComma()
    {
        var file = Parse("private _a, _b, _c;");
        Assert.Single(file.Statements);
        var priv = Assert.IsType<SqfPrivateStatement>(file.Statements[0]);
        Assert.Equal(3, priv.Variables.Count);
    }

    // ─── Params ───

    [Fact]
    public void Parse_Params_Simple()
    {
        var file = Parse("params [\"_a\", \"_b\"];");
        Assert.Single(file.Statements);
        var par = Assert.IsType<SqfParamsStatement>(file.Statements[0]);
        Assert.Equal(2, par.Parameters.Count);
    }

    [Fact]
    public void Parse_Params_WithDefaults()
    {
        var file = Parse("params [[\"_a\", 0], [\"_b\", 1]];");
        Assert.Single(file.Statements);
        var par = Assert.IsType<SqfParamsStatement>(file.Statements[0]);
        Assert.Equal(2, par.Parameters.Count);
        Assert.IsType<SqfArrayLiteral>(par.Parameters[0]);
        Assert.IsType<SqfArrayLiteral>(par.Parameters[1]);
    }

    // ─── Multiple Statements ───

    [Fact]
    public void Parse_MultipleStatements()
    {
        var file = Parse("player; _x = 5; hint \"hello\";");
        Assert.Equal(3, file.Statements.Count);
    }

    [Fact]
    public void Parse_ComplexScript()
    {
        var src = @"
private _x = 5;
if (_x > 3) then {
    hint ""big"";
} else {
    hint ""small"";
};
while {_x < 10} do {
    _x = _x + 1;
};
";
        var file = Parse(src);
        Assert.Equal(3, file.Statements.Count);
        Assert.IsType<SqfPrivateStatement>(file.Statements[0]);
        Assert.IsType<SqfIfStatement>(file.Statements[1]);
        Assert.IsType<SqfWhileStatement>(file.Statements[2]);
    }

    // ─── Source Location Tracking ───

    [Fact]
    public void Parse_SourceLocation_OnNodes()
    {
        var file = Parse("player;");
        Assert.Single(file.Statements);
        var stmt = file.Statements[0];
        Assert.Equal(1, stmt.Line);
        Assert.Equal(1, stmt.Column);
        var exprStmt = Assert.IsType<SqfExpressionStatement>(stmt);
        Assert.Equal(1, exprStmt.Expression.Line);
    }

    // ─── Error Cases ───

    [Fact]
    public void Parse_Error_UnterminatedArray()
    {
        var ex = Assert.Throws<FormatException>(() => Parse("[1, 2;"));
        Assert.Contains("Expected ']'", ex.Message);
    }

    [Fact]
    public void Parse_Error_UnterminatedCodeBlock()
    {
        var ex = Assert.Throws<FormatException>(() => Parse("{player;"));
        Assert.Contains("Expected '}'", ex.Message);
    }

    [Fact]
    public void Parse_Error_UnterminatedParen()
    {
        var ex = Assert.Throws<FormatException>(() => Parse("(player;"));
        Assert.Contains("Expected ')'", ex.Message);
    }

    [Fact]
    public void Parse_Error_MissingSemicolon()
    {
        var ex = Assert.Throws<FormatException>(() => Parse("player"));
        Assert.Contains("Expected ';'", ex.Message);
    }

    [Fact]
    public void Parse_IfWithoutThen_Succeeds()
    {
        // Valid SQF: if (cond) { body }; is equivalent to if (cond) then { body };
        var result = Parse("if (true) {hint;};");
        Assert.Single(result.Statements);
        Assert.IsType<SqfIfStatement>(result.Statements[0]);
    }

    [Fact]
    public void Parse_Error_WhileMissingDo()
    {
        var ex = Assert.Throws<FormatException>(() => Parse("while {true} {hint;};"));
        Assert.Contains("Expected 'do'", ex.Message);
    }

    [Fact]
    public void Parse_Error_AssignmentToNonIdentifier()
    {
        var ex = Assert.Throws<FormatException>(() => Parse("5 = 3;"));
        Assert.Contains("Left side of assignment must be an identifier", ex.Message);
    }

    // ─── Comments (already stripped by tokenizer) ───

    [Fact]
    public void Parse_WithLineComments()
    {
        var file = Parse("// comment\nplayer;");
        Assert.Single(file.Statements);
    }

    [Fact]
    public void Parse_WithBlockComments()
    {
        var file = Parse("/* block */ player;");
        Assert.Single(file.Statements);
    }

    // ─── Deeply Nested ───

    [Fact]
    public void Parse_DeeplyNestedArrays()
    {
        var file = Parse("[[[1]]];");
        AssertSingleExprStmt(file, expr =>
        {
            var outer = Assert.IsType<SqfArrayLiteral>(expr);
            var mid = Assert.IsType<SqfArrayLiteral>(outer.Elements[0]);
            var inner = Assert.IsType<SqfArrayLiteral>(mid.Elements[0]);
            Assert.Single(inner.Elements);
        });
    }

    [Fact]
    public void Parse_NestedCodeBlocks()
    {
        var file = Parse("{{player;};};");
        AssertSingleExprStmt(file, expr =>
        {
            var outer = Assert.IsType<SqfCodeBlock>(expr);
            Assert.Single(outer.Statements);
            var innerStmt = Assert.IsType<SqfExpressionStatement>(outer.Statements[0]);
            var innerBlock = Assert.IsType<SqfCodeBlock>(innerStmt.Expression);
            Assert.Single(innerBlock.Statements);
        });
    }

    // ─── For From/To/Step ───

    [Fact]
    public void Parse_For_FromTo()
    {
        var file = Parse("for \"_i\" from 0 to 10 do {hint str _i;};");
        Assert.Single(file.Statements);
        var forStmt = Assert.IsType<SqfForStatement>(file.Statements[0]);
        Assert.NotNull(forStmt.Init);
        Assert.NotNull(forStmt.Condition);
        Assert.NotNull(forStmt.Step);
        Assert.NotNull(forStmt.Body);
        Assert.Single(forStmt.Body.Statements);
    }

    [Fact]
    public void Parse_For_FromToWithStep()
    {
        var file = Parse("for \"_i\" from 0 to 10 step 2 do {hint str _i;};");
        Assert.Single(file.Statements);
        var forStmt = Assert.IsType<SqfForStatement>(file.Statements[0]);
        Assert.NotNull(forStmt.Init);
        Assert.NotNull(forStmt.Condition);
        Assert.NotNull(forStmt.Step);
        Assert.NotNull(forStmt.Body);
    }

    // ─── Private String ───

    [Fact]
    public void Parse_Private_StringArg()
    {
        var file = Parse("private \"_x\";");
        Assert.Single(file.Statements);
        var priv = Assert.IsType<SqfPrivateStatement>(file.Statements[0]);
        Assert.Single(priv.Variables);
        var s = Assert.IsType<SqfStringLiteral>(priv.Variables[0]);
        Assert.Equal("_x", s.Value);
    }

    // ─── Try / Catch ───

    [Fact]
    public void Parse_TryCatch()
    {
        var file = Parse("try {hint \"ok\";} catch {hint \"err\";};");
        Assert.Single(file.Statements);
        var tc = Assert.IsType<SqfTryCatchStatement>(file.Statements[0]);
        Assert.NotNull(tc.TryBlock);
        Assert.NotNull(tc.CatchBlock);
        Assert.Single(tc.TryBlock.Statements);
        Assert.Single(tc.CatchBlock.Statements);
    }

    // ─── Throw ───

    [Fact]
    public void Parse_Throw()
    {
        var file = Parse("throw \"error\";");
        Assert.Single(file.Statements);
        var thr = Assert.IsType<SqfThrowStatement>(file.Statements[0]);
        var msg = Assert.IsType<SqfStringLiteral>(thr.Value);
        Assert.Equal("error", msg.Value);
    }

    // ─── Break / Continue ───

    [Fact]
    public void Parse_Break()
    {
        var file = Parse("break;");
        Assert.Single(file.Statements);
        Assert.IsType<SqfBreakStatement>(file.Statements[0]);
    }

    [Fact]
    public void Parse_Continue()
    {
        var file = Parse("continue;");
        Assert.Single(file.Statements);
        Assert.IsType<SqfContinueStatement>(file.Statements[0]);
    }

    // ─── BreakWith / ContinueWith ───

    [Fact]
    public void Parse_BreakWith()
    {
        var file = Parse("breakWith 42;");
        Assert.Single(file.Statements);
        var bw = Assert.IsType<SqfBreakWithStatement>(file.Statements[0]);
        var val = Assert.IsType<SqfNumberLiteral>(bw.Value);
        Assert.Equal("42", val.Text);
    }

    [Fact]
    public void Parse_ContinueWith()
    {
        var file = Parse("continueWith 42;");
        Assert.Single(file.Statements);
        var cw = Assert.IsType<SqfContinueWithStatement>(file.Statements[0]);
        var val = Assert.IsType<SqfNumberLiteral>(cw.Value);
        Assert.Equal("42", val.Text);
    }

    // ─── ScopeName ───

    [Fact]
    public void Parse_ScopeName()
    {
        var file = Parse("scopeName \"myScope\";");
        Assert.Single(file.Statements);
        var sn = Assert.IsType<SqfScopeNameStatement>(file.Statements[0]);
        Assert.Equal("myScope", sn.Name);
    }

    // ─── Case / Default ───

    [Fact]
    public void Parse_SwitchCase_WithCode()
    {
        var file = Parse("switch (_x) do {case 1: {hint \"one\";};};");
        Assert.Single(file.Statements);
        var sw = Assert.IsType<SqfSwitchStatement>(file.Statements[0]);
        Assert.Single(sw.Body.Statements);
        var cs = Assert.IsType<SqfCaseStatement>(sw.Body.Statements[0]);
        var val = Assert.IsType<SqfNumberLiteral>(cs.Value!);
        Assert.Equal("1", val.Text);
        Assert.NotNull(cs.Body);
    }

    [Fact]
    public void Parse_SwitchCase_Fallthrough()
    {
        // case with no colon, just semicolon
        var file = Parse("switch (_x) do {case 1;};");
        Assert.Single(file.Statements);
        var sw = Assert.IsType<SqfSwitchStatement>(file.Statements[0]);
        Assert.Single(sw.Body.Statements);
        var cs = Assert.IsType<SqfCaseStatement>(sw.Body.Statements[0]);
        Assert.Null(cs.Body);
    }

    [Fact]
    public void Parse_SwitchDefault()
    {
        var file = Parse("switch (_x) do {default {hint \"other\";};};");
        Assert.Single(file.Statements);
        var sw = Assert.IsType<SqfSwitchStatement>(file.Statements[0]);
        Assert.Single(sw.Body.Statements);
        var cs = Assert.IsType<SqfCaseStatement>(sw.Body.Statements[0]);
        Assert.Null(cs.Value);
        Assert.NotNull(cs.Body);
    }

    // ─── Malformed Input Fuzz Tests ───

    [Fact]
    public void Parse_Fuzz_EmptyString()
    {
        var source = "";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_OnlyWhitespace()
    {
        var source = "   \t  \n  \r\n  ";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_OnlyLineComment()
    {
        var source = "// nothing here";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_OnlyBlockComment()
    {
        var source = "/* nothing \n here */";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_UnmatchedOpenBrace()
    {
        var source = "{";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_UnmatchedCloseBrace()
    {
        var source = "}";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_UnmatchedOpenBracket()
    {
        var source = "[";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_UnmatchedOpenParen()
    {
        var source = "(";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_UnterminatedDoubleQuotedString()
    {
        var source = "\"no end";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_UnterminatedSingleQuotedString()
    {
        var source = "'no end";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_GarbageCharacters()
    {
        var source = "@#$%^&*()";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_ExtremelyLongIdentifier()
    {
        var source = new string('x', 1000) + ";";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_DeeplyNestedBraces()
    {
        // 60 levels of nested braces — should not StackOverflow
        var source = new string('{', 60) + new string('}', 60) + ";";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_MixedValidAndInvalid()
    {
        var source = "_x = 1; @@@; _y = 2;";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_NegativeNumberLiteral()
    {
        var source = "-42;";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_MultipleDecimalPoints()
    {
        var source = "1.2.3;";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_ConsecutiveOperators()
    {
        var source = "_x = 1 + - * / 2;";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_UnterminatedEscapeInString()
    {
        var source = "\"hello \\";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_SemicolonInExpression()
    {
        var source = "_x = ; 1;";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_BinaryOperatorWithoutOperands()
    {
        var source = "+;";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        Assert.NotNull(ex);
        Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_MixedLineAndBlockComment()
    {
        var source = "/* // */";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_PreprocessorNotAtLineStart()
    {
        var source = "  #include \"x\"";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }

    [Fact]
    public void Parse_Fuzz_TabCharactersInCode()
    {
        var source = "_x\t=\t1;";
        var ex = Record.Exception(() =>
        {
            var tokens = new SqfTokenizer(source).Tokenize();
            var parser = new SqfParser(tokens);
            parser.ParseFile(source);
        });
        if (ex != null)
            Assert.IsType<FormatException>(ex);
    }
}
