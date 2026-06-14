using System.Diagnostics;
using Xunit;

namespace BIS.CLI.Test;

public class CliTest : IDisposable
{
    private readonly string _tempDir;

    public CliTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bis_cli_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string RunCli(string args, int expectedExitCode = 0)
    {
        var projectDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "BIS.CLI"));
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectDir}\" --no-build -- {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _tempDir,
        };
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        proc.WaitForExit(15000);

        if (proc.ExitCode != expectedExitCode)
        {
            Assert.Fail($"Expected exit code {expectedExitCode}, got {proc.ExitCode}.\nStdout:\n{output}\nStderr:\n{error}");
        }
        return output;
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // ─── SQF lint ──────────────────────────────────────────────────

    [Fact]
    public void LintSqf_CleanFile_NoIssues()
    {
        WriteFile("test.sqf", "hint \"hello\";\n");
        var output = RunCli($"lint sqf {_tempDir}");
        Assert.Contains("no issues found", output);
    }

    [Fact]
    public void LintSqf_DirtyFile_ReportsIssue()
    {
        WriteFile("test.sqf", "Hint \"hello\";\n");
        var output = RunCli($"lint sqf {_tempDir} --exit-code", expectedExitCode: 1);
        Assert.Contains("L-S04", output);
    }

    [Fact]
    public void LintSqf_JsonOutput_ValidJson()
    {
        WriteFile("test.sqf", "Hint \"hello\";\n");
        var output = RunCli($"lint sqf {_tempDir} --json");
        Assert.Contains("L-S04", output);
        Assert.Contains("\"code\"", output);
    }

    // ─── SQF fix ──────────────────────────────────────────────────

    [Fact]
    public void LintSqf_FixFlag_FixesIssues()
    {
        WriteFile("test.sqf", "Hint \"hello\";\n");
        var output = RunCli($"lint sqf {_tempDir} --fix");
        Assert.Contains("auto-fixed", output);
        // File should now be fixed
        var fixedContent = File.ReadAllText(Path.Combine(_tempDir, "test.sqf"));
        Assert.Contains("hint \"hello\"", fixedContent);
    }

    // ─── SQF format ───────────────────────────────────────────────

    [Fact]
    public void FmtSqf_CheckMode_ReportsUnformatted()
    {
        WriteFile("test.sqf", "_x = 1+2;\n");
        var output = RunCli($"fmt sqf {_tempDir} --check", expectedExitCode: 1);
        Assert.Contains("would reformat", output);
    }

    [Fact]
    public void FmtSqf_FormatsInPlace()
    {
        WriteFile("test.sqf", "_x = 1+2;\n");
        var output = RunCli($"fmt sqf {_tempDir}", expectedExitCode: 1);
        var formatted = File.ReadAllText(Path.Combine(_tempDir, "test.sqf"));
        Assert.Contains("1 + 2", formatted);
    }

    [Fact]
    public void FmtSqf_CleanFile_NoOutput()
    {
        WriteFile("test.sqf", "_x = 1 + 2;\n");
        var output = RunCli($"fmt sqf {_tempDir} --check");
        Assert.Contains("0 would reformat", output);
    }

    // ─── Config lint ──────────────────────────────────────────────

    [Fact]
    public void LintConfig_CleanFile_NoIssues()
    {
        WriteFile("test.cpp", "class Test {};\n");
        var output = RunCli($"lint config {_tempDir}");
        Assert.Contains("no issues found", output);
    }

    // ─── PBO operations ──────────────────────────────────────────

    [Fact]
    public void PboPack_List_Roundtrip()
    {
        WriteFile("test.txt", "hello world\n");
        var pboPath = Path.Combine(_tempDir, "test.pbo");

        var packOutput = RunCli($"pbo pack {_tempDir} -o {pboPath}");
        Assert.Contains("Packed", packOutput);

        // List it back
        var listOutput = RunCli($"pbo list {pboPath}");
        Assert.Contains("test.txt", listOutput);
    }

    // ─── Config serialize ─────────────────────────────────────────

    [Fact]
    public void ConfigSerialize_Roundtrip()
    {
        WriteFile("input.cpp", "class Test { value = 42; };\n");
        var outPath = Path.Combine(_tempDir, "output.txt");
        var output = RunCli($"config serialize {Path.Combine(_tempDir, "input.cpp")} -o {outPath}");
        Assert.True(File.Exists(outPath));
        var content = File.ReadAllText(outPath);
        Assert.Contains("value", content);
    }

    // ─── Stringtable lint ─────────────────────────────────────────

    [Fact]
    public void LintStringtable_UnsortedKeys_ReportsWarning()
    {
        WriteFile("stringtable.xml", @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project name=""Test"">
  <Package name=""Test"">
    <Key ID=""STR_B""><Original>B</Original></Key>
    <Key ID=""STR_A""><Original>A</Original></Key>
  </Package>
</Project>");
        var output = RunCli($"lint stringtable {_tempDir} --exit-code", expectedExitCode: 1);
        Assert.Contains("L-L01", output);
    }

    // ─── Preprocessor lint ─────────────────────────────────────────

    [Fact]
    public void LintPreprocessor_MissingInclude_ReportsWarning()
    {
        WriteFile("test.cpp", "#include \"missing.hpp\"\nclass Test {};\n");
        var output = RunCli($"lint preprocessor {_tempDir} --exit-code", expectedExitCode: 1);
        Assert.Contains("PW2", output);
    }

    // ─── PBO lint ─────────────────────────────────────────────────

    [Fact]
    public void LintPbo_ValidPbo_NoIssues()
    {
        WriteFile("test.txt", "hello\n");
        var pboPath = Path.Combine(_tempDir, "test.pbo");
        RunCli($"pbo pack {_tempDir} -o {pboPath} -p test");
        var output = RunCli($"lint pbo {pboPath}");
        Assert.Contains("no issues", output);
    }

    // ─── P3D error cases ─────────────────────────────────────────

    [Fact]
    public void P3dInfo_NonexistentFile_ReportsError()
    {
        var output = RunCli("p3d info /nonexistent.p3d", expectedExitCode: 1);
        Assert.Contains("Description", output);
    }

    [Fact]
    public void P3dValidate_NonexistentFile_ReportsError()
    {
        var output = RunCli("p3d validate /nonexistent.p3d", expectedExitCode: 1);
        Assert.Contains("Description", output);
    }

    // ─── PAA error cases ─────────────────────────────────────────

    [Fact]
    public void PaaAnalyze_NonexistentFile_ReportsError()
    {
        var output = RunCli("paa analyze /nonexistent.paa", expectedExitCode: 1);
        Assert.Contains("Description", output);
    }

    [Fact]
    public void PaaSuggest_NonexistentFile_ReportsError()
    {
        var output = RunCli("paa suggest /nonexistent.paa", expectedExitCode: 1);
        Assert.Contains("Description", output);
    }

    [Fact]
    public void Pipeline_PboPack_LintSqf_FmtSqf_List()
    {
        WriteFile("scripts/test.sqf", "Hint \"hello\";\n_x = 1+2;\n");
        WriteFile("scripts/config.cpp", "class Test {};\n");

        var pboPath = Path.Combine(_tempDir, "test.pbo");
        var packOut = RunCli($"pbo pack {Path.Combine(_tempDir, "scripts")} -o {pboPath} -p test_mod");
        Assert.Contains("Packed", packOut);

        var listOut = RunCli($"pbo list {pboPath}");
        Assert.Contains("test.sqf", listOut);
        Assert.Contains("config.cpp", listOut);

        var extractDir = Path.Combine(_tempDir, "extracted");
        var pboPathFull = Path.GetFullPath(pboPath);
        var runDir = Path.GetDirectoryName(pboPathFull)!;

        Directory.CreateDirectory(extractDir);
        var psiExtract = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "BIS.CLI"))}\" --no-build -- pbo extract {pboPathFull} -o {extractDir}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = runDir,
        };
        using var procExtract = Process.Start(psiExtract)!;
        procExtract.WaitForExit(15000);
        Assert.True(File.Exists(Path.Combine(extractDir, "test.sqf")));

        var psiLint = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "BIS.CLI"))}\" --no-build -- lint sqf {extractDir} --exit-code",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = runDir,
        };
        using var procLint = Process.Start(psiLint)!;
        var lintOut = procLint.StandardOutput.ReadToEnd();
        procLint.WaitForExit(15000);
        Assert.Contains("L-S04", lintOut);

        var psiFmt = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "BIS.CLI"))}\" --no-build -- fmt sqf {extractDir}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = runDir,
        };
        using var procFmt = Process.Start(psiFmt)!;
        var fmtOut = procFmt.StandardOutput.ReadToEnd();
        procFmt.WaitForExit(15000);
        Assert.Contains("reformatted", fmtOut);

        var formattedContent = File.ReadAllText(Path.Combine(extractDir, "test.sqf"));
        Assert.Contains("1 + 2", formattedContent);
    }
}
