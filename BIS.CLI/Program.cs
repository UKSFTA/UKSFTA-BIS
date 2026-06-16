using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using BIS.Core.Config;
using BIS.P3D;
using BIS.P3D.Conversion;
using BIS.P3D.Export;
using BIS.PBO.Deobfuscator.Format;
using BIS.SQF;
using BIS.Stringtable;
using BIS.PBO;
using BIS.PBO.Deobfuscator;
using BIS.PBO.Deobfuscator.Profiles.Specialized;
using Spectre.Console;

var root = new RootCommand("BIS file format CLI tool")
{
    new Command("p3d", "P3D model operations")
    {
        new Command("validate", "Validate a P3D file")
        {
            new Argument<FileInfo>("path", "Path to .p3d file").ExistingOnly(),
        },
        new Command("info", "Show P3D file information")
        {
            new Argument<FileInfo>("path", "Path to .p3d file").ExistingOnly(),
        },
        new Command("convert", "Convert P3D between ODOL and MLOD")
        {
            new Argument<FileInfo>("path", "Path to .p3d file").ExistingOnly(),
            new Option<FileInfo?>(["--output", "-o"], "Output path"),
        },
        new Command("roundtrip", "Round-trip to verify conversion")
        {
            new Argument<FileInfo>("path", "Path to .p3d file").ExistingOnly(),
        },
        new Command("export", "Export a P3D model to Blender .blend format")
        {
            new Argument<FileInfo>("path", "Path to .p3d file").ExistingOnly(),
            new Option<DirectoryInfo?>(["--output", "-o"], "Output directory for .blend file"),
        },
    },
    new Command("paa", "PAA texture operations")
    {
        new Command("analyze", "Analyze a PAA file")
        {
            new Argument<FileInfo>("path", "Path to .paa file").ExistingOnly(),
        },
        new Command("suggest", "Suggest optimal PAA format")
        {
            new Argument<FileInfo>("path", "Path to .paa file").ExistingOnly(),
        },
    },
    new Command("pbo", "PBO archive operations")
    {
        new Command("list", "List PBO contents")
        {
            new Argument<FileInfo>("path", "Path to .pbo file").ExistingOnly(),
            new Option<bool>(["--raw", "-r"], "Show original (un-deobfuscated) filenames"),
        },
        new Command("extract", "Extract PBO contents")
        {
            new Argument<FileInfo>("path", "Path to .pbo file").ExistingOnly(),
            new Option<DirectoryInfo?>(["--output-dir", "-o"], "Output directory"),
            new Option<bool>(["--raw", "-r"], "Extract with original (un-deobfuscated) filenames"),
            new Option<bool>(["--match-textures", "-m"], "After extraction, match orphan .paa files against known textures by pixel content"),
            new Option<bool>(["--fuzzy-match", "-fm"], () => true, "After extraction and exact-match, match orphan .paa files against known textures by structural similarity (same layout, different colors) [default: on]"),
            new Option<bool>(["--export-blender", "-b"], "Export models as Blender scripts with PNG textures alongside extracted files"),
        },
        new Command("pack", "Pack a directory into a PBO file")
        {
            new Argument<DirectoryInfo>("path", "Source directory to pack").ExistingOnly(),
            new Option<FileInfo?>(["--output", "-o"], "Output .pbo path"),
            new Option<string?>(["--prefix", "-p"], () => null, "Prefix property (defaults to directory name)"),
            new Option<bool>(["--compress", "-c"], "Enable LZSS compression for files >= 1024 bytes"),
        },
    },
    new Command("config", "Config file operations")
    {
        new Command("serialize", "Serialize a config .cpp file to standard format")
        {
            new Argument<FileInfo>("path", "Path to .cpp config file").ExistingOnly(),
            new Option<FileInfo?>(["--output", "-o"], "Output file path") { IsRequired = true },
        },
    },
    new Command("lint", "Lint and diagnostic operations")
    {
        new Command("config", "Lint config files (.cpp/.hpp) — accepts files or directories")
        {
            new Argument<string>("path", "Path to config file or directory").LegalFilePathsOnly(),
            new Option<bool>(["--json", "-j"], "Output as JSON"),
            new Option<bool>(["--exit-code", "-e"], "Exit 1 if any issues found (CI mode)"),
            new Option<bool>(["--fix"], "Apply auto-fixes to fixable issues"),
            new Option<string[]>(["--search-dirs", "-s"], "Additional include search directories"),
        },
        new Command("stringtable", "Lint stringtable XML files — accepts files or directories")
        {
            new Argument<string>("path", "Path to stringtable.xml or directory").LegalFilePathsOnly(),
            new Option<bool>(["--json", "-j"], "Output as JSON"),
            new Option<bool>(["--exit-code", "-e"], "Exit 1 if any issues found (CI mode)"),
        },
        new Command("preprocessor", "Preprocess and show preprocessor warnings")
        {
            new Argument<string>("path", "Path to config file or directory").LegalFilePathsOnly(),
            new Option<bool>(["--json", "-j"], "Output as JSON"),
            new Option<bool>(["--exit-code", "-e"], "Exit 1 if any issues found (CI mode)"),
        },
        new Command("sqf", "Lint SQF script files — accepts files or directories")
        {
            new Argument<string>("path", "Path to .sqf file or directory").LegalFilePathsOnly(),
            new Option<bool>(["--json", "-j"], "Output as JSON"),
            new Option<bool>(["--exit-code", "-e"], "Exit 1 if any issues found (CI mode)"),
            new Option<bool>(["--fix"], "Apply auto-fixes to fixable issues (tab→spaces, command case, etc.)"),
        },
        new Command("pbo", "Lint PBO archive files — accepts files or directories")
        {
            new Argument<string>("path", "Path to .pbo file or directory").LegalFilePathsOnly(),
            new Option<bool>(["--json", "-j"], "Output as JSON"),
            new Option<bool>(["--exit-code", "-e"], "Exit 1 if any issues found (CI mode)"),
        },
        },
    new Command("fmt", "Format SQF source files")
    {
        new Command("sqf", "Format SQF script files — accepts files or directories")
        {
            new Argument<string>("path", "Path to .sqf file or directory").LegalFilePathsOnly(),
            new Option<bool>(["--check", "-c"], "Check mode: exit 1 if any file would change (no writes)"),
        },
    },
};

