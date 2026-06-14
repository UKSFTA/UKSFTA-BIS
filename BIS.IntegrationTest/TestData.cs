namespace BIS.IntegrationTest;

/// <summary>
/// Discovers test data files from the solution-level _testdata/ directory.
/// Tests skip gracefully when data files aren't present.
/// </summary>
public static class TestData
{
    private static readonly string? _root;

    static TestData()
    {
        // Search upward from the assembly location for _testdata/
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "_testdata");
            if (Directory.Exists(candidate))
            {
                _root = candidate;
                return;
            }
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
    }

    public static string? Root => _root;

    /// <summary>Returns true if _testdata/ exists and is populated.</summary>
    public static bool IsAvailable => _root != null && Directory.Exists(_root);

    /// <summary>Returns the full path for a file in a format subdirectory, or null if not found.</summary>
    public static string? GetFile(string formatDir, string pattern)
    {
        if (_root == null) return null;
        var dir = Path.Combine(_root, formatDir);
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir, pattern);
        return files.Length > 0 ? files[0] : null;
    }

    /// <summary>Returns all files matching a pattern in a format subdirectory.</summary>
    public static string[] GetFiles(string formatDir, string pattern)
    {
        if (_root == null) return [];
        var dir = Path.Combine(_root, formatDir);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, pattern);
    }

    /// <summary>Returns the first file in a format subdirectory, or null if empty.</summary>
    public static string? GetFirstFile(string formatDir)
    {
        if (_root == null) return null;
        var dir = Path.Combine(_root, formatDir);
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir);
        return files.Length > 0 ? files[0] : null;
    }

    /// <summary>Check if test data is available. Returns false if not (caller should return early).</summary>
    public static bool CheckAvailable(string formatDir, string hint)
    {
        if (!IsAvailable)
        {
            return false;
        }
        var dir = Path.Combine(_root!, formatDir);
        if (!Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0)
        {
            return false;
        }
        return true;
    }
}
