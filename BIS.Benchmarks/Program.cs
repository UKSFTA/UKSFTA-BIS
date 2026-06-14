using System.Reflection;
using BenchmarkDotNet.Running;

var testDataRoot = FindTestData();
if (testDataRoot == null)
{
    Console.Error.WriteLine("_testdata/ not found. Run _testdata/download.sh first.");
    return 1;
}

TestDataPath.Root = testDataRoot;
BenchmarkRunner.Run(Assembly.GetExecutingAssembly());
return 0;

static string? FindTestData()
{
    var dir = AppContext.BaseDirectory;
    for (int i = 0; i < 10; i++)
    {
        var candidate = Path.Combine(dir, "_testdata");
        if (Directory.Exists(candidate)) return candidate;
        var parent = Directory.GetParent(dir);
        if (parent == null) break;
        dir = parent.FullName;
    }
    return null;
}

public static class TestDataPath
{
    public static string? Root { get; set; }

    public static string[] GetFiles(string formatDir, string pattern)
    {
        if (Root == null) return [];
        var dir = Path.Combine(Root, formatDir);
        return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : [];
    }

    public static string? GetFile(string formatDir, string pattern)
    {
        var files = GetFiles(formatDir, pattern);
        return files.Length > 0 ? files[0] : null;
    }

    public static byte[] ReadAllBytes(string formatDir, string pattern, long maxBytes = long.MaxValue)
    {
        var files = GetFiles(formatDir, pattern);
        if (files.Length == 0) return [];
        using var combined = new MemoryStream();
        foreach (var f in files)
        {
            var chunk = File.ReadAllBytes(f);
            if (combined.Position + chunk.Length > maxBytes)
            {
                var remaining = (int)(maxBytes - combined.Position);
                combined.Write(chunk, 0, remaining);
                break;
            }
            combined.Write(chunk, 0, chunk.Length);
        }
        return combined.ToArray();
    }
}