foreach (var cmd in root.Children.OfType<Command>())
{
    foreach (var sub in cmd.Children.OfType<Command>())
    {
        sub.SetHandler(ctx =>
        {
            try
            {
                var parse = ctx.ParseResult;
                var name = $"{cmd.Name} {sub.Name}";
                switch (name)
                {
                    case "p3d validate":     HandleP3DValidate(GetFileArg(sub.Arguments.First(), parse).FullName); break;
                    case "p3d info":         HandleP3DInfo(GetFileArg(sub.Arguments.First(), parse).FullName); break;
                    case "p3d convert":      HandleP3DConvert(GetFileArg(sub.Arguments.First(), parse).FullName, GetOptVal<FileInfo?>(sub, parse, "--output")); break;
                    case "p3d roundtrip":    HandleP3DRoundtrip(GetFileArg(sub.Arguments.First(), parse).FullName); break;
                    case "p3d export":       HandleP3DExport(GetFileArg(sub.Arguments.First(), parse).FullName, GetOptVal<DirectoryInfo?>(sub, parse, "--output")); break;
                    case "paa analyze":      HandlePAAAnalyze(GetFileArg(sub.Arguments.First(), parse).FullName); break;
                    case "paa suggest":      HandlePAASuggest(GetFileArg(sub.Arguments.First(), parse).FullName); break;
                    case "pbo list":         HandlePBOList(GetFileArg(sub.Arguments.First(), parse).FullName, GetOptVal<bool>(sub, parse, "--raw")); break;
                    case "pbo extract":      HandlePBOExtract(GetFileArg(sub.Arguments.First(), parse).FullName, GetOptVal<DirectoryInfo?>(sub, parse, "--output-dir"), GetOptVal<bool>(sub, parse, "--raw"), GetOptVal<bool>(sub, parse, "--match-textures"), GetOptVal<bool>(sub, parse, "--export-blender"), GetOptVal<bool>(sub, parse, "--fuzzy-match")); break;
                    case "pbo pack":         HandlePBOPack(GetDirArg(sub.Arguments.First(), parse).FullName, GetOptVal<FileInfo>(sub, parse, "--output"), GetOptVal<string?>(sub, parse, "--prefix"), GetOptVal<bool>(sub, parse, "--compress")); break;
                    case "config serialize": HandleConfigSerialize(GetFileArg(sub.Arguments.First(), parse).FullName, GetOptVal<FileInfo>(sub, parse, "--output")); break;
                    case "lint config":
                        ctx.ExitCode = LintConfigBatch(GetPathArg(sub.Arguments.First(), parse),
                            GetOptVal<bool>(sub, parse, "--json"),
                            GetOptVal<bool>(sub, parse, "--exit-code"),
                            GetOptVal<bool>(sub, parse, "--fix"),
                            GetOptVal<string[]>(sub, parse, "--search-dirs"));
                        break;
                    case "lint stringtable":
                        ctx.ExitCode = LintStringtableBatch(GetPathArg(sub.Arguments.First(), parse),
                            GetOptVal<bool>(sub, parse, "--json"),
                            GetOptVal<bool>(sub, parse, "--exit-code"));
                        break;
                    case "lint preprocessor":
                        ctx.ExitCode = LintPreprocessorBatch(GetPathArg(sub.Arguments.First(), parse),
                            GetOptVal<bool>(sub, parse, "--json"),
                            GetOptVal<bool>(sub, parse, "--exit-code"));
                        break;
                    case "lint sqf":
                        ctx.ExitCode = LintSqfBatch(GetPathArg(sub.Arguments.First(), parse),
                            GetOptVal<bool>(sub, parse, "--json"),
                            GetOptVal<bool>(sub, parse, "--exit-code"),
                            GetOptVal<bool>(sub, parse, "--fix"));
                        break;
                    case "lint pbo":
                        ctx.ExitCode = LintPboBatch(GetPathArg(sub.Arguments.First(), parse),
                            GetOptVal<bool>(sub, parse, "--json"),
                            GetOptVal<bool>(sub, parse, "--exit-code"));
                        break;
                    case "fmt sqf":
                        ctx.ExitCode = HandleFmtSqf(GetPathArg(sub.Arguments.First(), parse),
                            GetOptVal<bool>(sub, parse, "--check"));
                        break;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                ctx.ExitCode = 1;
            }
        });
    }
}

return await root.InvokeAsync(args);

// ─── Argument/option helpers ───

static FileInfo GetFileArg(Argument arg, ParseResult parse)
{
    return parse.GetValueForArgument((Argument<FileInfo>)arg)!;
}

static DirectoryInfo GetDirArg(Argument arg, ParseResult parse)
{
    return parse.GetValueForArgument((Argument<DirectoryInfo>)arg)!;
}

static string GetPathArg(Argument arg, ParseResult parse)
{
    return parse.GetValueForArgument((Argument<string>)arg)!;
}

static T GetOptVal<T>(Command cmd, ParseResult parse, string alias)
{
    var opt = cmd.Options.OfType<Option<T>>().First(o => o.Aliases.Contains(alias));
    return parse.GetValueForOption(opt)!;
}

// ─── Batch lint helpers ───

static string[] DiscoverFiles(string path, string pattern)
{
    if (File.Exists(path))
        return [path];
    if (Directory.Exists(path))
        return Directory.GetFiles(path, pattern, SearchOption.AllDirectories);
    AnsiConsole.MarkupLine($"[red]Error: '{path}' is not a file or directory[/]");
    return [];
}

static void PrintSummary(string label, int fileCount, int issueCount, long elapsedMs)
{
    var time = elapsedMs > 0 ? $" in {elapsedMs / 1000.0:F1}s" : "";
    var prefix = issueCount > 0 ? "\n" : "";
    if (issueCount == 0)
        AnsiConsole.MarkupLine($"{prefix}[green]{fileCount} {label} file{(fileCount == 1 ? "" : "s")} linted, no issues found{time}.[/]");
    else
        AnsiConsole.MarkupLine($"{prefix}[red]{fileCount} {label} file{(fileCount == 1 ? "" : "s")} linted, {issueCount} issue{(issueCount == 1 ? "" : "s")} found{time}.[/]");
}

// ─── Batch handlers ───

static int LintConfigBatch(string path, bool json, bool exitCode, bool fix, string[]? searchDirs)
{
    var files = DiscoverFiles(path, "*.cpp")
        .Concat(DiscoverFiles(path, "*.hpp"))
        .Concat(DiscoverFiles(path, "*.ext"))
        .Distinct()
        .ToArray();
    if (files.Length == 0) return exitCode ? 1 : 0;

    var allDiagnostics = new List<object>();
    var tableRows = new List<(string Code, string Severity, string Color, string Message, string File, string Location)>();
    var sw = Stopwatch.StartNew();
    var fixedCount = 0;

    void ProcessOne(string file)
    {
        try
        {
            var source = File.ReadAllText(file);
            var resolver = new DefaultIncludeResolver(searchDirs ?? []);
            var parser = new ConfigParser();
            var config = parser.ParseFile(file, resolver);

            var preDiags = parser.PreprocessorDiagnostics;
            if (preDiags != null)
            {
                foreach (var d in preDiags)
                {
                    if (json)
                        allDiagnostics.Add(new { code = d.Code, severity = "Warning", message = d.Message, file = d.File, line = d.Line, path = "" });
                    else
                        tableRows.Add((d.Code, "Warning", "yellow", d.Message, d.File, $"line {d.Line}"));
                }
            }

            var linter = new ConfigLinter();
            var diags = linter.Lint(config, source);

            if (fix && diags.Any(d => d.Fix != null))
            {
                var fixedSource = ConfigLinter.ApplyFixes(source, diags);
                if (fixedSource != source)
                {
                    File.WriteAllText(file, fixedSource);
                    fixedCount++;
                    source = fixedSource;
                    resolver = new DefaultIncludeResolver(searchDirs ?? []);
                    parser = new ConfigParser();
                    config = parser.ParseFile(file, resolver);
                    linter = new ConfigLinter();
                    diags = linter.Lint(config, source);
                }
            }

            foreach (var d in diags)
            {
                if (json)
                    allDiagnostics.Add(new { code = d.Code, severity = d.Severity.ToString(), message = d.Message, file = d.File, line = d.Line, path = d.Path });
                else
                {
                    var color = d.Severity.ToString() switch
                    {
                        "Error" => "red",
                        "Warning" => "yellow",
                        "Help" => "blue",
                        _ => "white"
                    };
                    tableRows.Add((d.Code, d.Severity.ToString(), color, d.Message, d.File, $"line {d.Line}"));
                }
            }
        }
        catch (Exception ex)
        {
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, line = 0, path = "" });
            else
                tableRows.Add(("PARSE", "Error", "red", ex.Message, file, "line 0"));
        }
    }

    if (files.Length > 1)
    {
        AnsiConsole.Progress()
            .Start(ctx =>
            {
                var task = ctx.AddTask($"Linting {files.Length} config files...", new ProgressTaskSettings { MaxValue = files.Length });
                foreach (var file in files)
                {
                    ProcessOne(file);
                    task.Increment(1);
                }
            });
    }
    else
    {
        foreach (var file in files) ProcessOne(file);
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else
    {
        if (tableRows.Count > 0)
        {
            var table = new Table();
            table.AddColumn("Code");
            table.AddColumn("Severity");
            table.AddColumn("Message");
            table.AddColumn("File");
            table.AddColumn("Location");
            foreach (var (code, severity, color, message, file, location) in tableRows)
            {
                table.AddRow(code, $"[{color}]{severity}[/]", message, file, location);
            }
            AnsiConsole.Write(table);
        }
        if (fixedCount > 0)
            AnsiConsole.MarkupLine($"\n[green]{fixedCount} file{(fixedCount == 1 ? "" : "s")} auto-fixed.[/]");
    }

    var total = json ? allDiagnostics.Count : tableRows.Count;
    if (files.Length == 1 && total == 0)
        AnsiConsole.MarkupLine("[green]No issues found.[/]");
    PrintSummary("config", files.Length, total, sw.ElapsedMilliseconds);
    return exitCode && total > 0 ? 1 : 0;
}

