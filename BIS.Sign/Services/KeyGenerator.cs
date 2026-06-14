using System;
using System.IO;
using BIS.Sign.Models;

namespace BIS.Sign.Services
{
    /// <summary>
    /// Generates BI (Bohemia Interactive) RSA key pairs for PBO signing.
    /// Produces .biprivatekey and .bikey files.
    /// </summary>
    public class KeyGenerator
    {
        /// <summary>
        /// Generates a key pair and saves to the specified path.
        /// </summary>
        /// <param name="name">The author/signer name (e.g. "my_mod").</param>
        /// <param name="outputDir">Directory to write the key files to.</param>
        /// <param name="keyLength">Key length in bits (1024, 2048, or 4096).</param>
        /// <returns>The generated BiKey.</returns>
        public BiKey Generate(string name, string outputDir, int keyLength = BiKey.DefaultKeyLength)
        {
            var key = BiKey.Generate(name, keyLength);

            var privatePath = Path.Combine(outputDir, $"{name}.biprivatekey");
            var publicPath = Path.Combine(outputDir, $"{name}.bikey");

            key.SavePrivateKey(privatePath);
            key.SavePublicKey(publicPath);

            return key;
        }

        /// <summary>
        /// Generates a key pair and returns the key object without saving.
        /// </summary>
        public BiKey GenerateInMemory(string name, int keyLength = BiKey.DefaultKeyLength)
        {
            return BiKey.Generate(name, keyLength);
        }
    }
}
