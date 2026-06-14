using BIS.Core.Config;
using Xunit;

namespace BIS.IntegrationTest;

public class ConfigIntegrationTests
{
    [Fact]
    public void Decompile_ValidConfigBin_ProducesText()
    {
        if (!TestData.CheckAvailable("config", "Run _testdata/download.sh.")) return;

        var files = TestData.GetFiles("config", "config.bin");
        foreach (var file in files)
        {
            using var input = File.OpenRead(file);
            using var output = new MemoryStream();
            ConfigSerializer.Serialize(input, output);
            output.Position = 0;
            var text = new StreamReader(output).ReadToEnd();
            Assert.NotEmpty(text);
            Assert.StartsWith("#version 12", text);
        }
    }

    [Fact]
    public void Decompile_Roundtrip_ProducesValidConfig()
    {
        var file = TestData.GetFile("config", "config.bin");
        if (file == null) return;

        using var input = File.OpenRead(file);
        using var output = new MemoryStream();
        ConfigSerializer.Serialize(input, output);
        output.Position = 0;
        var text = new StreamReader(output).ReadToEnd();
        Assert.Contains("class", text);
        Assert.Contains("};", text);
    }

    [Fact]
    public void Parse_BinaryConfig_Succeeds()
    {
        var file = TestData.GetFile("config", "config.bin");
        if (file == null) return;

        using var input = File.OpenRead(file);
        var param = new ParamFile(input);
        Assert.NotNull(param);
    }
}