static int LintStringtableBatch(string path, bool json, bool exitCode)
{
    var files = DiscoverFiles(path, "stringtable.xml");
    if (files.Length == 0) return exitCode ? 1 : 0;

    var allDiagnostics = new List<object>();
    var tableRows = new List<(string Code, string Severity, string Color, string Message, string File, string Location)>();
    var sw = Stopwatch.StartNew();

    void ProcessOne(string file)
    {
        try
        {
            var table = StringtableXml.Load(file);
            var linter = new StringtableLinter();
            var diags = linter.Lint(table);

            foreach (var d in diags)
            {
                if (json)
                    allDiagnostics.Add(new { code = d.Code, severity = d.Severity.ToString(), message = d.Message, keyId = d.KeyId, package = d.Package, language = d.Language, file });
                else
                {
                    var color = d.Severity.ToString() switch
                    {
                        "Error" => "red",
                        "Warning" => "yellow",
                        "Help" => "blue",
                        _ => "white"
                    };
                    var loc = !string.IsNullOrEmpty(d.KeyId) ? d.KeyId : "(unknown)";
                    if (!string.IsNullOrEmpty(d.Package)) loc = $"{d.Package}/{loc}";
                    if (!string.IsNullOrEmpty(d.Language)) loc = $"{loc}[{d.Language}]";
                    tableRows.Add((d.Code, d.Severity.ToString(), color, d.Message, file, loc));
                }
            }
        }
        catch (Exception ex)
        {
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, keyId = "", package = "", language = "" });
            else
                tableRows.Add(("PARSE", "Error", "red", ex.Message, file, "—"));
        }
    }

    if (files.Length > 1)
    {
        AnsiConsole.Progress()
            .Start(ctx =>
            {
                var task = ctx.AddTask($"Linting {files.Length} stringtable files...", new ProgressTaskSettings { MaxValue = files.Length });
                foreach (var file in files)
                {
                    ProcessOne(file);
                    task.Increment(1);
                }
            });
    }
    else
    {
        foreach (var file in files) ProcessOne(file);
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else if (tableRows.Count > 0)
    {
        var table = new Table();
        table.AddColumn("Code");
        table.AddColumn("Severity");
        table.AddColumn("Message");
        table.AddColumn("File");
        table.AddColumn("Location");
        foreach (var (code, severity, color, message, file, location) in tableRows)
        {
            table.AddRow(code, $"[{color}]{severity}[/]", message, file, location);
        }
        AnsiConsole.Write(table);
    }

    var total = json ? allDiagnostics.Count : tableRows.Count;
    if (files.Length == 1 && total == 0)
        AnsiConsole.MarkupLine("[green]No issues found.[/]");
    PrintSummary("SQF", files.Length, total, sw.ElapsedMilliseconds);
    return exitCode && total > 0 ? 1 : 0;
}

