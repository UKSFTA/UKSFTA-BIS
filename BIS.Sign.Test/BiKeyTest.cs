using System.IO;
using System.Security.Cryptography;
using BIS.Sign.Models;
using Xunit;

namespace BIS.Sign.Test;

public class BiKeyTest
{
    [Fact]
    public void Generate_CreatesKeyWithCorrectKeyLength()
    {
        var key = BiKey.Generate("test_mod", 2048);
        Assert.Equal(2048, key.KeyLength);
        Assert.True(key.HasPrivateKey);
    }

    [Fact]
    public void Generate_1024Bit_Works()
    {
        var key = BiKey.Generate("test_mod", 1024);
        Assert.Equal(1024, key.KeyLength);
        Assert.Equal("test_mod", key.AuthorName);
    }

    [Fact]
    public void Generate_4096Bit_Works()
    {
        var key = BiKey.Generate("test_mod", 4096);
        Assert.Equal(4096, key.KeyLength);
    }

    [Fact]
    public void Generate_UnsupportedKeyLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => BiKey.Generate("test_mod", 512));
    }

    [Fact]
    public void Generate_EmptyAuthorName_Throws()
    {
        Assert.Throws<ArgumentException>(() => BiKey.Generate(""));
    }

    [Fact]
    public void Generate_NullAuthorName_Throws()
    {
        Assert.Throws<ArgumentException>(() => BiKey.Generate(null!));
    }

    [Fact]
    public void Generate_TooLongAuthorName_Throws()
    {
        var longName = new string('a', 513);
        Assert.Throws<ArgumentException>(() => BiKey.Generate(longName));
    }

    [Fact]
    public void ExportPublicKey_ImportPublicKey_RoundTrip()
    {
        var key = BiKey.Generate("test_mod", 2048);
        var exported = key.ExportPublicKey();
        Assert.NotNull(exported);
        Assert.True(exported.Length > 0);

        var imported = BiKey.LoadPublicKey(exported);
        Assert.Equal("test_mod", imported.AuthorName);
        Assert.Equal(2048, imported.KeyLength);
        Assert.False(imported.HasPrivateKey);
    }

    [Fact]
    public void ExportPrivateKey_ImportPrivateKey_RoundTrip()
    {
        var key = BiKey.Generate("test_mod", 2048);
        var exported = key.ExportPrivateKey();
        Assert.NotNull(exported);

        var imported = BiKey.LoadPrivateKey(exported);
        Assert.Equal("test_mod", imported.AuthorName);
        Assert.Equal(2048, imported.KeyLength);
        Assert.True(imported.HasPrivateKey);
    }

    [Fact]
    public void SaveAndLoadPublicKey_FileRoundTrip()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var key = BiKey.Generate("test_mod", 2048);
            key.SavePublicKey(tempFile);

            var fileBytes = File.ReadAllBytes(tempFile);
            Assert.True(fileBytes.Length > 0);

            var imported = BiKey.LoadPublicKey(tempFile);
            Assert.Equal("test_mod", imported.AuthorName);
            Assert.Equal(2048, imported.KeyLength);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void SaveAndLoadPrivateKey_FileRoundTrip()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var key = BiKey.Generate("test_mod", 2048);
            key.SavePrivateKey(tempFile);

            var imported = BiKey.LoadPrivateKey(tempFile);
            Assert.Equal("test_mod", imported.AuthorName);
            Assert.Equal(2048, imported.KeyLength);
            Assert.True(imported.HasPrivateKey);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadPublicKey_InvalidData_Throws()
    {
        var data = new byte[] { (byte)'a', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Throws<InvalidDataException>(() => BiKey.LoadPublicKey(data));
    }

    [Fact]
    public void LoadPrivateKey_InvalidData_Throws()
    {
        var data = new byte[] { (byte)'a', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Throws<InvalidDataException>(() => BiKey.LoadPrivateKey(data));
    }

    [Fact]
    public void GeneratedKey_CanSignAndVerify()
    {
        var key = BiKey.Generate("test_mod", 2048);
        var rsa = key.Rsa;

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var ok = rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.True(ok);
    }

    [Fact]
    public void ImportedPublicKey_VerifiesSignature()
    {
        var original = BiKey.Generate("test_mod", 2048);
        var exported = original.ExportPublicKey();
        var pubKey = BiKey.LoadPublicKey(exported);

        var data = new byte[] { 10, 20, 30, 40 };
        var sig = original.Rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var ok = pubKey.Rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.True(ok);
    }

}
