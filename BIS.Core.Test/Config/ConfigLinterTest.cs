using BIS.Core.Config;
using System.Linq;
using Xunit;

namespace BIS.Core.Test.Config
{
    public class ConfigLinterTest
    {
        private static ParamFile ParseSource(string source)
        {
            var tokens = ConfigTokenizer.Tokenize(source, "test.cpp");
            var parser = new ConfigParser();
            return parser.Parse(tokens);
        }

        private static IReadOnlyList<LintDiagnostic> Lint(string source)
        {
            var file = ParseSource(source);
            var linter = new ConfigLinter();
            return linter.Lint(file);
        }

        private static IReadOnlyList<LintDiagnostic> LintWithSource(string source)
        {
            var file = ParseSource(source);
            var linter = new ConfigLinter();
            return linter.Lint(file, source);
        }

        // ─── L-C02: Duplicate property ───

        [Fact]
        public void L_C02_DuplicateProperty_EmitsError()
        {
            var diags = Lint(@"
class CfgVehicles {
    value = 1;
    value = 2;
};
");
            Assert.Contains(diags, d => d.Code == "L-C02");
        }

        [Fact]
        public void L_C02_NoDuplicateProperty_NoDiagnostic()
        {
            var diags = Lint(@"
class CfgVehicles {
    value1 = 1;
    value2 = 2;
};
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C02");
        }

        // ─── L-C03: Duplicate classes ───

        [Fact]
        public void L_C03_DuplicateClass_EmitsError()
        {
            var diags = Lint(@"
class CfgVehicles {
    class MyClass {};
    class MyClass {};
};
");
            Assert.Contains(diags, d => d.Code == "L-C03");
        }

        [Fact]
        public void L_C03_NoDuplicateClass_NoDiagnostic()
        {
            var diags = Lint(@"
class CfgVehicles {
    class ClassA {};
    class ClassB {};
};
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C03");
        }

        // L-C06: Unexpected array — enforced at parse time ([] requires {})
        // L-C07: Expected array

        // ─── L-C07: Expected array ───

        [Fact]
        public void L_C07_ExpectedArray_EmitsError()
        {
            var diags = Lint(@"
class Values {
    data = {1, 2, 3};
};
");
            Assert.Contains(diags, d => d.Code == "L-C07");
        }

        [Fact]
        public void L_C07_NoExpectedArray_NoDiagnostic()
        {
            var diags = Lint(@"
class Values {
    data = 1;
    data[] = {1, 2, 3};
};
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C07");
        }

        // ─── L-C11: File type ───

        [Fact]
        public void L_C11_ModelWithoutP3D_EmitsWarning()
        {
            var diags = Lint(@"
class Default {
    model = ""my_model.blend"";
};
");
            Assert.Contains(diags, d => d.Code == "L-C11");
        }

        [Fact]
        public void L_C11_ModelWithP3D_NoDiagnostic()
        {
            var diags = Lint(@"
class Default {
    model = ""my_model.p3d"";
};
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C11");
        }

        [Fact]
        public void L_C11_ModelWithoutExtension_EmitsWarning()
        {
            var diags = Lint(@"
class Default {
    model = ""my_model"";
};
");
            Assert.Contains(diags, d => d.Code == "L-C11");
        }

        // ─── L-C12: Math could be unquoted ───

        [Fact]
        public void L_C12_MathExpression_EmitsHelp()
        {
            var diags = Lint(@"
class Values {
    speed = '2+3';
};
");
            var mathDiags = diags.Where(d => d.Code == "L-C12").ToList();
            Assert.NotEmpty(mathDiags);
        }

        [Fact]
        public void L_C12_NoMathExpression_NoDiagnostic()
        {
            var diags = Lint(@"
class Values {
    text = ""Hello World"";
};
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C12");
        }

        // ─── L-C13: Config this call ───

        [Fact]
        public void L_C13_ThisCallInStatement_EmitsHelp()
        {
            var diags = Lint(@"
class MyAction {
    statement = ""_this call my_fnc"";
};
");
            Assert.Contains(diags, d => d.Code == "L-C13");
        }

        [Fact]
        public void L_C13_NoThisCall_NoDiagnostic()
        {
            var diags = Lint(@"
class MyAction {
    statement = ""call my_fnc"";
};
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C13");
        }

        // ─── L-C15: CfgPatches scope ───

        [Fact]
        public void L_C15_MissingVehicleInCfgPatches_EmitsWarning()
        {
            var diags = Lint(@"
class CfgPatches {
    class MyMod {
        units[] = {""MissingVehicle""};
    };
};
class CfgVehicles {
    class ExistingVehicle {};
};
");
            Assert.Contains(diags, d => d.Code == "L-C15" && d.Message.Contains("MissingVehicle"));
        }

        [Fact]
        public void L_C15_AllVehiclesPresent_NoDiagnostic()
        {
            var diags = Lint(@"
class CfgPatches {
    class MyMod {
        units[] = {""MyVehicle""};
    };
};
class CfgVehicles {
    class MyVehicle {};
};
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C15");
        }

        // ─── L-C09: Magwell missing magazine ───

        [Fact]
        public void L_C09_MagwellMissingMagazine_EmitsError()
        {
            var diags = Lint(@"
class CfgPatches {
    class MyMod {};
};
class CfgMagazineWells {
    class MyWeapon {
        mags[] = {""MissingMag""};
    };
};
class CfgMagazines {
    class ExistingMag {};
};
");
            var c09 = diags.Where(d => d.Code == "L-C09").ToList();
            Assert.Contains(c09, d => d.Message.Contains("MissingMag"));
        }

        [Fact]
        public void L_C09_AllMagazinesPresent_NoDiagnostic()
        {
            var diags = Lint(@"
class CfgMagazineWells {
    class MyWeapon {
        mags[] = {""ExistingMag""};
    };
};
class CfgMagazines {
    class ExistingMag {};
};
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C09");
        }

        // ─── L-C16: File missing ───

        [Fact]
        public void L_C16_FileReferenceWithLeadingDot_EmitsWarning()
        {
            var diags = Lint(@"
class Default {
    model = ""./relative/model.p3d"";
};
");
            Assert.Contains(diags, d => d.Code == "L-C16");
        }

        // ─── L-C04: Missing external class ───

        [Fact]
        public void L_C04_MissingExternalClass_EmitsError()
        {
            var diags = Lint(@"
class CfgVehicles {
    class MyVehicle : MissingExternal {};
};
");
            Assert.Contains(diags, d => d.Code == "L-C04" && d.Severity == LintSeverity.Error);
        }

        [Fact]
        public void L_C04_ExternalPresent_NoDiagnostic()
        {
            var diags = Lint(@"
class CfgVehicles {
    class MyVehicle : ExternalBase {};
};
class ExternalBase {};
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C04");
        }

        [Fact]
        public void L_C04_ExternDeclared_NoDiagnostic()
        {
            var diags = Lint(@"
class CfgVehicles {
    class MyVehicle : ExternalBase {};
};
class ExternalBase;
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C04");
        }

        // ─── L-C14: Unused external class ───

        [Fact]
        public void L_C14_UnusedExternal_EmitsWarning()
        {
            var diags = Lint(@"
class CfgVehicles {
    class MyVehicle {};
};
class UnusedExternal;
");
            Assert.Contains(diags, d => d.Code == "L-C14" && d.Severity == LintSeverity.Warning);
        }

        [Fact]
        public void L_C14_UsedExternal_NoDiagnostic()
        {
            var diags = Lint(@"
class CfgVehicles {
    class MyVehicle : UsedExternal {};
};
class UsedExternal;
");
            Assert.DoesNotContain(diags, d => d.Code == "L-C14");
        }

        // ─── Fix tests (L-C04, L-C05, L-C12, L-C13, L-C14) ───

        [Fact]
        public void L_C12_Fix_UnquotesMathExpression()
        {
            var source = @"
class V {
    speed = '2+3';
};";
            var diags = LintWithSource(source);
            Assert.Contains(diags, d => d.Code == "L-C12" && d.Fix != null);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.DoesNotContain("'2+3'", fixedSource);
            Assert.Contains("= 2+3;", fixedSource);
        }

        [Fact]
        public void L_C13_Fix_RemovesThisCall()
        {
            var source = @"
class Rsc {
    onLoad = ""_this call my_fnc"";
};";
            var diags = LintWithSource(source);
            Assert.Contains(diags, d => d.Code == "L-C13" && d.Fix != null);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.DoesNotContain("_this call", fixedSource);
            Assert.Contains("_this my_fnc", fixedSource);
        }

        [Fact]
        public void L_C14_Fix_RemovesUnusedExtern()
        {
            var source = @"
class CfgVehicles {
    class MyVehicle {};
};
class UnusedExternal;
";
            var diags = LintWithSource(source);
            Assert.Contains(diags, d => d.Code == "L-C14" && d.Fix != null);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.DoesNotContain("class UnusedExternal;", fixedSource);
        }

        [Fact]
        public void L_C05_Fix_CorrectsParentCase()
        {
            var source = @"
class MyBaseClass;
class MyClass : mybaseclass {};
";
            var diags = LintWithSource(source);
            Assert.Contains(diags, d => d.Code == "L-C05" && d.Fix != null);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.DoesNotContain(": mybaseclass", fixedSource);
            Assert.Contains(": MyBaseClass", fixedSource);
        }

        [Fact]
        public void L_C04_Fix_AddsMissingExtern()
        {
            var source = @"
class CfgVehicles {
    class MyVehicle : MissingExternal {};
};";
            var diags = LintWithSource(source);
            Assert.Contains(diags, d => d.Code == "L-C04" && d.Fix != null);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.Contains("class MissingExternal;", fixedSource);
        }

        [Fact]
        public void ApplyFixes_NoFixableDiagnostics_ReturnsUnchangedSource()
        {
            var source = @"
class CfgVehicles {
    class MyVehicle {};
};";
            var diags = LintWithSource(source);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.Equal(source, fixedSource);
        }

        // ─── Ensure clean config produces zero diagnostics ───

        [Fact]
        public void CleanConfig_NoDiagnostics()
        {
            var diags = Lint(@"
class CfgPatches {
    class MyMod {
        units[] = {""MyVehicle""};
        weapons[] = {""MyRifle""};
    };
};
class CfgVehicles {
    class MyVehicle {};
};
class CfgWeapons {
    class MyRifle {};
};
class CfgMagazines {
    class MyMag {};
};
class CfgMagazineWells {
    class MyWeapon {
        mags[] = {""MyMag""};
    };
};
class Values {
    model = ""model.p3d"";
    texture = ""tex.paa"";
    value1 = 1;
    value2 = ""hello"";
    data[] = {1, 2, 3};
};
");
            Assert.Empty(diags);
        }

        // ─── Combined preprocessor + linter diagnostics ───

        [Fact]
        public void IncludeDirective_NoPreprocessorWarnings_LinterRunsOnCombinedResult()
        {
            // Create a temp directory with two files: main includes helper
            var tmpDir = Path.Combine(Path.GetTempPath(), $"bis_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);
            try
            {
                var mainFile = Path.Combine(tmpDir, "main.cpp");
                var helperFile = Path.Combine(tmpDir, "helper.h");

                File.WriteAllText(helperFile, @"
class HelperClass {
    value = 42;
};
");
                File.WriteAllText(mainFile, $@"#include ""helper.h""
class MainClass : HelperClass {{
}};
");

                var resolver = new DefaultIncludeResolver(new[] { tmpDir });
                var parser = new ConfigParser();
                var config = parser.ParseFile(mainFile, resolver);

                // No preprocessor warnings
                Assert.NotNull(parser.PreprocessorDiagnostics);
                Assert.Empty(parser.PreprocessorDiagnostics);

                // Config linter runs on the combined result — MainClass inherits from HelperClass
                var linter = new ConfigLinter();
                var diags = linter.Lint(config);
                Assert.DoesNotContain(diags, d => d.Code == "L-C04"); // HelperClass is defined
            }
            finally
            {
                Directory.Delete(tmpDir, recursive: true);
            }
        }

        [Fact]
        public void MissingInclude_EmitsPreprocessorDiagnostic()
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), $"bis_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);
            try
            {
                var mainFile = Path.Combine(tmpDir, "main.cpp");
                File.WriteAllText(mainFile, @"#include ""nonexistent.h""
class Foo {};
");

                var resolver = new DefaultIncludeResolver(new[] { tmpDir });
                var parser = new ConfigParser();
                parser.ParseFile(mainFile, resolver);

                Assert.NotNull(parser.PreprocessorDiagnostics);
                Assert.Contains(parser.PreprocessorDiagnostics,
                    d => d.Code == "PW2" && d.Message.Contains("nonexistent.h"));
            }
            finally
            {
                Directory.Delete(tmpDir, recursive: true);
            }
        }

        [Fact]
        public void DefineMacro_AffectsParsedConfig_LinterSeesExpandedResult()
        {
            // #define references a class name — preprocessor expands it, linter checks the result
            var tmpDir = Path.Combine(Path.GetTempPath(), $"bis_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);
            try
            {
                var mainFile = Path.Combine(tmpDir, "main.cpp");
                File.WriteAllText(mainFile, @"
#define BASE SomeBase
class MyVehicle : BASE {};
");

                var resolver = new DefaultIncludeResolver(new[] { tmpDir });
                var parser = new ConfigParser();
                var config = parser.ParseFile(mainFile, resolver);

                // No preprocessor warnings (no redefine, no missing include)
                Assert.NotNull(parser.PreprocessorDiagnostics);
                Assert.Empty(parser.PreprocessorDiagnostics);

                // Linter runs on the parsed result: SomeBase is not defined → L-C04
                var linter = new ConfigLinter();
                var diags = linter.Lint(config);
                Assert.Contains(diags, d => d.Code == "L-C04" && d.Message.Contains("SomeBase"));
            }
            finally
            {
                Directory.Delete(tmpDir, recursive: true);
            }
        }

        // ─── Edge case fix tests ───

        [Fact]
        public void L_C04_Fix_MultipleMissingExterns_BothInserted()
        {
            var source = "class V { class A : Missing1 {}; class B : Missing2 {}; };";
            var diags = LintWithSource(source);
            var c04s = diags.Where(d => d.Code == "L-C04").ToList();
            Assert.Equal(2, c04s.Count);
            Assert.All(c04s, d => Assert.NotNull(d.Fix));
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.Contains("class Missing1;", fixedSource);
            Assert.Contains("class Missing2;", fixedSource);
        }

        [Fact]
        public void L_C04_LintWithoutSource_NoFixes_ApplyFixesReturnsOriginal()
        {
            var source = @"
class CfgVehicles {
    class MyVehicle : MissingExternal {};
};";
            // Lint(file) without source text → diagnostics have no Fix
            var diags = Lint(source);
            Assert.Contains(diags, d => d.Code == "L-C04");
            Assert.All(diags, d => Assert.Null(d.Fix));
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.Equal(source, fixedSource);
        }

        [Fact]
        public void L_C12_L_C13_Fix_CombinedOnSameSource()
        {
            var source = @"
class V {
    speed = '2+3';
    onLoad = ""_this call my_fnc"";
};";
            var diags = LintWithSource(source);
            Assert.Contains(diags, d => d.Code == "L-C12" && d.Fix != null);
            Assert.Contains(diags, d => d.Code == "L-C13" && d.Fix != null);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.DoesNotContain("'2+3'", fixedSource);
            Assert.Contains("= 2+3;", fixedSource);
            Assert.DoesNotContain("_this call", fixedSource);
            Assert.Contains("_this my_fnc", fixedSource);
        }

        [Fact]
        public void EmptySource_NoDiagnostics()
        {
            var diags = Lint("");
            Assert.Empty(diags);
        }

        [Fact]
        public void L_C14_Fix_RemovesExternWithIndentation()
        {
            var source = "class Used {};\n    class Unused;\nclass Another : Used {};";
            var diags = LintWithSource(source);
            Assert.Contains(diags, d => d.Code == "L-C14" && d.Fix != null);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.DoesNotContain("class Unused;", fixedSource);
            Assert.Contains("class Used {};", fixedSource);
            Assert.Contains("class Another : Used {};", fixedSource);
        }

        [Fact]
        public void L_C12_Fix_UnquotesDoubleQuotedMath()
        {
            var source = @"
class V {
    speed = ""2+3"";
};";
            var diags = LintWithSource(source);
            Assert.Contains(diags, d => d.Code == "L-C12" && d.Fix != null);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.DoesNotContain("\"2+3\"", fixedSource);
            Assert.Contains("= 2+3;", fixedSource);
        }

        [Fact]
        public void L_C05_Fix_MultipleWrongCaseBaseClasses()
        {
            var source = @"
class MyBase;
class A : mybase {};
class B : mybase {};
";
            var diags = LintWithSource(source);
            var c05s = diags.Where(d => d.Code == "L-C05").ToList();
            Assert.Equal(2, c05s.Count);
            Assert.All(c05s, d => Assert.NotNull(d.Fix));
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.DoesNotContain(": mybase", fixedSource);
            var matches = System.Text.RegularExpressions.Regex.Matches(fixedSource, ": MyBase");
            Assert.Equal(2, matches.Count);
        }

        [Fact]
        public void ApplyFixes_NonFixableDiagnostics_NoOp()
        {
            var source = @"
class CfgVehicles {
    value = 1;
    value = 2;
};";
            var diags = LintWithSource(source);
            // L-C02 duplicate property has no auto-fix
            Assert.Contains(diags, d => d.Code == "L-C02");
            Assert.All(diags, d => Assert.Null(d.Fix));
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.Equal(source, fixedSource);
        }

        [Fact]
        public void L_C12_Fix_OnlyTargetsPropertyValue_NotOtherOccurrences()
        {
            var source = @"
class V {
    speed = '2+3';
    name = '2+3';
};";
            var diags = LintWithSource(source);
            var c12 = diags.Where(d => d.Code == "L-C12").ToList();
            // 'name' is in the ignored-property list for L-C12
            Assert.Single(c12);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            // speed value unquoted
            Assert.DoesNotContain("speed = '2+3'", fixedSource);
            Assert.Contains("speed = 2+3;", fixedSource);
            // name value remains untouched
            Assert.Contains("name = '2+3'", fixedSource);
        }

        [Fact]
        public void L_C14_Fix_RemovesTrailingWhitespaceBeforeNewline()
        {
            var source = "class Used {};\nclass Unused;   \nclass Another : Used {};";
            var diags = LintWithSource(source);
            Assert.Contains(diags, d => d.Code == "L-C14" && d.Fix != null);
            var fixedSource = ConfigLinter.ApplyFixes(source, diags);
            Assert.DoesNotContain("class Unused;", fixedSource);
            Assert.Contains("class Used {};", fixedSource);
            Assert.Contains("class Another : Used {};", fixedSource);
            // Verify no double-newline artifacts
            Assert.DoesNotContain("\n\n\n", fixedSource);
        }
    }
}