static int LintPreprocessorBatch(string path, bool json, bool exitCode)
{
    var files = DiscoverFiles(path, "*.cpp")
        .Concat(DiscoverFiles(path, "*.hpp"))
        .Concat(DiscoverFiles(path, "*.ext"))
        .Distinct()
        .ToArray();
    if (files.Length == 0) return exitCode ? 1 : 0;

    var allDiagnostics = new List<object>();
    var tableRows = new List<(string Code, string Severity, string Color, string Message, string File, string Location)>();
    var sw = Stopwatch.StartNew();

    void ProcessOne(string file)
    {
        try
        {
            var resolver = new DefaultIncludeResolver([]);
            var preprocessor = new ConfigPreprocessor(resolver);
            var _ = preprocessor.Preprocess(file);
            var diags = preprocessor.Diagnostics;

            foreach (var d in diags)
            {
                if (json)
                    allDiagnostics.Add(new { code = d.Code, message = d.Message, file = d.File, line = d.Line });
                else
                    tableRows.Add((d.Code, "Warning", "yellow", d.Message, d.File, $"line {d.Line}"));
            }
        }
        catch (Exception ex)
        {
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, line = 0 });
            else
                tableRows.Add(("PARSE", "Error", "red", ex.Message, file, "line 0"));
        }
    }

    if (files.Length > 1)
    {
        AnsiConsole.Progress()
            .Start(ctx =>
            {
                var task = ctx.AddTask($"Preprocessing {files.Length} config files...", new ProgressTaskSettings { MaxValue = files.Length });
                foreach (var file in files)
                {
                    ProcessOne(file);
                    task.Increment(1);
                }
            });
    }
    else
    {
        foreach (var file in files) ProcessOne(file);
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else if (tableRows.Count > 0)
    {
        var table = new Table();
        table.AddColumn("Code");
        table.AddColumn("Severity");
        table.AddColumn("Message");
        table.AddColumn("File");
        table.AddColumn("Location");
        foreach (var (code, severity, color, message, file, location) in tableRows)
        {
            table.AddRow(code, $"[{color}]{severity}[/]", message, file, location);
        }
        AnsiConsole.Write(table);
    }

    var total = json ? allDiagnostics.Count : tableRows.Count;
    if (files.Length == 1 && total == 0)
        AnsiConsole.MarkupLine("[green]No issues found.[/]");
    PrintSummary("config", files.Length, total, sw.ElapsedMilliseconds);
    return exitCode && total > 0 ? 1 : 0;
}

static int LintSqfBatch(string path, bool json, bool exitCode, bool fix = false)
{
    var files = DiscoverFiles(path, "*.sqf");
    if (files.Length == 0) return exitCode ? 1 : 0;

    var allDiagnostics = new List<object>();
    var tableRows = new List<(string Code, string Severity, string Color, string Message, string File, string Location)>();
    var sw = Stopwatch.StartNew();
    var fixedCount = 0;

    void ProcessOne(string file)
    {
        try
        {
            var source = File.ReadAllText(file);
            var tokens = new SqfTokenizer(source, file).Tokenize();
            var parsed = new SqfParser(tokens).ParseFile(source);
            var linter = new SqfLinter();
            var diags = linter.Lint(parsed);

            // Apply fixes if requested
            if (fix && diags.Any(d => d.Fix != null))
            {
                var fixedSource = SqfLinter.ApplyFixes(source, diags);
                if (fixedSource != source)
                {
                    File.WriteAllText(file, fixedSource);
                    fixedCount++;
                    // Re-lint to get post-fix diagnostic state for reporting
                    tokens = new SqfTokenizer(fixedSource, file).Tokenize();
                    parsed = new SqfParser(tokens).ParseFile(fixedSource);
                    linter = new SqfLinter();
                    diags = linter.Lint(parsed);
                }
            }

            foreach (var d in diags)
            {
                if (json)
                    allDiagnostics.Add(new { code = d.Code, severity = d.Severity.ToString(), message = d.Message, file = d.File, line = d.Line, column = d.Column });
                else
                {
                    var color = d.Severity.ToString() switch
                    {
                        "Error" => "red",
                        "Warning" => "yellow",
                        "Help" => "blue",
                        _ => "white"
                    };
                    tableRows.Add((d.Code, d.Severity.ToString(), color, d.Message, d.File, $"{d.Line}:{d.Column}"));
                }
            }
        }
        catch (Exception ex)
        {
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, line = 0, column = 0 });
            else
                tableRows.Add(("PARSE", "Error", "red", ex.Message, file, "0:0"));
        }
    }

    if (files.Length > 1)
    {
        AnsiConsole.Progress()
            .Start(ctx =>
            {
                var task = ctx.AddTask($"Linting {files.Length} SQF files...", new ProgressTaskSettings { MaxValue = files.Length });
                foreach (var file in files)
                {
                    ProcessOne(file);
                    task.Increment(1);
                }
            });
    }
    else
    {
        foreach (var file in files) ProcessOne(file);
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else
    {
        if (tableRows.Count > 0)
        {
            var table = new Table();
            table.AddColumn("Code");
            table.AddColumn("Severity");
            table.AddColumn("Message");
            table.AddColumn("File");
            table.AddColumn("Location");
            foreach (var (code, severity, color, message, file, location) in tableRows)
            {
                table.AddRow(code, $"[{color}]{severity}[/]", message, file, location);
            }
            AnsiConsole.Write(table);
        }
        if (fixedCount > 0)
            AnsiConsole.MarkupLine($"\n[green]{fixedCount} file{(fixedCount == 1 ? "" : "s")} auto-fixed.[/]");
    }

    var total = json ? allDiagnostics.Count : tableRows.Count;
    PrintSummary("SQF", files.Length, total, sw.ElapsedMilliseconds);
    return exitCode && total > 0 ? 1 : 0;
}

static int LintPboBatch(string path, bool json, bool exitCode)
{
    var files = DiscoverFiles(path, "*.pbo");
    if (files.Length == 0) return exitCode ? 1 : 0;

    var allDiagnostics = new List<object>();
    var tableRows = new List<(string Code, string Severity, string Color, string Message, string File, string Location)>();
    var sw = Stopwatch.StartNew();

    void ProcessOne(string file)
    {
        try
        {
            var pbo = new PBO(file);
            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            foreach (var d in diags)
            {
                if (json)
                    allDiagnostics.Add(new { code = d.Code, severity = d.Severity.ToString(), message = d.Message, entryName = d.EntryName, file });
                else
                {
                    var color = d.Severity.ToString() switch
                    {
                        "Error" => "red",
                        "Warning" => "yellow",
                        "Help" => "blue",
                        _ => "white"
                    };
                    var loc = string.IsNullOrEmpty(d.EntryName) ? "—" : d.EntryName;
                    tableRows.Add((d.Code, d.Severity.ToString(), color, d.Message, file, loc));
                }
            }
        }
        catch (Exception ex)
        {
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, entryName = "" });
            else
                tableRows.Add(("PARSE", "Error", "red", ex.Message, file, "—"));
        }
    }

    if (files.Length > 1)
    {
        AnsiConsole.Progress()
            .Start(ctx =>
            {
                var task = ctx.AddTask($"Linting {files.Length} PBO files...", new ProgressTaskSettings { MaxValue = files.Length });
                foreach (var file in files)
                {
                    ProcessOne(file);
                    task.Increment(1);
                }
            });
    }
    else
    {
        foreach (var file in files) ProcessOne(file);
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else if (tableRows.Count > 0)
    {
        var table = new Table();
        table.AddColumn("Code");
        table.AddColumn("Severity");
        table.AddColumn("Message");
        table.AddColumn("File");
        table.AddColumn("Location");
        foreach (var (code, severity, color, message, file, location) in tableRows)
        {
            table.AddRow(code, $"[{color}]{severity}[/]", message, file, location);
        }
        AnsiConsole.Write(table);
    }

    var total = json ? allDiagnostics.Count : tableRows.Count;
    if (files.Length == 1 && total == 0)
        AnsiConsole.MarkupLine("[green]No issues found.[/]");
    PrintSummary("PBO", files.Length, total, sw.ElapsedMilliseconds);
    return exitCode && total > 0 ? 1 : 0;
}

