using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIS.Sign.Models
{
    /// <summary>
    /// Represents a complete BISign (.bisign) file containing RSA-SHA1 signatures
    /// for an Arma PBO file.
    /// </summary>
    public class BiSignature
    {
        private static readonly byte[] PublicKeyMagic =
            [0x06, 0x02, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00];

        private static readonly byte[] Rsa1 = "RSA1"u8.ToArray();

        /// <summary>The author/signer name (e.g. "my_mod").</summary>
        public string AuthorName { get; init; }

        /// <summary>RSA key length in bits.</summary>
        public int KeyLength { get; init; }

        /// <summary>Public exponent (little-endian).</summary>
        public uint Exponent { get; init; }

        /// <summary>Modulus (n) in little-endian byte order.</summary>
        public byte[] Modulus { get; init; }

        /// <summary>Signature 1: RSA signature of the embedded SHA1 hash.</summary>
        public byte[] Signature1 { get; init; }

        /// <summary>Signature 2: RSA signature of SHA1(hash1 || namehash || prefix).</summary>
        public byte[] Signature2 { get; init; }

        /// <summary>Signature 3: RSA signature of SHA1(filehash || namehash || prefix).</summary>
        public byte[] Signature3 { get; init; }

        /// <summary>
        /// Reads a .bisign file from disk.
        /// </summary>
        public static BiSignature Load(string path)
        {
            return Load(File.ReadAllBytes(path));
        }

        /// <summary>
        /// Reads a .bisign file from raw bytes.
        /// </summary>
        public static BiSignature Load(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var authorName = ReadNullTerminatedString(reader, 512);
            var totalSize = reader.ReadUInt32();
            var magic = reader.ReadBytes(8);

            if (!magic.SequenceEqual(PublicKeyMagic))
                throw new InvalidDataException("Invalid BISign magic bytes.");

            var keyType = reader.ReadBytes(4);
            if (!keyType.SequenceEqual(Rsa1))
                throw new InvalidDataException("Expected RSA1 key type in BISign file.");

            var keyLength = reader.ReadInt32();
            var exponent = reader.ReadUInt32();
            var modulusLe = reader.ReadBytes(keyLength / 8);

            var sig1Length = reader.ReadInt32();
            var sig1 = reader.ReadBytes(sig1Length);

            var additionalSigs = reader.ReadInt32();
            var sig2Length = reader.ReadInt32();
            var sig2 = reader.ReadBytes(sig2Length);
            var sig3Length = reader.ReadInt32();
            var sig3 = reader.ReadBytes(sig3Length);

            return new BiSignature
            {
                AuthorName = authorName,
                KeyLength = keyLength,
                Exponent = exponent,
                Modulus = modulusLe,
                Signature1 = sig1,
                Signature2 = sig2,
                Signature3 = sig3,
            };
        }

        /// <summary>
        /// Saves the signature to a .bisign file on disk.
        /// </summary>
        public void Save(string path)
        {
            File.WriteAllBytes(path, ToByteArray());
        }

        /// <summary>
        /// Serializes the signature to raw .bisign bytes.
        /// </summary>
        public byte[] ToByteArray()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(AuthorName.ToCharArray());
            writer.Write((byte)0);

            var totalSize = KeyLength / 8 + 20;
            writer.Write(totalSize);
            writer.Write(PublicKeyMagic);
            writer.Write(Rsa1);
            writer.Write(KeyLength);
            writer.Write(Exponent);
            writer.Write(Modulus);

            writer.Write(Signature1.Length);
            writer.Write(Signature1);
            writer.Write(2); // additional sigs count
            writer.Write(Signature2.Length);
            writer.Write(Signature2);
            writer.Write(Signature3.Length);
            writer.Write(Signature3);

            return ms.ToArray();
        }

        /// <summary>
        /// Converts to a BiKey for verification using the public key embedded in this signature.
        /// </summary>
        public BiKey ToPublicKey()
        {
            var bikeyBytes = BuildBikeyBytes();
            return BiKey.LoadPublicKey(bikeyBytes);
        }

        private byte[] BuildBikeyBytes()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(AuthorName.ToCharArray());
            writer.Write((byte)0);

            var totalSize = KeyLength / 8 + 20;
            writer.Write(totalSize);
            writer.Write(PublicKeyMagic);
            writer.Write(Rsa1);
            writer.Write(KeyLength);
            writer.Write(Exponent);
            writer.Write(Modulus);

            return ms.ToArray();
        }

        private static string ReadNullTerminatedString(BinaryReader reader, int maxLength)
        {
            var bytes = new List<byte>(maxLength);
            for (var i = 0; i < maxLength; i++)
            {
                var b = reader.ReadByte();
                if (b == 0)
                    break;
                bytes.Add(b);
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }
    }
}
