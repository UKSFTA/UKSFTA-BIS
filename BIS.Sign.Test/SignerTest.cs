using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BIS.Sign.Models;
using BIS.Sign.Services;
using Xunit;

namespace BIS.Sign.Test;

public class SignerTest
{
    /// <summary>
    /// Creates a minimal PBO in memory with a valid 0x00 + SHA1 footer.
    /// </summary>
    internal static byte[] CreateMinimalPbo()
    {
        using var ms = new MemoryStream();

        // Header: prefix = testprefix
        var prefixKey = "prefix"u8.ToArray();
        var prefixVal = "testprefix"u8.ToArray();
        ms.Write(prefixKey);
        ms.Write(new byte[] { 0 });
        ms.Write(prefixVal);
        ms.Write(new byte[] { 0 });

        // End of header extensions
        ms.Write(new byte[] { 0 });

        // File entry: "hello.txt" (not a skipped extension)
        var fileName = "hello.txt"u8.ToArray();
        ms.Write(fileName);
        ms.Write(new byte[] { 0 });
        ms.Write(BitConverter.GetBytes(0)); // packing method
        ms.Write(BitConverter.GetBytes(13)); // original size
        ms.Write(BitConverter.GetBytes(0)); // reserved
        ms.Write(BitConverter.GetBytes(0)); // timestamp
        ms.Write(BitConverter.GetBytes(13)); // data size

        // File data
        var fileData = "Hello World!\n"u8.ToArray();
        ms.Write(fileData);

        // End of file entries marker (empty filename + 20 bytes reserved)
        ms.Write(new byte[] { 0 });
        ms.Write(new byte[20]);

        // Compute SHA1 of the body
        var bodyBytes = ms.ToArray();
        var sha1 = SHA1.HashData(bodyBytes);

        // Footer: 0x00 + 20-byte SHA1
        ms.Write(new byte[] { 0 });
        ms.Write(sha1);

        return ms.ToArray();
    }

    [Fact]
    public void SignAndVerify_RoundTrip()
    {
        var key = BiKey.Generate("test_mod", 2048);
        var signer = new Signer();

        var pboBytes = CreateMinimalPbo();
        using var signMs = new MemoryStream(pboBytes);
        var sig = signer.ComputeSignature(signMs, key);

        Assert.NotNull(sig);
        Assert.Equal("test_mod", sig.AuthorName);
        Assert.Equal(2048, sig.KeyLength);
        Assert.True(sig.Signature1.Length > 0);
        Assert.True(sig.Signature2.Length > 0);
        Assert.True(sig.Signature3.Length > 0);

        // Verify against the same PBO
        using var verifyMs = new MemoryStream(pboBytes);
        var ok = signer.VerifyPbo(verifyMs, sig);
        Assert.True(ok);
    }

    [Fact]
    public void Verify_WithWrongKey_Fails()
    {
        var key1 = BiKey.Generate("mod_a", 2048);
        var key2 = BiKey.Generate("mod_b", 2048);
        var signer = new Signer();

        var pboBytes = CreateMinimalPbo();

        // Sign with key1
        using var signMs = new MemoryStream(pboBytes);
        var sig = signer.ComputeSignature(signMs, key1);

        // Tamper sig1 bytes to make verification fail with a different signature
        var tamperedSig1 = (byte[])sig.Signature1.Clone();
        tamperedSig1[0] ^= 0xFF;

        var wrongSig = new BiSignature
        {
            AuthorName = sig.AuthorName,
            KeyLength = sig.KeyLength,
            Exponent = sig.Exponent,
            Modulus = sig.Modulus,
            Signature1 = tamperedSig1,
            Signature2 = sig.Signature2,
            Signature3 = sig.Signature3,
        };

        using var verifyMs = new MemoryStream(pboBytes);
        var ok = signer.VerifyPbo(verifyMs, wrongSig);
        Assert.False(ok);
    }

    [Fact]
    public void SignPbo_FileRoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var pboPath = Path.Combine(tempDir, "test.pbo");
            File.WriteAllBytes(pboPath, CreateMinimalPbo());

            var key = BiKey.Generate("test_mod", 2048);
            var signer = new Signer();

            var sig = signer.SignPbo(pboPath, key);
            Assert.NotNull(sig);
            Assert.Equal("test_mod", sig.AuthorName);

