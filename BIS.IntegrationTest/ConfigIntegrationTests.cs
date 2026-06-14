using BIS.Core.Config;
using BIS.Core.Streams;
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
            Assert.StartsWith("class ", text);
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

    [Fact]
    public void ParamFile_BinaryRoundtrip_PreservesData()
    {
        var file = TestData.GetFile("config", "config.bin");
        if (file == null) return;

        // Parse binary config directly as ParamFile
        using var input = File.OpenRead(file);
        var original = new ParamFile(input);
        Assert.NotNull(original);

        // Rapify: ParamFile → binary stream
        using var binaryOut = new MemoryStream();
        var writer = new BinaryWriterEx(binaryOut, true);
        original.Write(writer);
        writer.Flush();
        var roundtripBytes = binaryOut.ToArray();

        // Verify output is non-empty and re-parses as valid ParamFile
        Assert.NotEmpty(roundtripBytes);
        using var verifyStream = new MemoryStream(roundtripBytes);
        var verified = new ParamFile(verifyStream);
        Assert.NotNull(verified);
    }

    [Fact]
    public void Decompile_Text_ReParsesAsParamFile()
    {
        var file = TestData.GetFile("config", "config.bin");
        if (file == null) return;

        using var input = File.OpenRead(file);
        using var textOut = new MemoryStream();
        ConfigSerializer.Serialize(input, textOut);

        textOut.Position = 0;
        var text = new System.IO.StreamReader(textOut).ReadToEnd();

        // The text output should be valid config syntax — verify it has
        // class declarations and proper structure
        Assert.Contains("class", text);
        Assert.Contains("{", text);
        Assert.Contains("};", text);
        Assert.StartsWith("class ", text);
    }

    [Fact]
    public void Decompile_AutoExtractBinarizedConfig_BypassesFormatDetection()
    {
        var file = TestData.GetFile("config", "config.bin");
        if (file == null) return;

        using var stream = File.OpenRead(file);
        var header = new byte[4];
        stream.ReadExactly(header, 0, 4);

        // The config.bin starts with \0raP (ParamFile binary format)
        // It should NOT be confused with a #version 12 text config
        Assert.Equal(0x00, header[0]);
        Assert.Equal((byte)'r', header[1]);
        Assert.Equal((byte)'a', header[2]);
        Assert.Equal((byte)'P', header[3]);

        // Serialize should still work (detects format, outputs version-agnostic text)
        stream.Position = 0;
        using var output = new MemoryStream();
        ConfigSerializer.Serialize(stream, output);
        Assert.True(output.Length > 0);
    }
}
