using System.IO;
using BIS.Core.Config;
using Xunit;

namespace BIS.IntegrationTest;

public class RvmatIntegrationTests
{
    /// <summary>Binarized RVMAT header: \0raP</summary>
    private static readonly byte[] BinRvmatHeader = [0x00, 0x72, 0x61, 0x50];

    private static bool IsTextRvmat(string file)
    {
        var header = new byte[4];
        using var s = File.OpenRead(file);
        if (s.Read(header, 0, 4) < 4)
            return false;
        return header[0] != 0x00 || header[1] != 0x72 || header[2] != 0x61 || header[3] != 0x50;
    }

    private static string ReadRvmatText(string file)
    {
        if (IsTextRvmat(file))
            return File.ReadAllText(file);
        using var input = File.OpenRead(file);
        using var output = new MemoryStream();
        ConfigSerializer.Serialize(input, output);
        output.Position = 0;
        return new StreamReader(output).ReadToEnd();
    }

    [Fact]
    public void Read_ValidRvmat_HasConfigStructure()
    {
        if (!TestData.CheckAvailable("rvmat", "Install Arma 3 Samples on Steam.")) return;

        var files = TestData.GetFiles("rvmat", "*.rvmat");
        foreach (var file in files)
        {
            if (!IsTextRvmat(file))
                continue; // Skip binarized RVMATs — they need config.bin parsing

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

        var text = ReadRvmatText(file);
        Assert.Contains("class", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(text.Length > 10);
    }

    [Fact]
    public void Read_MultipleRvmat_ValidFormat()
    {
        var files = TestData.GetFiles("rvmat", "*.rvmat");
        if (files.Length == 0) return;

        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.True(bytes.Length > 4);
            // Accept both text RVMATs (non-null first byte) and binarized RVMATs (\0raP header)
            bool isText = bytes[0] != 0;
            bool isBinarized = bytes.Length >= 4
                && bytes[0] == BinRvmatHeader[0]
                && bytes[1] == BinRvmatHeader[1]
                && bytes[2] == BinRvmatHeader[2]
                && bytes[3] == BinRvmatHeader[3];
            Assert.True(isText || isBinarized, $"RVMAT {file} is neither text nor binarized format");
        }
    }
}
