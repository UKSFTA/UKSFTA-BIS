using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using BIS.Core.Config;
using BIS.P3D;
using BIS.SQF;
using BIS.Stringtable;
using BIS.PBO;

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
        },
        new Command("extract", "Extract PBO contents")
        {
            new Argument<FileInfo>("path", "Path to .pbo file").ExistingOnly(),
            new Option<DirectoryInfo?>(["--output-dir", "-o"], "Output directory"),
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
                    case "paa analyze":      HandlePAAAnalyze(GetFileArg(sub.Arguments.First(), parse).FullName); break;
                    case "paa suggest":      HandlePAASuggest(GetFileArg(sub.Arguments.First(), parse).FullName); break;
                    case "pbo list":         HandlePBOList(GetFileArg(sub.Arguments.First(), parse).FullName); break;
                    case "pbo extract":      HandlePBOExtract(GetFileArg(sub.Arguments.First(), parse).FullName, GetOptVal<DirectoryInfo?>(sub, parse, "--output-dir")); break;
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
                Console.Error.WriteLine($"Error: {ex.Message}");
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
    Console.Error.WriteLine($"Error: '{path}' is not a file or directory");
    return [];
}

static void PrintSummary(string label, int fileCount, int issueCount, long elapsedMs)
{
    var time = elapsedMs > 0 ? $" in {elapsedMs / 1000.0:F1}s" : "";
    var prefix = issueCount > 0 ? "\n" : "";
    if (issueCount == 0)
        Console.WriteLine($"{prefix}{fileCount} {label} file{(fileCount == 1 ? "" : "s")} linted, no issues found{time}.");
    else
        Console.WriteLine($"{prefix}{fileCount} {label} file{(fileCount == 1 ? "" : "s")} linted, {issueCount} issue{(issueCount == 1 ? "" : "s")} found{time}.");
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
    var allText = new List<string>();
    var sw = Stopwatch.StartNew();
    var fixedCount = 0;

    foreach (var file in files)
    {
        try
        {
            var source = File.ReadAllText(file);
            var resolver = new DefaultIncludeResolver(searchDirs ?? []);
            var parser = new ConfigParser();
            var config = parser.ParseFile(file, resolver);

            // Collect preprocessor diagnostics
            var preDiags = parser.PreprocessorDiagnostics;
            if (preDiags != null)
            {
                foreach (var d in preDiags)
                {
                    var prefix = $"{file}: ";
                    if (json)
                        allDiagnostics.Add(new { code = d.Code, severity = "Warning", message = d.Message, file = d.File, line = d.Line, path = "" });
                    else
                        allText.Add(prefix + d.ToString());
                }
            }

            var linter = new ConfigLinter();
            var diags = linter.Lint(config, source);

            // Apply fixes if requested
            if (fix && diags.Any(d => d.Fix != null))
            {
                var fixedSource = ConfigLinter.ApplyFixes(source, diags);
                if (fixedSource != source)
                {
                    File.WriteAllText(file, fixedSource);
                    fixedCount++;
                    // Re-lint post-fix
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
                    allText.Add(d.ToString());
            }
        }
        catch (Exception ex)
        {
            var msg = $"{file}: error: {ex.Message}";
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, line = 0, path = "" });
            else
                allText.Add(msg);
        }
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else
    {
        foreach (var t in allText) Console.WriteLine(t);
        if (fixedCount > 0)
            Console.WriteLine($"\n{fixedCount} file{(fixedCount == 1 ? "" : "s")} auto-fixed.");
    }

    var total = allText.Count + allDiagnostics.Count;
    if (files.Length == 1 && total == 0)
        Console.WriteLine("No issues found.");
    PrintSummary("config", files.Length, total, sw.ElapsedMilliseconds);
    return exitCode && total > 0 ? 1 : 0;
}