// ─── Single file handlers (p3d/paa/pbo) ───

static void HandleP3DValidate(string path)
{
    var result = P3DValidator.Analyse(path);
    AnsiConsole.MarkupLine($"Valid: [{(result.IsValid ? "green" : "red")}]{result.IsValid}[/]");
    AnsiConsole.MarkupLine($"Format: [blue]{((result.IsMLOD ? "MLOD" : "ODOL"))}[/]");
    AnsiConsole.MarkupLine($"Version: {result.Version}");
    AnsiConsole.MarkupLine($"LODs: {result.LodCount}");
    foreach (var issue in result.Issues)
        AnsiConsole.MarkupLine($"  [[yellow]{issue.Severity}[/]] {issue.Code}: {issue.Message}");
    AnsiConsole.MarkupLine($"Vertices: {result.TotalVertices}");
    AnsiConsole.MarkupLine($"Faces: {result.TotalFaces}");
    foreach (var lod in result.LODs)
    {
        AnsiConsole.MarkupLine($"  LOD {lod.Resolution:F1} ([blue]{lod.TypeName}[/]): {lod.VertexCount} verts, {lod.FaceCount} faces, {lod.TextureCount} textures");
    }
}

static void HandleP3DInfo(string path)
{
    using var stream = File.OpenRead(path);
    var p3d = new BIS.P3D.P3D(stream);

    AnsiConsole.MarkupLine($"File: [blue]{Path.GetFileName(path)}[/]");
    AnsiConsole.MarkupLine($"Format: [blue]{(p3d.IsODOLFormat ? "ODOL" : p3d.IsMLODFormat ? "MLOD" : "Unknown")}[/]");
    AnsiConsole.MarkupLine($"Version: {p3d.Version}");

    if (p3d.ModelInfo != null)
    {
        AnsiConsole.MarkupLine($"Class: {p3d.ModelInfo.Class}");
        AnsiConsole.MarkupLine($"Map: {p3d.ModelInfo.MapType}");
    }

    foreach (var lod in p3d.LODs)
    {
        AnsiConsole.MarkupLine($"  LOD {lod.Resolution:F1}: {lod.VertexCount} verts, {lod.FaceCount} faces");
        var texs = lod.GetTextures()?.ToArray() ?? [];
        if (texs.Length > 0)
            AnsiConsole.MarkupLine($"    Textures: {string.Join(", ", texs.Take(5))}{(texs.Length > 5 ? $" +{texs.Length - 5} more" : "")}");
    }
}

static void HandleP3DConvert(string path, FileInfo? output)
{
    using var stream = File.OpenRead(path);
    var p3d = new BIS.P3D.P3D(stream);
    var outPath = output?.FullName ?? Path.ChangeExtension(path, p3d.IsODOLFormat ? ".mlod" : ".odol");

    if (p3d.ODOL != null)
    {
        var mlod = BIS.P3D.Conversion.ODOL2MLOD.Convert(p3d.ODOL);
        mlod.WriteToFile(outPath, true);
        AnsiConsole.MarkupLine($"ODOL->MLOD: [green]{outPath}[/] ({mlod.Lods.Length} LODs)");
    }
    else if (p3d.MLOD != null)
    {
        var odol = BIS.P3D.Conversion.MLOD2ODOL.Convert(p3d.MLOD);
        using var outStream = File.Create(outPath);
        var writer = new BIS.Core.Streams.BinaryWriterEx(outStream);
        odol.Write(writer);
        AnsiConsole.MarkupLine($"MLOD->ODOL: [green]{outPath}[/] ({odol.Lods.Length} LODs)");
    }
    else
    {
        AnsiConsole.MarkupLine("[red]Unknown P3D format — cannot convert[/]");
    }
}

static void HandleP3DRoundtrip(string path)
{
    using var stream = File.OpenRead(path);
    var p3d = new BIS.P3D.P3D(stream);

    if (p3d.ODOL != null)
    {
        AnsiConsole.Markup("ODOL -> MLOD -> ODOL: ");
        var mlod = BIS.P3D.Conversion.ODOL2MLOD.Convert(p3d.ODOL);
        var odol = BIS.P3D.Conversion.MLOD2ODOL.Convert(mlod);
        bool ok = odol.Lods.Length == p3d.ODOL.Lods.Length;
        if (ok)
        {
            foreach (var origLod in p3d.ODOL.Lods)
            {
                var roundtripLod = odol.Lods.FirstOrDefault(l => l.Resolution == origLod.Resolution);
                if (roundtripLod == null || roundtripLod.Vertices.Count != origLod.Vertices.Count)
                {
                    ok = false;
                    break;
                }
            }
        }
        AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[red]MISMATCH[/]");
        AnsiConsole.MarkupLine($"  LODs: {p3d.ODOL.Lods.Length} -> {odol.Lods.Length}");
        foreach (var origLod in p3d.ODOL.Lods)
        {
            var roundtripLod = odol.Lods.FirstOrDefault(l => l.Resolution == origLod.Resolution);
            if (roundtripLod != null)
                AnsiConsole.MarkupLine($"  LOD {origLod.Resolution:F1}: {origLod.Vertices.Count} verts -> {roundtripLod.Vertices.Count} verts");
        }
    }
    else if (p3d.MLOD != null)
    {
        AnsiConsole.Markup("MLOD -> ODOL -> MLOD: ");
        var odol = BIS.P3D.Conversion.MLOD2ODOL.Convert(p3d.MLOD);
        var mlod = BIS.P3D.Conversion.ODOL2MLOD.Convert(odol);
        bool ok = mlod.Lods.Length == p3d.MLOD.Lods.Length;
        if (ok)
        {
            foreach (var origLod in p3d.MLOD.Lods)
            {
                var roundtripLod = mlod.Lods.FirstOrDefault(l => l.Resolution == origLod.Resolution);
                if (roundtripLod == null || roundtripLod.Points.Length != origLod.Points.Length)
                {
                    ok = false;
                    break;
                }
            }
        }
        AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[red]MISMATCH[/]");
        AnsiConsole.MarkupLine($"  LODs: {p3d.MLOD.Lods.Length} -> {mlod.Lods.Length}");
        foreach (var origLod in p3d.MLOD.Lods)
        {
            var roundtripLod = mlod.Lods.FirstOrDefault(l => l.Resolution == origLod.Resolution);
            if (roundtripLod != null)
                AnsiConsole.MarkupLine($"  LOD {origLod.Resolution:F1}: {origLod.Points.Length} points -> {roundtripLod.Points.Length} points");
        }
    }
}

