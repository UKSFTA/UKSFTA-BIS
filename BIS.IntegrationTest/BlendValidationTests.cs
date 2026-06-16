using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace BIS.IntegrationTest;

public class BlendValidationTests
{
    /// <summary>
    /// Validates all .blend files in the testdata blend directory using
    /// the BlendValidator.py Blender Python script.
    /// Skips gracefully if Blender or blend files aren't available.
    /// </summary>
    [Fact]
    public void Validate_AllBlends_Pass()
    {
        if (!TestData.CheckAvailable("blend", "Run 'bis p3d export' or 'bis pbo extract' to generate .blend files."))
            return;

        var blendDir = Path.Combine(TestData.Root!, "blend");
        if (!Directory.Exists(blendDir) || !Directory.GetFiles(blendDir, "*.blend").Any())
            return;

        string validatorScript = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "BIS.P3D", "Export", "BlendValidator.py"
        );
        validatorScript = Path.GetFullPath(validatorScript);

        if (!File.Exists(validatorScript))
        {
            // Try alternative: copy from source
            validatorScript = Path.Combine(
                Path.GetDirectoryName(AppContext.BaseDirectory) ?? ".",
                "BIS.P3D", "Export", "BlendValidator.py"
            );
            if (!File.Exists(validatorScript))
            {
                // Skip gracefully — validator script not found
                return;
            }
        }

        string blenderPath = ResolveBlenderPath();
        if (blenderPath == null)
        {
            // Skip gracefully — Blender not installed
            return;
        }

        string args = $"--background --python \"{validatorScript}\" -- \"{blendDir}\" --verbose";

        using var process = new Process();
        process.StartInfo.FileName = blenderPath;
        process.StartInfo.Arguments = args;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        bool exited = process.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds);

        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            Assert.Fail($"BlendValidator timed out after 10 minutes.\nOutput:\n{output}\nError:\n{error}");
        }

        Assert.True(process.ExitCode == 0,
            $"BlendValidator failed (exit code {process.ExitCode}).\nOutput:\n{output}\nError:\n{error}");
    }

    /// <summary>
    /// Validates blend files produced by the PBO export pipeline end-to-end.
    /// Requires a test PBO in _testdata/sources/ and Blender installed.
    /// </summary>
    [Fact]
    public void ExportAndValidate_EndToEnd_Succeeds()
    {
        // Find a small test PBO for end-to-end validation
        var sourcesDir = Path.Combine(TestData.Root ?? "", "sources");
        if (!Directory.Exists(sourcesDir))
            return;

        // Use a simple DayZ equipment PBOPBO for quick testing
        string testPbo = Directory.GetFiles(sourcesDir, ".*pbo", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(p => Path.GetFileName(p).Contains("dayz_equip", StringComparison.OrdinalIgnoreCase)
                              || Path.GetFileName(p).Contains("desert", StringComparison.OrdinalIgnoreCase));

        if (testPbo == null)
        {
            // Try any PBO under 5MB
            testPbo = Directory.GetFiles(sourcesDir, "*.*bo", SearchOption.TopDirectoryOnly)
                .Where(f => new FileInfo(f).Length < 5_000_000)
                .FirstOrDefault();
        }

        if (testPbo == null)
            return; // No test PBO available, skip

        string blenderPath = ResolveBlenderPath();
        if (blenderPath == null)
            return; // Blender not available, skip

        string tempDir = Path.Combine(Path.GetTempPath(), "bis_blend_e2e_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            // Run CLI to extract and export
            string projectDir = Path.GetFullPath(
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "BIS.CLI"));
            string cliArgs = $"run --project \"{projectDir}\" --no-build -- pbo extract \"{testPbo}\" --output \"{tempDir}\" --blender";

            using var cli = new Process();
            cli.StartInfo.FileName = "dotnet";
            cli.StartInfo.Arguments = cliArgs;
            cli.StartInfo.RedirectStandardOutput = true;
            cli.StartInfo.RedirectStandardError = true;
            cli.StartInfo.UseShellExecute = false;
            cli.StartInfo.WorkingDirectory = tempDir;

            cli.Start();
            string cliOutput = cli.StandardOutput.ReadToEnd();
            cli.WaitForExit(600_000); // 10 min max
            string cliError = cli.StandardError.ReadToEnd();

            Assert.True(cli.ExitCode == 0,
                $"CLI extract+export failed ({cli.ExitCode}).\nOutput:\n{cliOutput}\nError:\n{cliError}");

            // Now validate output blends
            string blendDir = Path.Combine(tempDir, "_blender");
            if (!Directory.Exists(blendDir) || !Directory.GetFiles(blendDir, "*.blend").Any())
            {
                Assert.Fail("No .blend files produced by export pipeline.\nOutput:\n" + cliOutput);
                return;
            }

            string validatorScript = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "BIS.P3D", "Export", "BlendValidator.py"));
            if (!File.Exists(validatorScript))
                return;

            string args = $"--background --python \"{validatorScript}\" -- \"{blendDir}\"";

            using var validate = new Process();
            validate.StartInfo.FileName = blenderPath;
            validate.StartInfo.Arguments = args;
            validate.StartInfo.RedirectStandardOutput = true;
            validate.StartInfo.RedirectStandardError = true;
            validate.StartInfo.UseShellExecute = false;
            validate.StartInfo.CreateNoWindow = true;

            validate.Start();
            string valOutput = validate.StandardOutput.ReadToEnd();
            string valError = validate.StandardError.ReadToEnd();
            bool valExited = validate.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds);

            if (!valExited)
            {
                try { validate.Kill(entireProcessTree: true); } catch { }
                Assert.Fail($"BlendValidator timed out.\nOutput:\n{valOutput}\nError:\n{valError}");
            }

            Assert.True(validate.ExitCode == 0,
                $"BlendValidator failed (exit {validate.ExitCode}).\nOutput:\n{valOutput}\nError:\n{valError}");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static string? ResolveBlenderPath()
    {
        var candidates = new[]
        {
            "/usr/bin/blender",
            "/usr/local/bin/blender",
            "/snap/bin/blender",
            "/opt/blender/blender",
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
