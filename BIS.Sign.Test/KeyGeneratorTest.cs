using System.IO;
using BIS.Sign.Services;
using Xunit;

namespace BIS.Sign.Test;

public class KeyGeneratorTest
{
    [Fact]
    public void GenerateToFile_CreatesBothKeyFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var gen = new KeyGenerator();
            var key = gen.Generate("test_mod", tempDir, 2048);

            var privatePath = Path.Combine(tempDir, "test_mod.biprivatekey");
            var publicPath = Path.Combine(tempDir, "test_mod.bikey");

            Assert.True(File.Exists(privatePath), "Private key file should exist");
            Assert.True(File.Exists(publicPath), "Public key file should exist");

            var privateBytes = File.ReadAllBytes(privatePath);
            var publicBytes = File.ReadAllBytes(publicPath);

            Assert.True(privateBytes.Length > 0);
            Assert.True(publicBytes.Length > 0);
            Assert.True(privateBytes.Length > publicBytes.Length);

            Assert.Equal("test_mod", key.AuthorName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GenerateInMemory_ReturnsValidKey()
    {
        var gen = new KeyGenerator();
        var key = gen.GenerateInMemory("test_mod", 2048);

        Assert.NotNull(key);
        Assert.Equal("test_mod", key.AuthorName);
        Assert.Equal(2048, key.KeyLength);
        Assert.True(key.HasPrivateKey);
    }

    [Fact]
    public void GenerateToFile_1024Bit_Works()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var gen = new KeyGenerator();
            gen.Generate("test_mod", tempDir, 1024);

            var publicPath = Path.Combine(tempDir, "test_mod.bikey");
            var privatePath = Path.Combine(tempDir, "test_mod.biprivatekey");

            Assert.True(File.Exists(publicPath));
            Assert.True(File.Exists(privatePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GenerateToFile_GeneratedKeys_CanRoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var gen = new KeyGenerator();
            gen.Generate("roundtrip_test", tempDir, 2048);

            var privatePath = Path.Combine(tempDir, "roundtrip_test.biprivatekey");
            var publicPath = Path.Combine(tempDir, "roundtrip_test.bikey");

            var loadedPrivate = Models.BiKey.LoadPrivateKey(privatePath);
            var loadedPublic = Models.BiKey.LoadPublicKey(publicPath);

            Assert.Equal("roundtrip_test", loadedPrivate.AuthorName);
            Assert.Equal("roundtrip_test", loadedPublic.AuthorName);
            Assert.True(loadedPrivate.HasPrivateKey);
            Assert.False(loadedPublic.HasPrivateKey);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