static void HandleP3DExport(string path, DirectoryInfo? outputDir)
{
    string outDir = outputDir?.FullName ?? Path.Combine(Path.GetDirectoryName(path) ?? ".", "_blender");
    AnsiConsole.MarkupLine($"[blue]Exporting {Path.GetFileName(path)} to Blender...[/]");
    var task = BlenderHelper.ExportSingleAsync(path, outDir);
    bool ok = task.GetAwaiter().GetResult();
    if (ok)
        AnsiConsole.MarkupLine($"[green]Blender export complete in {outDir}[/]");
    else
        AnsiConsole.MarkupLine($"[red]Blender export failed[/]");
}

static void HandlePAAAnalyze(string path)
{
    var analysis = BIS.PAA.PaaAnalyzer.Analyze(path);
    AnsiConsole.MarkupLine($"Format: [blue]{analysis.Format}[/]");
    AnsiConsole.MarkupLine($"Size: {analysis.Width}x{analysis.Height}");
    AnsiConsole.MarkupLine($"MIP maps: {analysis.MipmapCount}");
    AnsiConsole.MarkupLine($"Alpha: {(analysis.HasAlpha ? "[green]yes[/]" : "[grey]no[/]")}");
    AnsiConsole.MarkupLine($"Transparency: {(analysis.IsTransparent ? "[green]yes[/]" : "[grey]no[/]")}");
    AnsiConsole.MarkupLine($"Category: {analysis.CategoryLabel}");
}

static void HandlePAASuggest(string path)
{
    var analysis = BIS.PAA.PaaAnalyzer.Analyze(path);
    var suggestion = BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
    AnsiConsole.MarkupLine($"Current: {analysis.Format}");
    AnsiConsole.MarkupLine($"Optimal: [green]{suggestion.RecommendedFormat}[/]");
    AnsiConsole.MarkupLine($"Rationale: {suggestion.Rationale}");
    AnsiConsole.MarkupLine($"Size factor: {suggestion.EstimatedSizeFactor:F3}x");
    if (!string.IsNullOrEmpty(suggestion.Notes))
        AnsiConsole.MarkupLine($"Notes: {suggestion.Notes}");
}

static void HandlePBOList(string path, bool raw)
{
    var pbo = new BIS.PBO.PBO(path);
    AnsiConsole.MarkupLine($"File: [blue]{Path.GetFileName(path)}[/]");
    AnsiConsole.MarkupLine($"Entries: {pbo.Files.Count}");

    if (raw)
    {
        long totalSize = 0;
        foreach (var entry in pbo.Files)
        {
            int size = entry.Size;
            totalSize += size;
            string packInfo = entry.IsCompressed ? " [yellow](packed)[/]" : "";
            AnsiConsole.MarkupLine($"  {entry.FileName,-40} {size,8} bytes{packInfo}");
        }
        AnsiConsole.MarkupLine($"Total: {totalSize} bytes");
        return;
    }

    var deobfuscator = new PboDeobfuscator();
    var result = deobfuscator.Process(pbo);

    if (result.IsObfuscated)
        AnsiConsole.MarkupLine($"[yellow]Deobfuscation:[/] {result.MatchedProfile}");
    if (result.FilteredOut.Count > 0)
        AnsiConsole.MarkupLine($"[grey]Decoy/stub entries hidden:[/] {result.FilteredOut.Count}");

    long totalSizeDeob = 0;
    for (int i = 0; i < pbo.Files.Count; i++)
    {
        if (result.FilteredOut.Contains(i))
            continue;

        var entry = pbo.Files[i];
        int size = entry.Size;
        totalSizeDeob += size;

        bool recovered = result.RecoveredNames.TryGetValue(i, out var recoveredName);
        string packInfo = entry.IsCompressed ? " [yellow](packed)[/]" : "";
        string recoveryTag = recovered ? " [green]✔[/]" : "";

        if (recovered)
        {
            var originalName = entry.FileName;
            AnsiConsole.MarkupLine($"  {recoveredName,-40} {size,8} bytes{packInfo}{recoveryTag}");
            if (originalName != recoveredName)
                AnsiConsole.MarkupLine($"    [grey](was: {originalName})[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  {entry.FileName,-40} {size,8} bytes{packInfo}");
        }
    }
    AnsiConsole.MarkupLine($"Total: {totalSizeDeob} bytes");
    if (result.FilteredOut.Count > 0)
        AnsiConsole.MarkupLine($"[grey]({result.FilteredOut.Count} decoy/stub entries skipped)[/]");
}

