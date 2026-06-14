using Xunit;

namespace BIS.IntegrationTest;

public class P3dIntegrationTests
{
    [Fact]
    public void Read_ValidP3d_Succeeds()
    {
        if (!TestData.CheckAvailable("p3d", "Install Arma 3 Samples on Steam.")) return;

        var files = TestData.GetFiles("p3d", "*.p3d");
        foreach (var file in files)
        {
            using var stream = System.IO.File.OpenRead(file);
            var p3d = new global::BIS.P3D.P3D(stream);
            Assert.NotNull(p3d);
            Assert.NotNull(p3d.LODs);
        }
    }

    [Fact]
    public void Read_ValidP3d_HasLODs()
    {
        var file = TestData.GetFile("p3d", "*.p3d");
        if (file == null) return;

        using var stream = System.IO.File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(stream);
        Assert.NotEmpty(p3d.LODs);
    }

    [Fact]
    public void Read_NonExistentFile_Throws()
    {
        var missingFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "__nonexistent_test_file__.p3d");
        Assert.Throws<System.IO.FileNotFoundException>(() =>
            new global::BIS.P3D.P3D(System.IO.File.OpenRead(missingFile)));
    }
}
