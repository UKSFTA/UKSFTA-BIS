using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BIS.Sign.Models;

namespace BIS.Sign.Services
{
    /// <summary>
    /// Signs and verifies Arma PBO files using the BI (Bohemia Interactive) BISign format.
    /// Produces .bisign files with three RSA-SHA1 signatures.
    /// </summary>
    public class Signer
    {
        // Known binary file extensions that Armake skips when computing filehash.
        private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".paa", ".jpg", ".p3d", ".tga", ".rvmat", ".lip",
            ".ogg", ".wss", ".png", ".rtm", ".pac", ".fxy", ".wrp"
        };

        /// <summary>
        /// Signs a PBO file and creates a corresponding .bisign file.
        /// </summary>
        public BiSignature SignPbo(string pboPath, BiKey privateKey, string signatureOutputPath = null)
        {
            if (!privateKey.HasPrivateKey)
                throw new ArgumentException("The provided key does not contain a private key.", nameof(privateKey));

            var pboBytes = File.ReadAllBytes(pboPath);
            var signature = ComputeSignature(new MemoryStream(pboBytes), privateKey);
            EmbedHashFooter(pboPath);

            signatureOutputPath ??= $"{pboPath}.{privateKey.AuthorName}.bisign";
            signature.Save(signatureOutputPath);

            return signature;
        }

        /// <summary>
        /// Computes a BISign signature for a PBO file without writing to disk.
        /// </summary>
        public BiSignature ComputeSignature(string pboPath, BiKey privateKey)
        {
            using var pboStream = File.OpenRead(pboPath);
            return ComputeSignature(pboStream, privateKey);
        }

        /// <summary>
        /// Computes a BISign signature from a PBO stream.
        /// </summary>
        public BiSignature ComputeSignature(Stream pboStream, BiKey privateKey)
        {
            var (hash1, namehash, filehash, prefix) = ComputePboHashes(pboStream);

            // hash2 = SHA1(hash1 || namehash || prefix)
            var hash2 = ComputeCombinedHash(hash1, namehash, prefix);

            // hash3 = SHA1(filehash || namehash || prefix)
            var hash3 = ComputeCombinedHash(filehash, namehash, prefix);

            var keyLengthBytes = privateKey.KeyLength / 8;

            // Sign with RSA (PKCS#1 v1.5) — output is big-endian
            var sig1Be = RsaSignHash(privateKey.Rsa, hash1);
            var sig2Be = RsaSignHash(privateKey.Rsa, hash2);
            var sig3Be = RsaSignHash(privateKey.Rsa, hash3);

            // Convert to little-endian for .bisign file format
            var sig1Le = ToLittleEndian(sig1Be, keyLengthBytes);
            var sig2Le = ToLittleEndian(sig2Be, keyLengthBytes);
            var sig3Le = ToLittleEndian(sig3Be, keyLengthBytes);

            // Export parameters for the public key section
            var parameters = privateKey.Rsa.ExportParameters(false);
            var exponentLe = BitConverter.GetBytes(BiKey.PublicExponent);
            var modulusLe = (byte[])parameters.Modulus.Clone();
            Array.Reverse(modulusLe);

            return new BiSignature
            {
                AuthorName = privateKey.AuthorName,
                KeyLength = privateKey.KeyLength,
                Exponent = BitConverter.ToUInt32(exponentLe),
                Modulus = modulusLe,
                Signature1 = sig1Le,
                Signature2 = sig2Le,
                Signature3 = sig3Le,
            };
        }

        /// <summary>
        /// Verifies a PBO file against a .bisign signature file.
        /// </summary>
        public bool VerifyPbo(string pboPath, string bisignPath)
        {
            var signature = BiSignature.Load(bisignPath);
            return VerifyPbo(pboPath, signature);
        }

        /// <summary>
        /// Verifies a PBO file against a BiSignature.
        /// </summary>
        public bool VerifyPbo(string pboPath, BiSignature signature)
        {
            using var pboStream = File.OpenRead(pboPath);
            return VerifyPbo(pboStream, signature);
        }