static void HandlePBOExtract(string path, DirectoryInfo? outputDir, bool raw, bool matchTextures, bool exportBlender, bool fuzzyMatch)
{
    var outDir = outputDir?.FullName ?? Path.GetFileNameWithoutExtension(path);
    var pbo = new BIS.PBO.PBO(path);

    if (raw)
    {
        Directory.CreateDirectory(outDir);
        pbo.ExtractFiles(pbo.Files, outDir);
        AnsiConsole.MarkupLine($"[green]Extracted {pbo.Files.Count} files to {outDir}[/]");
        return;
    }

    var deobfuscator = new PboDeobfuscator();
    var result = deobfuscator.Process(pbo);

    // Apply pixel-content orphan matching BEFORE extraction so matched
    // names flow into the pathMap and reference updaters.
    if (matchTextures)
    {
        var applied = PaaContentMatcher.ApplyMatches(pbo, result);
        if (applied.Count > 0)
        {
            AnsiConsole.MarkupLine($"  [green]Pixel-content matching: {applied.Count} orphan texture(s) auto-renamed[/]");
            ShowAppliedTextureMatches(pbo, applied);
        }
        else
        {
            int orphanCount = CountOrphanPaas(pbo, result);
            AnsiConsole.MarkupLine($"  [yellow]No pixel-content matches for {orphanCount} orphan texture(s).[/]");
        }
    }

    // Apply fuzzy structural PAA matching — after exact-match so orphans
    // that differ in color palette but share UV layout can still be matched.
    if (fuzzyMatch)
    {
        var fuzzyApplied = PaaContentMatcher.ApplyFuzzyMatches(pbo, result);
        if (fuzzyApplied.Count > 0)
        {
            AnsiConsole.MarkupLine($"  [green]Fuzzy texture matching: {fuzzyApplied.Count} orphan texture(s) auto-renamed[/]");
            ShowAppliedTextureMatches(pbo, fuzzyApplied);
        }
        else
        {
            int orphanCount = CountOrphanPaas(pbo, result);
            AnsiConsole.MarkupLine($"  [yellow]No fuzzy matches for {orphanCount} remaining orphan texture(s).[/]");
        }
    }

    // Apply RVMAT content matching — after texture matching so known
    // texture names are available for reference inference.
    var rvmatApplied = RvmatContentMatcher.ApplyMatches(pbo, result);
    if (rvmatApplied.Count > 0)
        AnsiConsole.MarkupLine($"  [green]RVMAT content matching: {rvmatApplied.Count} orphan material(s) auto-renamed[/]");

    int extracted = DeobfuscatedExtract(pbo, result, outDir, out var pathMap);
    AnsiConsole.MarkupLine($"[green]Extracted {extracted} files to {outDir} (deobfuscated)[/]");

    // ─── Blender export (PAA→PNG + batch import → .blend) ───
    if (exportBlender)
    {
        // Convert PAAs to PNGs for fast Blender loading (a3ob.import_paa is ~4.5s per texture)
        string textureDir = Path.Combine(outDir, "_textures");
        int pngCount = BlenderExport.ExportAll(outDir, textureDir);
        if (pngCount > 0)
            AnsiConsole.MarkupLine($"  Converted {pngCount} PAA texture(s) to PNG for Blender");

        string blenderDir = Path.Combine(outDir, "_blender");
        var blenderTask = BlenderHelper.ExportAsync(outDir, blenderDir, textureDir);
        int blenderResult = blenderTask.GetAwaiter().GetResult();
        AnsiConsole.MarkupLine($"[green]Blender export complete in {blenderDir}[/]");
    }
}

