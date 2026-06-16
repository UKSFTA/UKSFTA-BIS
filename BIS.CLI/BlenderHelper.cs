using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIS.P3D.Export;

internal static class BlenderHelper
{
    /// <summary>
    /// Generates batch Blender scripts and runs them concurrently.
    /// Each batch script imports multiple models sequentially in one Blender process.
    /// </summary>
    public static async Task<int> ExportAsync(string extractedDir, string outputDir, string texturesDir = null)
    {
        // Generate batch scripts (each handles multiple models in a single Blender session)
        int cpuCount = Environment.ProcessorCount;
        int concurrency = Math.Clamp(cpuCount / 2, 2, 4); // 2-4 concurrent Blender processes
        var batchScripts = BlenderExport.GenerateAllBatchScripts(extractedDir, outputDir, texturesDir, concurrency);

        if (batchScripts.Count == 0)
        {
            Console.WriteLine("  No batch scripts generated. Skipping Blender export.");
            return 0;
        }

        // Run each batch script as one Blender process, up to concurrency at a time
        int completed = 0, successCount = 0;
        var lockObj = new object();
        var semaphore = new SemaphoreSlim(concurrency);

        var tasks = batchScripts.Select(async script =>
        {
            await semaphore.WaitAsync();
            try
            {
                bool ok = await RunBatchImportAsync(script, outputDir);
                lock (lockObj)
                {
                    completed++;
                    if (ok) successCount++;
                    var status = ok ? "OK" : "FAILED";
                    Console.WriteLine($"  [{completed}/{batchScripts.Count}] {status} {Path.GetFileName(script)}");
                }
                return ok;
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine($"  Blender export complete: {successCount}/{batchScripts.Count} batch(s) exported to {outputDir}");
        return successCount;
    }

    /// <summary>
    /// Derives the texture scan root from a P3D path.
    /// If the P3D is in a "model" subdirectory, goes up one level (common PBO extraction layout).
    /// Otherwise uses the P3D's parent directory.
    /// </summary>
    internal static string DeriveExtractRoot(string p3dPath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(p3dPath));
        if (dir != null && string.Equals(Path.GetFileName(dir), "model", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Path.GetDirectoryName(dir);
            if (parent != null) return parent;
        }
        return dir ?? ".";
    }

    /// <summary>
    /// Exports a single .p3d file to .blend format.
    /// </summary>
    public static async Task<bool> ExportSingleAsync(string p3dPath, string outputDir)
    {
        string extractRoot = DeriveExtractRoot(p3dPath);
        string texturesDir = Path.Combine(outputDir, "_textures");

        Directory.CreateDirectory(texturesDir);

        // Convert PAAs to PNGs
        int texCount = BlenderExport.ExportAll(extractRoot, texturesDir);
        Console.WriteLine($"  Converted {texCount} texture(s) from {extractRoot}");

        // Generate single-model batch script
        string scriptPath = BlenderExport.GenerateSingleModelScript(p3dPath, extractRoot, outputDir, texturesDir);
        Console.WriteLine($"  Generated script: {Path.GetFileName(scriptPath)}");

        // Run Blender
        return await RunBatchImportAsync(scriptPath, outputDir);
    }

    private static string? ResolveBlenderPath()
    {
        var fallbackPaths = new[]
        {
            "/usr/bin/blender",
            "/usr/local/bin/blender",
            "/snap/bin/blender",
            "/opt/blender/blender",
        };
        return fallbackPaths.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Runs a single batch import script (which handles multiple models) in one Blender process.
    /// </summary>
    private static async Task<bool> RunBatchImportAsync(string scriptPath, string workingDir)
    {
        string? blenderPath = ResolveBlenderPath();
        if (blenderPath == null)
        {
            Console.WriteLine("  Blender executable not found. Install Blender.");
            Console.WriteLine($"  Run manually: blender --background --python \"{scriptPath}\"");
            return false;
        }

        string args = $"--background --python \"{scriptPath}\"";
        int timeoutMinutes = 20;

        using var process = new Process();
        process.StartInfo.FileName = blenderPath;
        process.StartInfo.Arguments = args;
        process.StartInfo.WorkingDirectory = workingDir;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        try
        {
            process.Start();

            // Wait for exit with timeout
            bool exited = process.WaitForExit((int)TimeSpan.FromMinutes(timeoutMinutes).TotalMilliseconds);

            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                Console.WriteLine($"  [{Path.GetFileNameWithoutExtension(scriptPath)}] timed out after {timeoutMinutes} minutes");
                return false;
            }

            bool success = process.ExitCode == 0;
            if (success)
            {
                try { File.Delete(scriptPath); }
                catch { /* best-effort cleanup */ }
            }
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [{Path.GetFileNameWithoutExtension(scriptPath)}] failed: {ex.Message}");
            return false;
        }
    }
}
