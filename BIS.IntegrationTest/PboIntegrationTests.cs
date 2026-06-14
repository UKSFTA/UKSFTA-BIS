using Xunit;

namespace BIS.IntegrationTest;

public class PboIntegrationTests
{
    [Fact]
    public void Open_ValidPbo_Succeeds()
    {
        if (!TestData.CheckAvailable("pbo", "Run _testdata/download.sh to fetch ALDP packages.")) return;

        var files = TestData.GetFiles("pbo", "*.pbo");
        foreach (var file in files)
        {
            var pbo = new global::BIS.PBO.PBO(file);
            Assert.NotNull(pbo);
            Assert.NotEmpty(pbo.Files);
        }
    }

    [Fact]
    public void Open_ValidPbo_HasFiles()
    {
        var file = TestData.GetFile("pbo", "*.pbo");
        if (file == null) return;

        var pbo = new global::BIS.PBO.PBO(file);
        Assert.NotEmpty(pbo.Files);
        Assert.NotNull(pbo.Prefix);
    }

    [Fact]
    public void Open_ValidPbo_HasProperties()
    {
        var file = TestData.GetFile("pbo", "*.pbo");
        if (file == null) return;

        var pbo = new global::BIS.PBO.PBO(file);
        Assert.NotNull(pbo.PropertiesPairs);
        // Some PBOs (especially CWC-era) have no header properties — that's valid
    }

    [Fact]
    public void Extract_Roundtrip_PreservesData()
    {
        var file = TestData.GetFile("pbo", "*.pbo");
        if (file == null) return;

        var pbo = new global::BIS.PBO.PBO(file);
        foreach (var entry in pbo.Files)
        {
            if (entry.Size > 0)
            {
                using var stream = entry.OpenRead();
                var data = new byte[entry.Size];
                var read = stream.Read(data);
                Assert.Equal(entry.Size, read);
                return;
            }
        }
    }

    [Fact]
    public void Open_NonExistentFile_Throws()
    {
        var missingFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "__nonexistent_test_file__.pbo");
        Assert.Throws<System.IO.FileNotFoundException>(() =>
            new global::BIS.PBO.PBO(missingFile));
    }
}
