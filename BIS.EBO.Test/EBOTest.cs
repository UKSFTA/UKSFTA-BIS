using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BIS.EBO;
using BIS.EBO.Models;
using BIS.PBO;
using Xunit;

namespace BIS.EBO.Test;

public class EboTest
{
    private static readonly byte[] TestKey = "TEST_EBO_KEY_1234"u8.ToArray();

    private static byte[] CreateMinimalEbo(byte[] rc4Key)
    {
        using var ms = new MemoryStream();
        var plaintext = "Hello World!\n"u8.ToArray();
        var encrypted = RC4.Decrypt(plaintext, rc4Key);

        // Version entry: empty filename + VersionMagic (21 bytes)
        ms.Write(new byte[] { 0 });
        ms.Write(BitConverter.GetBytes(FileEntry.VersionMagic));
        ms.Write(BitConverter.GetBytes(0)); // originalSize
        ms.Write(BitConverter.GetBytes(0)); // reserved
        ms.Write(BitConverter.GetBytes(0)); // timestamp
        ms.Write(BitConverter.GetBytes(0)); // dataSize

        // Header extensions: prefix = testprefix
        ms.Write("prefix"u8.ToArray());
        ms.Write(new byte[] { 0 });
        ms.Write("testprefix"u8.ToArray());
        ms.Write(new byte[] { 0 });
        ms.Write(new byte[] { 0 }); // end of extensions

        // File entry header: "hello.txt" with EncryptionMagic (21 + 10 bytes)
        ms.Write("hello.txt"u8.ToArray());
        ms.Write(new byte[] { 0 });
        ms.Write(BitConverter.GetBytes(FileEntry.EncryptionMagic));
        ms.Write(BitConverter.GetBytes(plaintext.Length)); // originalSize
        ms.Write(BitConverter.GetBytes(0)); // reserved
        ms.Write(BitConverter.GetBytes(0)); // timestamp
        ms.Write(BitConverter.GetBytes(encrypted.Length)); // dataSize

        // End of file entries marker (21 bytes: empty filename + 20 zero bytes)
        ms.Write(new byte[] { 0 });
        ms.Write(new byte[20]);

        // File data (after ALL entry headers — matches PBO layout)
        ms.Write(encrypted);

        // Footer: 0x00 + SHA1
        var bodyBytes = ms.ToArray();
        var sha1 = SHA1.HashData(bodyBytes);
        ms.Write(new byte[] { 0 });
        ms.Write(sha1);

        return ms.ToArray();
    }

    [Fact]
    public void Constructor_ReadsHeader_WithKey()
    {
        var eboBytes = CreateMinimalEbo(TestKey);
        var tempPath = Path.GetTempFileName() + ".ebo";
        try
        {
            File.WriteAllBytes(tempPath, eboBytes);
            using var ebo = new EBO(tempPath, TestKey);

            Assert.Equal("testprefix", ebo.Prefix);
            Assert.Single(ebo.Files);
            Assert.Equal("hello.txt", ebo.Files[0].FileName);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Constructor_ReadsHeader_WithoutKey()
    {
        var eboBytes = CreateMinimalEbo(TestKey);
        var tempPath = Path.GetTempFileName() + ".ebo";
        try
        {
            File.WriteAllBytes(tempPath, eboBytes);
            using var ebo = new EBO(tempPath);

            Assert.Equal("testprefix", ebo.Prefix);
            Assert.Single(ebo.Files);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ReadFileData_DecryptsCorrectly()
    {
        var eboBytes = CreateMinimalEbo(TestKey);
        var tempPath = Path.GetTempFileName() + ".ebo";
        try
        {
            File.WriteAllBytes(tempPath, eboBytes);
            using var ebo = new EBO(tempPath, TestKey);

            var entry = ebo.Files[0];
            using var stream = entry.OpenRead();
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            Assert.Equal("Hello World!\n", content);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ReadFileData_WrongKey_ProducesGibberish()
    {
        var eboBytes = CreateMinimalEbo(TestKey);
        var tempPath = Path.GetTempFileName() + ".ebo";
        var wrongKey = "WRONG_KEY_1234567"u8.ToArray();
        try
        {
            File.WriteAllBytes(tempPath, eboBytes);
            using var ebo = new EBO(tempPath, wrongKey);

            var entry = ebo.Files[0];
            using var stream = entry.OpenRead();
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            Assert.NotEqual("Hello World!\n", content);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ReadFileData_WithoutKey_Throws()
    {
        var eboBytes = CreateMinimalEbo(TestKey);
        var tempPath = Path.GetTempFileName() + ".ebo";
        try
        {
            File.WriteAllBytes(tempPath, eboBytes);
            using var ebo = new EBO(tempPath);

            var entry = ebo.Files[0];
            Assert.Throws<InvalidOperationException>(() => entry.OpenRead());
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Constructor_NullKey_Throws()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentNullException>(() => new EBO(tempFile, null));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RC4_Decrypt_Symmetric()
    {
        var key = "testkey"u8.ToArray();
        var original = "Hello, RC4!"u8.ToArray();

        var encrypted = RC4.Decrypt(original, key);
        Assert.NotEqual(original, encrypted);

        var decrypted = RC4.Decrypt(encrypted, key);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void RC4_Decrypt_EmptyData()
    {
        var result = RC4.Decrypt(Array.Empty<byte>(), TestKey);
        Assert.Empty(result);
    }

    [Fact]
    public void RC4_Decrypt_LongData_ProducesConsistentOutput()
    {
        var data = new byte[10000];
        new Random(42).NextBytes(data);

        var encrypted = RC4.Decrypt(data, TestKey);
        var decrypted = RC4.Decrypt(encrypted, TestKey);

        Assert.Equal(data, decrypted);
    }
}
