using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using BIS.PAA;
using BIS.PBO;
using BIS.PBO.Deobfuscator;

namespace PboTool
{
    class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<
                    ListOptions, AnalyzeOptions, FixOptions,
                    CreateOptions, ExtractOptions, BatchOptions>(args)
                .MapResult(
                    (ListOptions opts) => RunList(opts),
                    (AnalyzeOptions opts) => RunAnalyze(opts),
                    (FixOptions opts) => RunFix(opts),
                    (CreateOptions opts) => RunCreate(opts),
                    (ExtractOptions opts) => RunExtract(opts),
                    (BatchOptions opts) => RunBatch(opts),
                    _ => 1);
        }

        // ── Verbs ──

        [Verb("list", HelpText = "List contents of a PBO")]
        class ListOptions
        {
            [Value(0, Required = true, HelpText = "Input PBO file")]
            public string Input { get; set; }

            [Option('r', "recovered", HelpText = "Show recovered filenames (runs deobfuscation)")]
            public bool Recovered { get; set; }
        }

        [Verb("analyze", HelpText = "Detect obfuscation patterns in a PBO")]
        class AnalyzeOptions
        {
            [Value(0, Required = true, HelpText = "Input PBO file")]
            public string Input { get; set; }
        }

        [Verb("fix", HelpText = "Deobfuscate and rebuild a clean PBO")]
        class FixOptions
        {
            [Value(0, Required = true, HelpText = "Input PBO file")]
            public string Input { get; set; }

            [Value(1, Required = false, HelpText = "Output PBO file (default: input_fixed.pbo)")]
            public string Output { get; set; }

            [Option('l', "list", HelpText = "Show recovered names during fix")]
            public bool List { get; set; }
        }

        [Verb("create", HelpText = "Create a PBO from a directory")]
        class CreateOptions
        {
            [Value(0, Required = true, HelpText = "Source directory")]
            public string Source { get; set; }

            [Value(1, Required = true, HelpText = "Output PBO file")]
            public string Output { get; set; }

            [Option('p', "prefix", HelpText = "PBO prefix (default: directory name)")]
            public string Prefix { get; set; }
        }

        [Verb("extract", HelpText = "Extract a PBO to a directory")]
        class ExtractOptions
        {
            [Value(0, Required = true, HelpText = "Input PBO file")]
            public string Input { get; set; }

            [Value(1, Required = false, HelpText = "Output directory (default: PBO name without extension)")]
            public string Output { get; set; }
        }

        [Verb("batch", HelpText = "Process all PBOs in a directory")]
        class BatchOptions
        {
            [Value(0, Required = true, HelpText = "Directory containing PBO files")]
            public string Directory { get; set; }

            [Option('o', "out", Required = true, HelpText = "Output directory for fixed PBOs")]
            public string Output { get; set; }

            [Option("analyze-only", HelpText = "Only analyze, don't fix")]
            public bool AnalyzeOnly { get; set; }
        }

        // ── Commands ──

        static int RunList(ListOptions opts)
        {
            if (!File.Exists(opts.Input))
            {
                Console.Error.WriteLine($"File not found: {opts.Input}");
                return 1;
            }
            using var pbo = new PBO(opts.Input);
            Console.WriteLine($"Prefix: {pbo.Prefix ?? "(none)"}");
            Console.WriteLine($"Files: {pbo.Files.Count}");

            if (opts.Recovered)
            {
                var deobf = new PboDeobfuscator();
                var result = QuietProcess(deobf, pbo);
                Console.WriteLine($"\nRecovered names:");
                for (int i = 0; i < pbo.Files.Count; i++)
                {
                    var entry = pbo.Files[i];
                    var name = result.RecoveredNames.TryGetValue(i, out var r)
                        ? r
                        : entry.FileName;
                    var marker = result.RecoveredNames.ContainsKey(i) ? " ✓" : "";

                    string texType = "";
                    if (name.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var stream = entry.OpenRead();
                            var info = PaaAnalyzer.Analyze(stream);
                            texType = $"  [{info.CategoryLabel}]";
                        }
                        catch { texType = "  [?]"; }
                    }

                    Console.WriteLine($"  {i,4}: {name}  ({entry.Size,8} bytes){texType}{marker}");
                }
            }
            else
            {
                foreach (var f in pbo.Files)
                    Console.WriteLine($"  {f.FileName}  ({f.Size} bytes)");
            }
            return 0;
        }

        static int RunAnalyze(AnalyzeOptions opts)
        {
            if (!File.Exists(opts.Input))
            {
                Console.Error.WriteLine($"File not found: {opts.Input}");
                return 1;
            }
            using var pbo = new PBO(opts.Input);
            Console.WriteLine($"Prefix: {pbo.Prefix ?? "(none)"}");
            Console.WriteLine($"Files: {pbo.Files.Count}");

            Console.WriteLine($"Properties:");
            foreach (var (k, v) in pbo.PropertiesPairs)
                Console.WriteLine($"  {k} = {v}");

            var deobf = new PboDeobfuscator();
            var result = QuietProcess(deobf, pbo);

            if (result.IsObfuscated)
            {
                Console.WriteLine($"\nObfuscation detected: {result.MatchedProfile}");
                foreach (var (k, v) in result.Stats)
                    Console.WriteLine($"  {k}: {v}");
                if (result.FilteredOut.Count > 0)
                    Console.WriteLine($"  Entries to discard: {result.FilteredOut.Count}");

                if (result.RecoveredNames.Count > 0)
                {
                    Console.WriteLine($"\nRecovered filenames ({result.RecoveredNames.Count} entries):");
                    foreach (var kvp in result.RecoveredNames)
                    {
                        var entry = pbo.Files[kvp.Key];
                        var original = entry.FileName;
                        string texType = "";
                        if (kvp.Value.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using var stream = entry.OpenRead();
                                var info = PaaAnalyzer.Analyze(stream);
                                texType = $"  [{info.CategoryLabel}]";
                            }
                            catch { texType = "  [?]"; }
                        }
                        Console.WriteLine($"  [{kvp.Key,4}] {original}  →  {kvp.Value}{texType}");
                    }
                }
            }
            else
            {
                Console.WriteLine("\nNo obfuscation detected. PBO appears clean.");
            }
            return 0;
        }

        static int RunFix(FixOptions opts)
        {
            if (!File.Exists(opts.Input))
            {
                Console.Error.WriteLine($"File not found: {opts.Input}");
                return 1;
            }
            var output = opts.Output ?? Path.ChangeExtension(opts.Input, "_fixed.pbo");
            if (!output.EndsWith(".pbo", StringComparison.OrdinalIgnoreCase))
                output += ".pbo";

            using var pbo = new PBO(opts.Input);
            var deobf = new PboDeobfuscator();
            var result = deobf.Process(pbo);

            if (!result.IsObfuscated)
            {
                Console.WriteLine("No obfuscation detected, copying as-is.");
                File.Copy(opts.Input, output, true);
                Console.WriteLine($"Output: {output}");
                return 0;
            }

            Console.WriteLine($"\n--- {result.MatchedProfile} ---");
            foreach (var (k, v) in result.Stats)
                Console.WriteLine($"  {k}: {v}");

            if (opts.List)
            {
                for (int i = 0; i < pbo.Files.Count; i++)
                {
                    var name = result.RecoveredNames.TryGetValue(i, out var r)
                        ? r
                        : pbo.Files[i].FileName;
                    Console.WriteLine($"  {i,4}: {name}  ({pbo.Files[i].Size} bytes)");
                }
            }

            deobf.Rebuild(pbo, result, output);
            return 0;
        }

        static int RunCreate(CreateOptions opts)
        {
            if (!Directory.Exists(opts.Source))
            {
                Console.Error.WriteLine($"Directory not found: {opts.Source}");
                return 1;
            }

            var prefix = opts.Prefix ?? new DirectoryInfo(opts.Source).Name;
            var files = Directory.GetFiles(opts.Source, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                Console.Error.WriteLine("Source directory is empty.");
                return 1;
            }

            using var pbo = new PBO();
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", prefix));

            foreach (var fp in files)
            {
                var rel = Path.GetRelativePath(opts.Source, fp).Replace('\\', '/');
                pbo.Files.Add(new PBOFileToAdd(new FileInfo(fp), rel));
            }

            pbo.SaveTo(opts.Output);
            Console.WriteLine($"Created: {opts.Output} ({pbo.Files.Count} files)");
            return 0;
        }

        static int RunExtract(ExtractOptions opts)
        {
            if (!File.Exists(opts.Input))
            {
                Console.Error.WriteLine($"File not found: {opts.Input}");
                return 1;
            }

            var dir = opts.Output ?? Path.GetFileNameWithoutExtension(opts.Input);
            Directory.CreateDirectory(dir);

            using var pbo = new PBO(opts.Input);
            pbo.ExtractFiles(pbo.Files, dir);
            Console.WriteLine($"Extracted {pbo.Files.Count} files to {dir}");
            return 0;
        }

        static int RunBatch(BatchOptions opts)
        {
            if (!Directory.Exists(opts.Directory))
            {
                Console.Error.WriteLine($"Directory not found: {opts.Directory}");
                return 1;
            }
            Directory.CreateDirectory(opts.Output);

            var pbos = Directory.GetFiles(opts.Directory, "*.pbo", SearchOption.AllDirectories);
            Console.WriteLine($"Found {pbos.Length} PBO files in {opts.Directory}");

            int fixed_ = 0, skipped = 0, errors = 0;
            foreach (var pboPath in pbos)
            {
                var rel = Path.GetRelativePath(opts.Directory, pboPath);
                var outPath = Path.Combine(opts.Output, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                try
                {
                    using var pbo = new PBO(pboPath);
                    var deobf = new PboDeobfuscator();
                    var result = deobf.Process(pbo);

                    if (!result.IsObfuscated)
                    {
                        if (opts.AnalyzeOnly)
                            Console.WriteLine($"[CLEAN] {rel}");
                        else
                        {
                            File.Copy(pboPath, outPath, true);
                            Console.WriteLine($"[COPY] {rel}");
                        }
                        skipped++;
                        continue;
                    }

                    if (opts.AnalyzeOnly)
                    {
                        Console.WriteLine($"[DIRTY] {rel} → {result.MatchedProfile}");
                        foreach (var (k, v) in result.Stats)
                            Console.WriteLine($"         {k}: {v}");
                    }
                    else
                    {
                        deobf.Rebuild(pbo, result, outPath);
                        Console.WriteLine($"[FIXED] {rel}");
                    }
                    fixed_++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] {rel}: {ex.Message}");
                    errors++;
                }
            }

            Console.WriteLine($"\nDone: {fixed_} fixed, {skipped} clean/skipped, {errors} errors");
            return errors > 0 ? 1 : 0;
        }

        static DeobfuscationResult QuietProcess(PboDeobfuscator deobf, PBO pbo)
        {
            var original = Console.Out;
            Console.SetOut(TextWriter.Null);
            try
            {
                return deobf.Process(pbo);
            }
            finally
            {
                Console.SetOut(original);
            }
        }
    }
}