static int LintStringtableBatch(string path, bool json, bool exitCode)
{
    var files = DiscoverFiles(path, "stringtable.xml");
    if (files.Length == 0) return exitCode ? 1 : 0;

    var allDiagnostics = new List<object>();
    var allText = new List<string>();
    var sw = Stopwatch.StartNew();

    foreach (var file in files)
    {
        try
        {
            var table = StringtableXml.Load(file);
            var linter = new StringtableLinter();
            var diags = linter.Lint(table);

            foreach (var d in diags)
            {
                var prefix = $"{file}: ";
                if (json)
                    allDiagnostics.Add(new { code = d.Code, severity = d.Severity.ToString(), message = d.Message, keyId = d.KeyId, package = d.Package, language = d.Language, file });
                else
                    allText.Add(prefix + d.ToString());
            }
        }
        catch (Exception ex)
        {
            var msg = $"{file}: error: {ex.Message}";
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, keyId = "", package = "", language = "" });
            else
                allText.Add(msg);
        }
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else
        foreach (var t in allText) Console.WriteLine(t);

    var total = allText.Count + allDiagnostics.Count;
    if (files.Length == 1 && total == 0)
        Console.WriteLine("No issues found.");
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
    var allText = new List<string>();
    var sw = Stopwatch.StartNew();

    foreach (var file in files)
    {
        try
        {
            var resolver = new DefaultIncludeResolver([]);
            var preprocessor = new ConfigPreprocessor(resolver);
            var _ = preprocessor.Preprocess(file);
            var diags = preprocessor.Diagnostics;

            foreach (var d in diags)
            {
                var prefix = $"{file}: ";
                if (json)
                    allDiagnostics.Add(new { code = d.Code, message = d.Message, file = d.File, line = d.Line });
                else
                    allText.Add(prefix + d.ToString());
            }
        }
        catch (Exception ex)
        {
            var msg = $"{file}: error: {ex.Message}";
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, line = 0 });
            else
                allText.Add(msg);
        }
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else
        foreach (var t in allText) Console.WriteLine(t);

    var total = allText.Count + allDiagnostics.Count;
    if (files.Length == 1 && total == 0)
        Console.WriteLine("No issues found.");
    PrintSummary("config", files.Length, total, sw.ElapsedMilliseconds);
    return exitCode && total > 0 ? 1 : 0;
}

static int LintSqfBatch(string path, bool json, bool exitCode, bool fix = false)
{
    var files = DiscoverFiles(path, "*.sqf");
    if (files.Length == 0) return exitCode ? 1 : 0;

    var allDiagnostics = new List<object>();
    var allText = new List<string>();
    var sw = Stopwatch.StartNew();
    var fixedCount = 0;

    foreach (var file in files)
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
                var line = d.ToString();
                if (json)
                    allDiagnostics.Add(new { code = d.Code, severity = d.Severity.ToString(), message = d.Message, file = d.File, line = d.Line, column = d.Column });
                else
                    allText.Add(line);
            }
        }
        catch (Exception ex)
        {
            var msg = $"{file}: error: {ex.Message}";
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, line = 0, column = 0 });
            else
                allText.Add(msg);
        }
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else
    {
        foreach (var t in allText) Console.WriteLine(t);
        if (fixedCount > 0)
            Console.WriteLine($"\n{fixedCount} file{(fixedCount == 1 ? "" : "s")} auto-fixed.");
    }

    var total = allText.Count + allDiagnostics.Count;
    PrintSummary("SQF", files.Length, total, sw.ElapsedMilliseconds);
    return exitCode && total > 0 ? 1 : 0;
}

