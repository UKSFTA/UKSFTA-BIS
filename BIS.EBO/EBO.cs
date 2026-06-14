using System;
using System.IO;
using BIS.EBO.Models;
using BIS.PBO;

namespace BIS.EBO;

public class EBO : global::BIS.PBO.PBO
{
    private readonly byte[] _rc4Key;

    public EBO(string fileName, byte[] rc4Key)
        : base(rc4Key == null ? throw new ArgumentNullException(nameof(rc4Key)) : fileName)
    {
        _rc4Key = rc4Key;
    }

    public EBO(string fileName)
        : base(fileName)
    {
        _rc4Key = null;
    }

    protected override byte[] GetFileData(FileEntry entry)
    {
        var data = base.GetFileData(entry);

        if (entry.CompressedMagic == FileEntry.EncryptionMagic)
        {
            if (_rc4Key == null)
                throw new InvalidOperationException(
                    "Cannot decrypt EBO file data: no RC4 key provided. " +
                    "Open the EBO with the rc4Key parameter or extract the key from the game binary.");

            return RC4.Decrypt(data, _rc4Key);
        }

        return data;
    }
}
