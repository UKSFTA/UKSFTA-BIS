using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BIS.Sign.Models
{
    /// <summary>
    /// Represents a BI (Bohemia Interactive) key pair or public key.
    /// Wraps an RSA instance with an associated author/signer name.
    /// </summary>
    public class BiKey
    {
        /// <summary>Maximum length of the author/signer name in bytes (including null terminator).</summary>
        public const int MaxNameLength = 512;

        /// <summary>Default key length in bits (2048 is standard for Arma 3).</summary>
        public const int DefaultKeyLength = 2048;

        /// <summary>Supported key lengths.</summary>
        public static readonly int[] SupportedKeyLengths = [1024, 2048, 4096];

        /// <summary>Public exponent value used by BI keys (65537 = 0x010001).</summary>
        public const int PublicExponent = 65537;

        private static readonly byte[] PrivateKeyMagic =
            [0x07, 0x02, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00];

        private static readonly byte[] PublicKeyMagic =
            [0x06, 0x02, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00];

        private static readonly byte[] Rsa1 = "RSA1"u8.ToArray();
        private static readonly byte[] Rsa2 = "RSA2"u8.ToArray();

        /// <summary>The author/signer name (e.g. "ace_3.5.1.0").</summary>
        public string AuthorName { get; init; }

        /// <summary>The RSA key length in bits.</summary>
        public int KeyLength { get; private set; }

        /// <summary>The RSA instance.</summary>
        public RSA Rsa { get; }

        /// <summary>Whether this key contains the private exponent (i.e. can sign).</summary>
        public bool HasPrivateKey { get; private set; }

        private BiKey(RSA rsa)
        {
            Rsa = rsa;
            KeyLength = rsa.KeySize;
            HasPrivateKey = TryCheckHasPrivateKey(rsa);
            AuthorName = string.Empty;
        }

        /// <summary>
        /// Creates a new BI key pair with a generated RSA key.
        /// </summary>
        public static BiKey Generate(string authorName, int keyLength = DefaultKeyLength)
        {
            if (!SupportedKeyLengths.Contains(keyLength))
                throw new ArgumentException($"Unsupported key length: {keyLength}. Supported: {string.Join(", ", SupportedKeyLengths)}");

            if (string.IsNullOrEmpty(authorName))
                throw new ArgumentException("Author name is required.");

            if (authorName.Length >= MaxNameLength)
                throw new ArgumentException($"Author name must be less than {MaxNameLength} characters.");

            var rsa = RSA.Create(keyLength);
            return new BiKey(rsa) { AuthorName = authorName };
        }

        /// <summary>
        /// Loads a private key from a .biprivatekey file.
        /// </summary>
        public static BiKey LoadPrivateKey(string path)
        {
            return LoadPrivateKey(File.ReadAllBytes(path));
        }

        /// <summary>
        /// Loads a private key from .biprivatekey raw bytes.
        /// </summary>
        public static BiKey LoadPrivateKey(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var authorName = ReadNullTerminatedString(reader, MaxNameLength);
            var totalSize = reader.ReadUInt32();
            var magic = reader.ReadBytes(8);

            if (!magic.SequenceEqual(PrivateKeyMagic))
                throw new InvalidDataException("Invalid private key magic bytes.");

            var keyType = reader.ReadBytes(4);
            if (!keyType.SequenceEqual(Rsa2))
                throw new InvalidDataException("Expected RSA2 key type.");

            var keyLength = reader.ReadInt32();
            var exponentLe = reader.ReadUInt32();

            var modulusLe = reader.ReadBytes(keyLength / 8);
            var modulus = ReverseBytes(modulusLe);

            var factorSize = keyLength / 16;
            var pLe = reader.ReadBytes(factorSize);
            var qLe = reader.ReadBytes(factorSize);
            var dmp1Le = reader.ReadBytes(factorSize);
            var dmq1Le = reader.ReadBytes(factorSize);
            var iqmpLe = reader.ReadBytes(factorSize);

            var dLe = reader.ReadBytes(keyLength / 8);
            var d = ReverseBytes(dLe);

            var exponentBe = BitConverter.GetBytes(exponentLe);
            Array.Reverse(exponentBe);

            var parameters = new RSAParameters
            {
                Modulus = modulus,
                Exponent = exponentBe,
                D = d,
                P = ReverseBytes(pLe),
                Q = ReverseBytes(qLe),
                DP = ReverseBytes(dmp1Le),
                DQ = ReverseBytes(dmq1Le),
                InverseQ = ReverseBytes(iqmpLe),
            };

            var rsa = RSA.Create();
            rsa.ImportParameters(parameters);

            return new BiKey(rsa) { AuthorName = authorName, HasPrivateKey = true };
        }

        /// <summary>
        /// Loads a public key from a .bikey file.
        /// </summary>
        public static BiKey LoadPublicKey(string path)
        {
            return LoadPublicKey(File.ReadAllBytes(path));
        }

        /// <summary>
        /// Loads a public key from .bikey raw bytes.
        /// </summary>
        public static BiKey LoadPublicKey(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var authorName = ReadNullTerminatedString(reader, MaxNameLength);
            var totalSize = reader.ReadUInt32();
            var magic = reader.ReadBytes(8);

            if (!magic.SequenceEqual(PublicKeyMagic))
                throw new InvalidDataException("Invalid public key magic bytes.");

            var keyType = reader.ReadBytes(4);
            if (!keyType.SequenceEqual(Rsa1))
                throw new InvalidDataException("Expected RSA1 key type.");

            var keyLength = reader.ReadInt32();
            var exponentLe = reader.ReadUInt32();
            var modulusLe = reader.ReadBytes(keyLength / 8);

            var exponentBe = BitConverter.GetBytes(exponentLe);
            Array.Reverse(exponentBe);

            var parameters = new RSAParameters
            {
                Modulus = ReverseBytes(modulusLe),
                Exponent = exponentBe,
            };

            var rsa = RSA.Create();
            rsa.ImportParameters(parameters);

            return new BiKey(rsa) { AuthorName = authorName };
        }

        /// <summary>
        /// Saves the private key to a .biprivatekey file.
        /// </summary>
        public void SavePrivateKey(string path)
        {
            File.WriteAllBytes(path, ExportPrivateKey());
        }

        /// <summary>
        /// Exports the private key as .biprivatekey raw bytes.
        /// </summary>
        public byte[] ExportPrivateKey()
        {
            if (!HasPrivateKey)
                throw new InvalidOperationException("This key does not contain a private key.");

            var parameters = Rsa.ExportParameters(true);
            var keyLength = KeyLength;
            var exponentLe = BitConverter.GetBytes(PublicExponent);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(AuthorName.ToCharArray());
            writer.Write((byte)0);

            var totalSize = keyLength / 16 * 9 + 20;
            writer.Write(totalSize);
            writer.Write(PrivateKeyMagic);
            writer.Write(Rsa2);
            writer.Write(keyLength);
            writer.Write(exponentLe);
            writer.Write(ReverseBytes(parameters.Modulus));
            writer.Write(ReverseBytes(parameters.P));
            writer.Write(ReverseBytes(parameters.Q));
            writer.Write(ReverseBytes(parameters.DP));
            writer.Write(ReverseBytes(parameters.DQ));
            writer.Write(ReverseBytes(parameters.InverseQ));
            writer.Write(ReverseBytes(parameters.D));

            return ms.ToArray();
        }

        /// <summary>
        /// Saves the public key to a .bikey file.
        /// </summary>
        public void SavePublicKey(string path)
        {
            File.WriteAllBytes(path, ExportPublicKey());
        }

        /// <summary>
        /// Exports the public key as .bikey raw bytes.
        /// </summary>
        public byte[] ExportPublicKey()
        {
            var parameters = Rsa.ExportParameters(false);
            var keyLength = KeyLength;
            var exponentLe = BitConverter.GetBytes(PublicExponent);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(AuthorName.ToCharArray());
            writer.Write((byte)0);

            var totalSize = keyLength / 8 + 20;
            writer.Write(totalSize);
            writer.Write(PublicKeyMagic);
            writer.Write(Rsa1);
            writer.Write(keyLength);
            writer.Write(exponentLe);
            writer.Write(ReverseBytes(parameters.Modulus));

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

        internal static byte[] ReverseBytes(byte[] data)
        {
            var result = new byte[data.Length];
            Array.Copy(data, result, data.Length);
            Array.Reverse(result);
            return result;
        }

        private static bool TryCheckHasPrivateKey(RSA rsa)
        {
            try
            {
                rsa.ExportParameters(true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