        /// <summary>
        /// Verifies a PBO stream against a BiSignature.
        /// </summary>
        public bool VerifyPbo(Stream pboStream, BiSignature signature)
        {
            var (hash1, namehash, filehash, prefix) = ComputePboHashes(pboStream);

            var hash2 = ComputeCombinedHash(hash1, namehash, prefix);
            var hash3 = ComputeCombinedHash(filehash, namehash, prefix);

            var publicKey = signature.ToPublicKey();
            var keyLengthBytes = signature.KeyLength / 8;

            // Convert signatures from little-endian (file format) to big-endian (.NET format)
            var sig1Be = ToBigEndian(signature.Signature1, keyLengthBytes);
            var sig2Be = ToBigEndian(signature.Signature2, keyLengthBytes);
            var sig3Be = ToBigEndian(signature.Signature3, keyLengthBytes);

            var sig1Ok = RsaVerifyHash(publicKey.Rsa, hash1, sig1Be);
            var sig2Ok = RsaVerifyHash(publicKey.Rsa, hash2, sig2Be);
            var sig3Ok = RsaVerifyHash(publicKey.Rsa, hash3, sig3Be);

            return sig1Ok && sig2Ok && sig3Ok;
        }

        /// <summary>
        /// Computes the three PBO hashes needed for BISign.
        /// </summary>
        private static (byte[] hash1, byte[] namehash, byte[] filehash, string prefix) ComputePboHashes(Stream pboStream)
        {
            pboStream.Seek(0, SeekOrigin.Begin);
            using var ms = new MemoryStream();
            pboStream.CopyTo(ms);
            var pboData = ms.ToArray();
            var pboLen = pboData.Length;

            var offset = 0;

            // Parse prefix from header extensions
            string prefix = "";
            if (pboData[offset] != 0)
            {
                while (offset < pboLen)
                {
                    var entryEnd = Array.IndexOf<byte>(pboData, 0, offset);
                    if (entryEnd < 0 || entryEnd == offset)
                    {
                        offset = entryEnd + 1;
                        break;
                    }
                    var key = Encoding.ASCII.GetString(pboData, offset, entryEnd - offset);
                    offset = entryEnd + 1;

                    var valEnd = Array.IndexOf<byte>(pboData, 0, offset);
                    if (valEnd < 0)
                        break;
                    var value = Encoding.ASCII.GetString(pboData, offset, valEnd - offset);
                    offset = valEnd + 1;

                    if (string.Equals(key, "prefix", StringComparison.OrdinalIgnoreCase))
                        prefix = value;
                }
            }
            else
            {
                offset += 21; // skip 0x00 + 20-byte hash
            }

            if (!string.IsNullOrEmpty(prefix) && prefix[prefix.Length - 1] != '\\')
                prefix += '\\';

            // Parse file entries
            var fileNames = new List<string>();
            var fileOffsets = new List<(long offset, int size, string name)>();

            while (offset + 21 <= pboLen)
            {
                var nameEnd = Array.IndexOf<byte>(pboData, 0, offset);
                if (nameEnd < 0)
                    break;

                var fileName = Encoding.ASCII.GetString(pboData, offset, nameEnd - offset);
                offset = nameEnd + 1;

                if (string.IsNullOrEmpty(fileName))
                {
                    offset += 20;
                    break;
                }

                if (offset + 20 > pboLen) break;

                offset += 4; // packingMethod
                offset += 4; // originalSize
                offset += 4; // reserved
                offset += 4; // timestamp
                var dataSize = BitConverter.ToUInt32(pboData, offset);
                offset += 4;

                fileNames.Add(fileName);
                fileOffsets.Add((offset, (int)dataSize, fileName));
                offset += (int)dataSize;
            }

            // Hash1: last 20 bytes of the PBO (0x00 + 20-byte SHA1 footer).
            // If the PBO doesn't have a footer yet, compute hash1 from the entire body.
            byte[] hash1;
            if (pboLen >= 21 && pboData[pboLen - 21] == 0)
            {
                hash1 = new byte[20];
                Array.Copy(pboData, pboLen - 20, hash1, 0, 20);
            }
            else
            {
                using var sha1 = SHA1.Create();
                sha1.TransformFinalBlock(pboData, 0, pboLen);
                hash1 = sha1.Hash;
            }

            // Namehash: SHA1 of all lowercased sorted filenames
            var sortedNames = fileNames
                .Select(n => n.ToLowerInvariant())
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            byte[] namehash;
            using (var sha1 = SHA1.Create())
            {
                foreach (var name in sortedNames)
                {
                    var nameBytes = Encoding.ASCII.GetBytes(name);
                    sha1.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
                }
                sha1.TransformFinalBlock([], 0, 0);
                namehash = sha1.Hash;
            }

            // Filehash: SHA1 of file contents, skipping known binary types
            byte[] filehash;
            var hashedAnyFile = false;
            using (var sha1 = SHA1.Create())
            {
                foreach (var (foff, fsize, fname) in fileOffsets)
                {
                    var ext = Path.GetExtension(fname);
                    if (SkippedExtensions.Contains(ext))
                        continue;
                    if (fsize == 0)
                        continue;

                    hashedAnyFile = true;
                    var remaining = fsize;
                    var readOffset = foff;
                    const int bufferSize = 4096;

                    while (remaining > 0)
                    {
                        var chunkSize = Math.Min(bufferSize, remaining);
                        sha1.TransformBlock(pboData, (int)readOffset, chunkSize, null, 0);
                        readOffset += chunkSize;
                        remaining -= chunkSize;
                    }
                }

                if (!hashedAnyFile)
                {
                    var nothing = "nothing"u8.ToArray();
                    sha1.TransformBlock(nothing, 0, nothing.Length, null, 0);
                }

                sha1.TransformFinalBlock([], 0, 0);
                filehash = sha1.Hash;
            }

            return (hash1, namehash, filehash, prefix);
        }