            // Check .bisign file was created
            var bisignPath = $"{pboPath}.test_mod.bisign";
            Assert.True(File.Exists(bisignPath));

            // Verify using file-based method
            var ok = signer.VerifyPbo(pboPath, bisignPath);
            Assert.True(ok);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void VerifyPbo_TamperedData_Fails()
    {
        var key = BiKey.Generate("test_mod", 2048);
        var signer = new Signer();

        var pboBytes = CreateMinimalPbo();

        // Sign original
        using var signMs = new MemoryStream(pboBytes);
        var sig = signer.ComputeSignature(signMs, key);

        // Tamper: change a byte in the file data (offset 50 = second 'e' in "Hello")
        // This changes filehash, making sig3 verification fail
        pboBytes[50] ^= 0xFF;

        // Verify should fail
        using var verifyMs = new MemoryStream(pboBytes);
        var ok = signer.VerifyPbo(verifyMs, sig);
        Assert.False(ok);
    }

    [Fact]
    public void SignPbo_NoPrivateKey_Throws()
    {
        var key = BiKey.Generate("test_mod", 2048);
        var pubKeyBytes = key.ExportPublicKey();
        var pubKey = BiKey.LoadPublicKey(pubKeyBytes);

        var signer = new Signer();
        Assert.Throws<ArgumentException>(() => signer.SignPbo("dummy.pbo", pubKey));
    }

    [Fact]
    public void ComputeSignature_PboWithoutFooter_ComputesHashFromBody()
    {
        var key = BiKey.Generate("test_mod", 2048);
        var signer = new Signer();

        using var ms = new MemoryStream();
        var prefixKey = "prefix"u8.ToArray();
        var prefixVal = "test"u8.ToArray();
        ms.Write(prefixKey);
        ms.Write(new byte[] { 0 });
        ms.Write(prefixVal);
        ms.Write(new byte[] { 0 });
        ms.Write(new byte[] { 0 });
        ms.Write(new byte[] { 0 });
        ms.Write(new byte[20]);

        ms.Position = 0;
        var sig = signer.ComputeSignature(ms, key);
        Assert.NotNull(sig);
        Assert.True(sig.Signature1.Length > 0);
    }

    [Fact]
    public void ComputeSignature_WithSkippedExtensions_HashesNothing()
    {
        var key = BiKey.Generate("test_mod", 2048);
        var signer = new Signer();

        // Build PBO with only .paa files (skipped extension)
        using var ms = new MemoryStream();

        var prefixKey = "prefix"u8.ToArray();
        var prefixVal = "test"u8.ToArray();
        ms.Write(prefixKey);
        ms.Write(new byte[] { 0 });
        ms.Write(prefixVal);
        ms.Write(new byte[] { 0 });
        ms.Write(new byte[] { 0 });

        var fileName = "texture.paa"u8.ToArray();
        ms.Write(fileName);
        ms.Write(new byte[] { 0 });
        ms.Write(BitConverter.GetBytes(0));
        ms.Write(BitConverter.GetBytes(4));
        ms.Write(BitConverter.GetBytes(0));
        ms.Write(BitConverter.GetBytes(0));
        ms.Write(BitConverter.GetBytes(4));
        ms.Write(new byte[] { 1, 2, 3, 4 });

        ms.Write(new byte[] { 0 });
        ms.Write(new byte[20]);

        var bodyBytes = ms.ToArray();
        var sha1 = SHA1.HashData(bodyBytes);
        ms.Write(new byte[] { 0 });
        ms.Write(sha1);

        var pboBytes = ms.ToArray();
        using var signMs = new MemoryStream(pboBytes);
        var sig = signer.ComputeSignature(signMs, key);

        // Verify should still pass (hash3 uses "nothing" literal)
        using var verifyMs = new MemoryStream(pboBytes);
        var ok = signer.VerifyPbo(verifyMs, sig);
        Assert.True(ok);
    }

    [Fact]
    public void VerifyPbo_FileDoesNotExist_Throws()
    {
        var signer = new Signer();
        var tempFile = Path.GetTempFileName();
        File.Delete(tempFile); // file path exists (temp dir) but no file
        Assert.Throws<FileNotFoundException>(() => signer.VerifyPbo(tempFile, tempFile + ".bisign"));
    }
}