/// <summary>
/// Extracts PBO files applying deobfuscated names and updating internal
/// references (config.bin model= paths, P3D textures, RVMAT materials).
/// Decoy/stub entries are skipped entirely.
/// </summary>
static int DeobfuscatedExtract(BIS.PBO.PBO pbo, DeobfuscationResult result, string outDir, out Dictionary<string, string> pathMap)
{
    if (!result.IsObfuscated && result.RecoveredNames.Count == 0)
    {
        // Clean PBO — extract as-is
        Directory.CreateDirectory(outDir);
        pbo.ExtractFiles(pbo.Files, outDir);
        pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return pbo.Files.Count;
    }

    if (result.IsObfuscated)
        AnsiConsole.MarkupLine($"  [yellow]Detected: {result.MatchedProfile}[/]");

    // Build path map: RawFileName (preserved obfuscated path) → output name
    // Recovered entries get their recovered names; non-recovered entries get
    // their sanitized names so reference updaters can resolve all paths.
    pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var recoveredIndices = new HashSet<int>(result.RecoveredNames.Keys);
    foreach (var kvp in result.RecoveredNames)
    {
        var orig = pbo.Files[kvp.Key].RawFileName.Replace('\\', '/');
        pathMap[orig] = kvp.Value.Replace('\\', '/');
    }
    for (int i = 0; i < pbo.Files.Count; i++)
    {
        if (result.FilteredOut.Contains(i)) continue;
        if (recoveredIndices.Contains(i)) continue;
        var orig = pbo.Files[i].RawFileName.Replace('\\', '/');
        var final = pbo.Files[i].FileName.Replace('\\', '/');
        if (!pathMap.ContainsKey(orig))
            pathMap[orig] = final;
    }

    // Run reference updaters on each entry
    var modifiedData = new Dictionary<int, byte[]>();
    var updaters = new List<IReferenceUpdater>
    {
        new P3DTextureReferenceUpdater(),
        new RVMATReferenceUpdater(),
        new ConfigReferenceUpdater()
    };

    for (int i = 0; i < pbo.Files.Count; i++)
    {
        if (result.FilteredOut.Contains(i)) continue;
        foreach (var updater in updaters)
        {
            if (modifiedData.ContainsKey(i)) break;
            var data = updater.UpdateReferences(pbo.Files[i], pathMap);
            if (data != null)
            {
                modifiedData[i] = data;
                break;
            }
        }
    }

    int extracted = 0;
    Directory.CreateDirectory(outDir);
    for (int i = 0; i < pbo.Files.Count; i++)
    {
        if (result.FilteredOut.Contains(i)) continue;
        var entry = pbo.Files[i];
        string entryName = result.RecoveredNames.TryGetValue(i, out var recoveredName)
            ? recoveredName.Replace('\\', '/').ToLowerInvariant()
            : entry.FileName.Replace('\\', '/').ToLowerInvariant();

        // Detect files that need format conversion for pack-ready output
        bool isConfigBin = entryName.Equals("config.bin", StringComparison.OrdinalIgnoreCase);
        bool isTexHeaders = entryName.Equals("texheaders.bin", StringComparison.OrdinalIgnoreCase);
        bool isP3D = entryName.EndsWith(".p3d", StringComparison.OrdinalIgnoreCase);

        // Rename files that get converted
        string outputName = entryName;
        if (isConfigBin) outputName = "config.cpp";
        // .p3d keeps its extension — game reads both ODOL and MLOD formats

        var path = Path.Combine(outDir, outputName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (modifiedData.TryGetValue(i, out var data))
        {
            // Reference updater modified this file — apply format conversion if needed
            if (isConfigBin)
            {
                var configText = DerapifyConfig(data);
                File.WriteAllText(path, configText);
            }
            else if (isP3D)
            {
                var mlodData = ConvertP3DToMLOD(data);
                File.WriteAllBytes(path, mlodData ?? data);
            }
            else
            {
                File.WriteAllBytes(path, data);
            }
        }
        else
        {
            // File was not modified — check if it still needs format conversion
            if (isTexHeaders)
            {
                // texHeaders.bin is a generated cache — HEMTT regenerates it from .paa files.
                // Skip it; the build tool will generate a fresh one.
                continue;
            }
            else if (isP3D)
            {
                using var src = entry.OpenRead();
                using var ms = new MemoryStream();
                src.CopyTo(ms);
                var mlodData = ConvertP3DToMLOD(ms.ToArray());
                File.WriteAllBytes(path, mlodData ?? ms.ToArray());
            }
            else
            {
                using var source = entry.OpenRead();
                using var target = File.Create(path);
                source.CopyTo(target);
            }
        }

        extracted++;
    }

    if (result.FilteredOut.Count > 0)
        AnsiConsole.MarkupLine($"  [grey]({result.FilteredOut.Count} decoy/stub entries skipped)[/]");

    return extracted;
}

static int CountOrphanPaas(BIS.PBO.PBO pbo, DeobfuscationResult result)
{
    int count = 0;
    for (int i = 0; i < pbo.Files.Count; i++)
    {
        if (result.FilteredOut.Contains(i)) continue;
        if (result.RecoveredNames.ContainsKey(i)) continue;
        if (pbo.Files[i].FileName.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
            count++;
    }
    return count;
}

static void ShowAppliedTextureMatches(BIS.PBO.PBO pbo, Dictionary<int, string> applied)
{
    var table = new Table();
    table.AddColumn("Orphan");
    table.AddColumn("Auto-Renamed To");

    foreach (var kvp in applied)
    {
        var orphanName = pbo.Files[kvp.Key].FileName;
        table.AddRow(
            $"[grey]{orphanName}[/]",
            $"[green]{kvp.Value}[/]");
    }

    AnsiConsole.Write(table);
}

static string DerapifyConfig(byte[] configBinData)
{
    using var ms = new MemoryStream(configBinData);
    var config = new ParamFile(ms);
    var text = ConfigSerializer.SerializeToConfigText(config);
    return NormalizeConfigPaths(text);
}

/// <summary>
/// Normalizes file path strings in generated config.cpp to lowercase
/// so they match the extracted filesystem (all deobfuscated paths are lowercase).
/// Affects paths ending with .paa, .p3d, .rvmat extensions.
/// </summary>
static string NormalizeConfigPaths(string configText)
{
    var pathExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".paa", ".p3d", ".rvmat", ".bin"
    };

    return System.Text.RegularExpressions.Regex.Replace(
        configText,
        "\"[^\"]+\"",
        m =>
        {
            var val = m.Value;
            var inner = val.Trim('"');
            var ext = Path.GetExtension(inner);
            if (!string.IsNullOrEmpty(ext) && pathExtensions.Contains(ext))
            {
                var normalized = inner.Replace('\\', '/').ToLowerInvariant();
                return "\"" + normalized + "\"";
            }
            return val;
        });
}

static byte[]? ConvertP3DToMLOD(byte[] p3dData)
{
    try
    {
        using var ms = new MemoryStream(p3dData);
        var p3d = new P3D(ms);
        if (p3d.IsODOLFormat)
        {
            var mlod = ODOL2MLOD.Convert(p3d.ODOL);
            using var outMs = new MemoryStream();
            mlod.WriteToStream(outMs);
            return outMs.ToArray();
        }
    }
    catch
    {
        // Conversion failed — caller falls back to original data
    }
    return null;
}

static void HandlePBOPack(string sourceDir, FileInfo? output, string? prefix, bool compress)
{
    var dir = new DirectoryInfo(sourceDir);
    var outPath = output?.FullName ?? dir.FullName.TrimEnd('/', '\\') + ".pbo";
    var prefixVal = prefix ?? dir.Name;

    var pbo = new PBO();
    pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", prefixVal));

    var files = dir.GetFiles("*", SearchOption.AllDirectories);
    foreach (var file in files)
    {
        var relativePath = Path.GetRelativePath(sourceDir, file.FullName).Replace('\\', '/');
        pbo.Files.Add(new PBOFileToAdd(file, relativePath));
    }

    pbo.SaveTo(outPath, compress);

    var compressInfo = compress ? " (compressed)" : "";
    AnsiConsole.MarkupLine($"[green]Packed {files.Length} files{compressInfo} -> {outPath}[/]  (prefix: [blue]{prefixVal}[/])");

    // Validate the generated PBO
    try
    {
        var linter = new PboLinter();
        var linterDiags = linter.Lint(pbo);
        if (linterDiags.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Issues:[/]");
            foreach (var d in linterDiags)
                AnsiConsole.MarkupLine($"  [yellow]{d}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[green]No issues found.[/]");
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Validation error: {ex.Message}[/]");
    }
}

static int HandleFmtSqf(string path, bool check)
{
    var files = DiscoverFiles(path, "*.sqf");
    if (files.Length == 0) return 1;

    var formatter = new SqfFormatter();
    var anyChanged = false;
    var totalChanged = 0;
    var sw = System.Diagnostics.Stopwatch.StartNew();

    void ProcessOne(string file)
    {
        var source = File.ReadAllText(file);
        var formatted = formatter.Format(source);

        if (formatted != source)
        {
            anyChanged = true;
            totalChanged++;
            if (check)
            {
                AnsiConsole.MarkupLine($"[yellow]{file}: would reformat[/]");
            }
            else
            {
                File.WriteAllText(file, formatted);
            }
        }
    }

    if (files.Length > 1)
    {
        AnsiConsole.Progress()
            .Start(ctx =>
            {
                var task = ctx.AddTask($"Formatting {files.Length} SQF files...", new ProgressTaskSettings { MaxValue = files.Length });
                foreach (var file in files)
                {
                    ProcessOne(file);
                    task.Increment(1);
                }
            });
    }
    else
    {
        foreach (var file in files) ProcessOne(file);
    }
    sw.Stop();

    if (check)
    {
        if (anyChanged)
            AnsiConsole.MarkupLine($"\n[red]{files.Length} file(s) checked, {totalChanged} would reformat in {sw.ElapsedMilliseconds / 1000.0:F1}s.[/]");
        else
            AnsiConsole.MarkupLine($"\n[green]{files.Length} file(s) checked, all files are clean in {sw.ElapsedMilliseconds / 1000.0:F1}s.[/]");
    }
    else
    {
        if (anyChanged)
            AnsiConsole.MarkupLine($"\n[green]{files.Length} file(s) processed, {totalChanged} reformatted in {sw.ElapsedMilliseconds / 1000.0:F1}s.[/]");
        else
            AnsiConsole.MarkupLine($"\n[green]{files.Length} file(s) processed, all clean in {sw.ElapsedMilliseconds / 1000.0:F1}s.[/]");
    }
    return anyChanged ? 1 : 0;
}

static void HandleConfigSerialize(string path, FileInfo output)
{
    var parser = new ConfigParser();
    var config = parser.ParseFile(path);
    using var stream = File.Create(output.FullName);
    ConfigSerializer.Serialize(config, stream);
    AnsiConsole.MarkupLine($"[green]Serialized config to {output.FullName}[/]");
}
