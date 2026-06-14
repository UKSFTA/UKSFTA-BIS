using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BIS.Sign.Models;
using BIS.Sign.Services;
using Xunit;

namespace BIS.Sign.Test;

public class BiSignatureTest
{
    private static BiSignature CreateTestSignature(string authorName = "test_mod")
    {
        var key = BiKey.Generate(authorName, 2048);
        var signer = new Signer();

        var pboBytes = CreateMinimalPbo();
        using var ms = new MemoryStream(pboBytes);
        return signer.ComputeSignature(ms, key);
    }

    private static byte[] CreateMinimalPbo()
    {
        // Build a minimal PBO with 0x00 + SHA1 footer
        // Layout: header extensions → file entries + data → 0x00 + 20-byte SHA1
        using var ms = new MemoryStream();

        // Header: prefix = test
        var prefixKey = "prefix"u8.ToArray();
        var prefixVal = "myprefix"u8.ToArray();
        ms.Write(prefixKey);
        ms.Write(new byte[] { 0 });
        ms.Write(prefixVal);
        ms.Write(new byte[] { 0 });

        // End of header extensions
        ms.Write(new byte[] { 0 });

        // File entry: "readme.txt"
        var fileName = "readme.txt"u8.ToArray();
        ms.Write(fileName);
        ms.Write(new byte[] { 0 });
        ms.Write(BitConverter.GetBytes(0)); // packing method
        ms.Write(BitConverter.GetBytes(12)); // original size
        ms.Write(BitConverter.GetBytes(0)); // reserved
        ms.Write(BitConverter.GetBytes(0)); // timestamp
        ms.Write(BitConverter.GetBytes(12)); // data size

        // File data
        var fileData = "Hello World!"u8.ToArray();
        ms.Write(fileData);

        // End of file entries marker (empty filename + 20 bytes reserved)
        ms.Write(new byte[] { 0 });
        ms.Write(new byte[20]); // reserved/padding

        // Compute SHA1 of body before footer
        var bodyBytes = ms.ToArray();
        var sha1 = SHA1.HashData(bodyBytes);

        // Footer: 0x00 + 20-byte SHA1
        ms.Write(new byte[] { 0 });
        ms.Write(sha1);

        return ms.ToArray();
    }

    [Fact]
    public void ToByteArray_Load_RoundTrip()
    {
        var sig = CreateTestSignature();
        var bytes = sig.ToByteArray();
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        var loaded = BiSignature.Load(bytes);
        Assert.Equal(sig.AuthorName, loaded.AuthorName);
        Assert.Equal(sig.KeyLength, loaded.KeyLength);
        Assert.Equal(sig.Exponent, loaded.Exponent);
        Assert.True(sig.Modulus.SequenceEqual(loaded.Modulus));
        Assert.True(sig.Signature1.SequenceEqual(loaded.Signature1));
        Assert.True(sig.Signature2.SequenceEqual(loaded.Signature2));
        Assert.True(sig.Signature3.SequenceEqual(loaded.Signature3));
    }

    [Fact]
    public void Save_Load_FileRoundTrip()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var sig = CreateTestSignature();
            sig.Save(tempFile);

            var loaded = BiSignature.Load(tempFile);
            Assert.Equal(sig.AuthorName, loaded.AuthorName);
            Assert.Equal(sig.KeyLength, loaded.KeyLength);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ToPublicKey_ReturnsValidBiKey()
    {
        var sig = CreateTestSignature();
        var pubKey = sig.ToPublicKey();

        Assert.Equal(sig.AuthorName, pubKey.AuthorName);
        Assert.Equal(sig.KeyLength, pubKey.KeyLength);
        Assert.False(pubKey.HasPrivateKey);
    }

    [Fact]
    public void ToPublicKey_ResultCanVerifySignature()
    {
        var key = BiKey.Generate("test_mod", 2048);
        var signer = new Signer();
        var pboBytes = CreateMinimalPbo();

        using var ms = new MemoryStream(pboBytes);
        var sig = signer.ComputeSignature(ms, key);

        var pubKey = sig.ToPublicKey();
        using var verifyMs = new MemoryStream(pboBytes);
        var ok = signer.VerifyPbo(verifyMs, sig);
        Assert.True(ok);
    }

    [Fact]
    public void Load_InvalidMagic_Throws()
    {
        var data = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Throws<InvalidDataException>(() => BiSignature.Load(data));
    }

    [Fact]
    public void Load_InvalidData_Throws()
    {
        Assert.Throws<InvalidDataException>(() => BiSignature.Load(new byte[13]));
    }
}
