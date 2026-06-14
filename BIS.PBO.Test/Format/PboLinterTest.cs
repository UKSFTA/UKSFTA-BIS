using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using BIS.Core.Streams;

namespace BIS.PBO.Test.Format
{
    public class PboLinterTest
    {
        // ─── Helpers ───

        private static PBO CreatePboFromBytes(byte[] pboBytes)
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFile, pboBytes);
                return new PBO(tempFile);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        private static PBO CreateMinimalPbo(string prefix, params (string name, int size)[] entries)
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                using (var fs = File.OpenWrite(tempFile))
                {
                    var writer = new BinaryWriterEx(fs);

                    // Version entry (header start marker)
                    writer.WriteAsciiz("");
                    writer.Write(FileEntry.VersionMagic);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    // Properties
                    writer.WriteAsciiz("prefix");
                    writer.WriteAsciiz(prefix);
                    writer.Write((byte)0); // end of properties

                    // File entries
                    foreach (var (name, size) in entries)
                    {
                        writer.WriteAsciiz(name);
                        writer.Write(0);          // compressed magic
                        writer.Write(0);          // uncompressed size
                        writer.Write(0);          // start offset (all 0 since we write no data)
                        writer.Write(0);          // timestamp
                        writer.Write(size);       // data size
                    }

                    // End marker
                    writer.WriteAsciiz("");
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                }

                return new PBO(tempFile);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        // ─── Tests ───

        [Fact]
        public void CleanPbo_NoDiagnostics()
        {
            var pbo = new PBO();
            pbo.AddFile("config.bin", [0x00, 0x01]);
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "my_mod"));

            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            Assert.Empty(diags);
        }

        [Fact]
        public void Lint_P01_DuplicateEntry_EmitsError()
        {
            var pbo = new PBO();
            pbo.AddFile("dup.txt", [0]);
            pbo.AddFile("dup.txt", [1]);

            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            Assert.Contains(diags, d => d.Code == "L-P01" && d.EntryName == "dup.txt");
        }

        [Fact]
        public void Lint_P01_DuplicateEntry_CaseInsensitive_EmitsError()
        {
            var pbo = new PBO();
            pbo.AddFile("Data.Bin", [0]);
            pbo.AddFile("data.bin", [1]);

            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            // Only the second entry is flagged as duplicate
            Assert.Equal(1, diags.Count(d => d.Code == "L-P01"));
        }

        [Fact]
        public void Lint_P02_ObfuscatedName_RawDiffersFromSanitized_EmitsWarning()
        {
            // Create a real PBO with an obfuscated entry name (contains wildcard chars)
            // The sanitizer in PBO.ReadHeader will clean it, RawFileName preserves original
            var tempFile = Path.GetTempFileName();
            try
            {
                using (var fs = File.OpenWrite(tempFile))
                {
                    var writer = new BinaryWriterEx(fs);

                    // Version
                    writer.WriteAsciiz("");
                    writer.Write(FileEntry.VersionMagic);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    // Properties with prefix
                    writer.WriteAsciiz("prefix");
                    writer.WriteAsciiz("test_mod");
                    writer.Write((byte)0);

                    // Entry with wildcard chars that will be sanitized
                    writer.WriteAsciiz("bad*file.txt");
                    writer.Write(0);
                    writer.Write(10);
                    writer.Write(0);
                    writer.Write(12345);
                    writer.Write(10);

                    // Entry with path traversal
                    writer.WriteAsciiz("..\\secret.txt");
                    writer.Write(0);
                    writer.Write(20);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(20);

                    // End marker
                    writer.WriteAsciiz("");
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    // Dummy data
                    var dummy = new byte[30];
                    writer.Write(dummy);
                }

                var pbo = new PBO(tempFile);
                var linter = new PboLinter();
                var diags = linter.Lint(pbo);

                // bad*file.txt got sanitized to badfile.txt -> raw differs
                Assert.Contains(diags, d =>
                    d.Code == "L-P02" &&
                    d.Message.Contains("obfuscated", StringComparison.OrdinalIgnoreCase));

                // ..\secret.txt got sanitized to secret.txt -> raw differs
                Assert.Contains(diags, d =>
                    d.Code == "L-P02" &&
                    d.Message.Contains("obfuscated", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public void Lint_P03_MissingPrefix_EmitsWarning()
        {
            var pbo = new PBO();
            pbo.AddFile("file.bin", [0]);

            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            Assert.Contains(diags, d => d.Code == "L-P03");
        }

        [Fact]
        public void Lint_P03_EmptyPrefix_EmitsWarning()
        {
            var pbo = new PBO();
            pbo.AddFile("file.bin", [0]);
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", ""));

            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            Assert.Contains(diags, d => d.Code == "L-P03");
        }

        [Fact]
        public void Lint_P04_EmptyPbo_EmitsWarning()
        {
            var pbo = new PBO();
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "my_mod"));

            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            Assert.Contains(diags, d => d.Code == "L-P04");
        }

        [Fact]
        public void Lint_P05_ZeroTimestamp_EmitsWarning()
        {
            var pbo = new PBO();
            // PBOFileInMemory always uses current time, so we need a custom entry
            var entries = pbo.Files;
            entries.Clear();

            // Add a file entry with zero timestamp via the internal FileEntry
            // We can use the PBO constructor from a binary file to get FileEntry objects
            var tempFile = Path.GetTempFileName();
            try
            {
                using (var fs = File.OpenWrite(tempFile))
                {
                    var writer = new BinaryWriterEx(fs);

                    writer.WriteAsciiz("");
                    writer.Write(FileEntry.VersionMagic);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    writer.WriteAsciiz("prefix");
                    writer.WriteAsciiz("test_mod");
                    writer.Write((byte)0);

                    // Entry with zero timestamp
                    writer.WriteAsciiz("config.bin");
                    writer.Write(0);
                    writer.Write(100);
                    writer.Write(0);
                    writer.Write(0);  // timestamp = 0
                    writer.Write(100);

                    writer.WriteAsciiz("");
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    writer.Write(new byte[100]);
                }

                var pbo2 = new PBO(tempFile);
                var linter = new PboLinter();
                var diags = linter.Lint(pbo2);

                Assert.Contains(diags, d => d.Code == "L-P05" && d.EntryName == "config.bin");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public void Lint_P05_ZeroTimestampOnEmptyEntry_NoDiagnostic()
        {
            // Zero-length entries with timestamp 0 are normal (end markers)
            var pbo = new PBO();
            pbo.AddFile("empty.txt", []);
            pbo.PropertiesPairs.Add(new KeyValuePair<string, string>("prefix", "mod"));

            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            Assert.DoesNotContain(diags, d => d.Code == "L-P05");
        }

        [Fact]
        public void Lint_MultipleRules_FindsAllIssues()
        {
            // PBO with: duplicate entries, no prefix
            var pbo = new PBO();
            pbo.AddFile("dup.txt", [0]);
            pbo.AddFile("dup.txt", [1]);

            var linter = new PboLinter();
            var diags = linter.Lint(pbo);

            Assert.Contains(diags, d => d.Code == "L-P01");
            Assert.Contains(diags, d => d.Code == "L-P03");
            Assert.DoesNotContain(diags, d => d.Code == "L-P04"); // has files
        }
    }
}
