using BIS.PBO;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: PboExtract <pbo-path> <output-directory>");
    return 1;
}

var pboPath = args[0];
var outputDir = args[1];

if (!File.Exists(pboPath))
{
    Console.Error.WriteLine($"File not found: {pboPath}");
    return 1;
}

try
{
    Directory.CreateDirectory(outputDir);
    using var pbo = new PBO(pboPath);
    pbo.ExtractFiles(pbo.Files, outputDir);
    Console.WriteLine($"Extracted {pbo.Files.Count} files to {outputDir}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Extraction failed: {ex.Message}");
    return 1;
}