static int LintPboBatch(string path, bool json, bool exitCode)
{
    var files = DiscoverFiles(path, "*.pbo");
    if (files.Length == 0) return exitCode ? 1 : 0;

    var allDiagnostics = new List<object>();
    var allText = new List<string>();
    var sw = Stopwatch.StartNew();

    foreach (var file in files)
    {
        try
        {
            var pbo = new PBO(file);
            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            foreach (var d in diags)
            {
                var line = d.ToString();
                if (json)
                    allDiagnostics.Add(new { code = d.Code, severity = d.Severity.ToString(), message = d.Message, entryName = d.EntryName, file });
                else
                    allText.Add($"{file}: {line}");
            }
        }
        catch (Exception ex)
        {
            var msg = $"{file}: error: {ex.Message}";
            if (json)
                allDiagnostics.Add(new { code = "PARSE", severity = "Error", message = ex.Message, file, entryName = "" });
            else
                allText.Add(msg);
        }
    }
    sw.Stop();

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(allDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
    else
        foreach (var t in allText) Console.WriteLine(t);

    var total = allText.Count + allDiagnostics.Count;
    if (files.Length == 1 && total == 0)
        Console.WriteLine("No issues found.");
    PrintSummary("PBO", files.Length, total, sw.ElapsedMilliseconds);
    return exitCode && total > 0 ? 1 : 0;
}

// ─── Single file handlers (p3d/paa/pbo) ───

static void HandleP3DValidate(string path)
{
    var result = P3DValidator.Analyse(path);
    Console.WriteLine($"Valid: {result.IsValid}");
    Console.WriteLine($"Format: {(result.IsMLOD ? "MLOD" : "ODOL")}");
    Console.WriteLine($"Version: {result.Version}");
    Console.WriteLine($"LODs: {result.LodCount}");
    foreach (var issue in result.Issues)
        Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
    Console.WriteLine($"Vertices: {result.TotalVertices}");
    Console.WriteLine($"Faces: {result.TotalFaces}");
    foreach (var lod in result.LODs)
    {
        Console.WriteLine($"  LOD {lod.Resolution:F1} ({lod.TypeName}): {lod.VertexCount} verts, {lod.FaceCount} faces, {lod.TextureCount} textures");
    }
}

static void HandleP3DInfo(string path)
{
    using var stream = File.OpenRead(path);
    var p3d = new BIS.P3D.P3D(stream);

    Console.WriteLine($"File: {Path.GetFileName(path)}");
    Console.WriteLine($"Format: {(p3d.IsODOLFormat ? "ODOL" : p3d.IsMLODFormat ? "MLOD" : "Unknown")}");
    Console.WriteLine($"Version: {p3d.Version}");

    if (p3d.ModelInfo != null)
    {
        Console.WriteLine($"Class: {p3d.ModelInfo.Class}");
        Console.WriteLine($"Map: {p3d.ModelInfo.MapType}");
    }

    foreach (var lod in p3d.LODs)
    {
        Console.WriteLine($"  LOD {lod.Resolution:F1}: {lod.VertexCount} verts, {lod.FaceCount} faces");
        var texs = lod.GetTextures()?.ToArray() ?? [];
        if (texs.Length > 0)
            Console.WriteLine($"    Textures: {string.Join(", ", texs.Take(5))}{(texs.Length > 5 ? $" +{texs.Length - 5} more" : "")}");
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
        Console.WriteLine($"ODOL->MLOD: {outPath} ({mlod.Lods.Length} LODs)");
    }
    else if (p3d.MLOD != null)
    {
        var odol = BIS.P3D.Conversion.MLOD2ODOL.Convert(p3d.MLOD);
        using var outStream = File.Create(outPath);
        var writer = new BIS.Core.Streams.BinaryWriterEx(outStream);
        odol.Write(writer);
        Console.WriteLine($"MLOD->ODOL: {outPath} ({odol.Lods.Length} LODs)");
    }
    else
    {
        Console.Error.WriteLine("Unknown P3D format — cannot convert");
    }
}

static void HandleP3DRoundtrip(string path)
{
    using var stream = File.OpenRead(path);
    var p3d = new BIS.P3D.P3D(stream);

    if (p3d.ODOL != null)
    {
        Console.Write("ODOL -> MLOD -> ODOL: ");
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
        Console.WriteLine(ok ? "OK" : "MISMATCH");
        Console.WriteLine($"  LODs: {p3d.ODOL.Lods.Length} -> {odol.Lods.Length}");
        foreach (var origLod in p3d.ODOL.Lods)
        {
            var roundtripLod = odol.Lods.FirstOrDefault(l => l.Resolution == origLod.Resolution);
            if (roundtripLod != null)
                Console.WriteLine($"  LOD {origLod.Resolution:F1}: {origLod.Vertices.Count} verts -> {roundtripLod.Vertices.Count} verts");
        }
    }
    else if (p3d.MLOD != null)
    {
        Console.Write("MLOD -> ODOL -> MLOD: ");
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
        Console.WriteLine(ok ? "OK" : "MISMATCH");
        Console.WriteLine($"  LODs: {p3d.MLOD.Lods.Length} -> {mlod.Lods.Length}");
        foreach (var origLod in p3d.MLOD.Lods)
        {
            var roundtripLod = mlod.Lods.FirstOrDefault(l => l.Resolution == origLod.Resolution);
            if (roundtripLod != null)
                Console.WriteLine($"  LOD {origLod.Resolution:F1}: {origLod.Points.Length} points -> {roundtripLod.Points.Length} points");
        }
    }
}

static void HandlePAAAnalyze(string path)
{
    var analysis = BIS.PAA.PaaAnalyzer.Analyze(path);
    Console.WriteLine($"Format: {analysis.Format}");
    Console.WriteLine($"Size: {analysis.Width}x{analysis.Height}");
    Console.WriteLine($"MIP maps: {analysis.MipmapCount}");
    Console.WriteLine($"Alpha: {(analysis.HasAlpha ? "yes" : "no")}");
    Console.WriteLine($"Transparency: {(analysis.IsTransparent ? "yes" : "no")}");
    Console.WriteLine($"Category: {analysis.CategoryLabel}");
}

static void HandlePAASuggest(string path)
{
    var analysis = BIS.PAA.PaaAnalyzer.Analyze(path);
    var suggestion = BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
    Console.WriteLine($"Current: {analysis.Format}");
    Console.WriteLine($"Optimal: {suggestion.RecommendedFormat}");
    Console.WriteLine($"Rationale: {suggestion.Rationale}");
    Console.WriteLine($"Size factor: {suggestion.EstimatedSizeFactor:F3}x");
    if (!string.IsNullOrEmpty(suggestion.Notes))
        Console.WriteLine($"Notes: {suggestion.Notes}");
}

static void HandlePBOList(string path)
{
    var pbo = new BIS.PBO.PBO(path);
    Console.WriteLine($"File: {Path.GetFileName(path)}");
    Console.WriteLine($"Entries: {pbo.Files.Count}");

    long totalSize = 0;
    foreach (var entry in pbo.Files)
    {
        int size = entry.Size;
        totalSize += size;
        string packInfo = entry.IsCompressed ? " (packed)" : "";
        Console.WriteLine($"  {entry.FileName,-40} {size,8} bytes{packInfo}");
    }
    Console.WriteLine($"Total: {totalSize} bytes");
}

static void HandlePBOExtract(string path, DirectoryInfo? outputDir)
{
    var outDir = outputDir?.FullName ?? Path.GetFileNameWithoutExtension(path);
    Directory.CreateDirectory(outDir);

    var pbo = new BIS.PBO.PBO(path);
    pbo.ExtractFiles(pbo.Files, outDir);
    Console.WriteLine($"Extracted {pbo.Files.Count} files to {outDir}");
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
    Console.WriteLine($"Packed {files.Length} files{compressInfo} -> {outPath}  (prefix: {prefixVal})");

    // Validate the generated PBO
    try
    {
        var linter = new PboLinter();
        var linterDiags = linter.Lint(pbo);
        if (linterDiags.Count > 0)
        {
            Console.WriteLine("Issues:");
            foreach (var d in linterDiags)
                Console.WriteLine($"  {d}");
        }
        else
        {
            Console.WriteLine("No issues found.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Validation error: {ex.Message}");
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

    foreach (var file in files)
    {
        var source = File.ReadAllText(file);
        var formatted = formatter.Format(source);

        if (formatted != source)
        {
            anyChanged = true;
            totalChanged++;
            if (check)
            {
                Console.WriteLine($"{file}: would reformat");
            }
            else
            {
                File.WriteAllText(file, formatted);
            }
        }
    }
    sw.Stop();

    if (check)
    {
        var verb = anyChanged ? "some would be reformatted" : "all files are clean";
        Console.WriteLine($"\n{files.Length} file(s) checked, {totalChanged} would reformat in {sw.ElapsedMilliseconds / 1000.0:F1}s.");
    }
    else
    {
        var verb = anyChanged ? "reformatted" : "all clean";
        Console.WriteLine($"\n{files.Length} file(s) processed, {totalChanged} {verb} in {sw.ElapsedMilliseconds / 1000.0:F1}s.");
    }
    return anyChanged ? 1 : 0;
}

static void HandleConfigSerialize(string path, FileInfo output)
{
    var parser = new ConfigParser();
    var config = parser.ParseFile(path);
    using var stream = File.Create(output.FullName);
    ConfigSerializer.Serialize(config, stream);
    Console.WriteLine($"Serialized config to {output.FullName}");
}
