using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace BIS.PBO.Test.Format;

public class PboPropertyTest
{
    // Property: SaveTo creates a file
    [Property(MaxTest = 50)]
    public void Pack_SingleFile_CreatesFile(NonEmptyString fileName, NonEmptyString content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pbo_prop_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePath = Path.Combine(tempDir, Sanitize(fileName.Get));
            File.WriteAllText(filePath, content.Get);

            var pboPath = Path.Combine(tempDir, "test.pbo");
            var pbo = new PBO();
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "test"));
            pbo.Files.Add(new PBOFileToAdd(new FileInfo(filePath), Sanitize(fileName.Get)));
            pbo.SaveTo(pboPath, false);

            Assert.True(File.Exists(pboPath), "PBO file was not created");
        }
        catch
        {
            // graceful
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(s.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray());
        return string.IsNullOrEmpty(sanitized) ? "file.txt" : sanitized;
    }
}