        private static byte[] ComputeCombinedHash(byte[] hashA, byte[] hashB, string prefix)
        {
            using var sha1 = SHA1.Create();
            sha1.TransformBlock(hashA, 0, hashA.Length, null, 0);
            sha1.TransformBlock(hashB, 0, hashB.Length, null, 0);
            if (!string.IsNullOrEmpty(prefix))
            {
                var prefixBytes = Encoding.ASCII.GetBytes(prefix);
                sha1.TransformBlock(prefixBytes, 0, prefixBytes.Length, null, 0);
            }
            sha1.TransformFinalBlock([], 0, 0);
            return sha1.Hash;
        }

        private static byte[] RsaSignHash(RSA rsa, byte[] hash)
        {
            return rsa.SignHash(hash, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }

        private static bool RsaVerifyHash(RSA rsa, byte[] hash, byte[] signature)
        {
            return rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Converts a big-endian signature to fixed-size little-endian for .bisign format.
        /// </summary>
        private static byte[] ToLittleEndian(byte[] bigEndian, int expectedLength)
        {
            var result = new byte[expectedLength];
            if (bigEndian.Length <= expectedLength)
            {
                Array.Copy(bigEndian, 0, result, expectedLength - bigEndian.Length, bigEndian.Length);
            }
            else
            {
                Array.Copy(bigEndian, bigEndian.Length - expectedLength, result, 0, expectedLength);
            }
            Array.Reverse(result);
            return result;
        }

        /// <summary>
        /// Converts a little-endian signature to big-endian for .NET RSA verification.
        /// </summary>
        private static byte[] ToBigEndian(byte[] littleEndian, int expectedLength)
        {
            var result = new byte[littleEndian.Length];
            Array.Copy(littleEndian, result, littleEndian.Length);
            Array.Reverse(result);
            return result;
        }

        internal static void EmbedHashFooter(string pboPath)
        {
            var pboData = File.ReadAllBytes(pboPath);
            if (pboData.Length >= 21 && pboData[pboData.Length - 21] == 0)
                return;

            var hash = SHA1.HashData(pboData);
            using var fs = File.Open(pboPath, FileMode.Append);
            fs.WriteByte(0);
            fs.Write(hash);
        }
    }
}
