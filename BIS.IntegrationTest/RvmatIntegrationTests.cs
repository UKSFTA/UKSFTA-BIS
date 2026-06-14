using Xunit;

namespace BIS.IntegrationTest;

public class RvmatIntegrationTests
{
    [Fact]
    public void Read_ValidRvmat_HasConfigStructure()
    {
        if (!TestData.CheckAvailable("rvmat", "Install Arma 3 Samples on Steam.")) return;

        var files = TestData.GetFiles("rvmat", "*.rvmat");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.NotEmpty(text);
            Assert.Matches(@"class\s+\w+", text);
        }
    }

    [Fact]
    public void Read_Rvmat_CanBeParsedAsConfig()
    {
        var file = TestData.GetFile("rvmat", "*.rvmat");
        if (file == null) return;

        var text = File.ReadAllText(file);
        Assert.Contains("class", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(text.Length > 10);
    }

    [Fact]
    public void Read_MultipleRvmat_AllTextBased()
    {
        var files = TestData.GetFiles("rvmat", "*.rvmat");
        if (files.Length == 0) return;

        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.True(bytes.Length > 4);
            Assert.NotEqual(0, bytes[0]);
        }
    }
}
