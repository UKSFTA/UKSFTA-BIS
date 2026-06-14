namespace BIS.EBO.Models;

/// <summary>
/// Standard RC4 stream cipher implementation.
/// Used for decrypting EBO (encrypted PBO) data blocks.
/// </summary>
public static class RC4
{
    /// <summary>
    /// Decrypts data using the RC4 stream cipher.
    /// Due to RC4's symmetric nature, encryption and decryption are identical operations.
    /// </summary>
    public static byte[] Decrypt(byte[] data, byte[] key)
    {
        var s = new byte[256];
        for (int i = 0; i < 256; i++)
            s[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var result = new byte[data.Length];
        int ki = 0, kj = 0;
        for (int n = 0; n < data.Length; n++)
        {
            ki = (ki + 1) & 0xFF;
            kj = (kj + s[ki]) & 0xFF;
            (s[ki], s[kj]) = (s[kj], s[ki]);
            int k = (s[ki] + s[kj]) & 0xFF;
            result[n] = (byte)(data[n] ^ s[k]);
        }

        return result;
    }
}
