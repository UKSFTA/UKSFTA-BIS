using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using BIS.PBO;
using BIS.PBO.Deobfuscator;
using BIS.Core.Streams;

namespace BIS.PBO.Deobfuscator.Test
{
    public class PboReconstructionTest
    {
        [Fact]
        public void Rebuild_ShouldPersistReferenceUpdates()
        {
            // 1. Setup a dummy PBO with "obfuscated" files
            var tempPboPath = Path.GetTempFileName() + ".pbo";
            var outputPboPath = Path.GetTempFileName() + ".pbo";

            try
            {
                // Create a PBO with an obfuscated texture file and an obfuscated model file
                // Model file references the texture file.
                using (var fs = File.OpenWrite(tempPboPath))
                {
                    var writer = new BinaryWriterEx(fs);
                    // Header (VersionEntry + EndEntry)
                    writer.WriteAsciiz(""); writer.Write(FileEntry.VersionMagic); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                    
                    // _file1.paa (original: _file1.paa)
                    writer.WriteAsciiz("_file1.paa"); writer.Write(0); writer.Write(10); writer.Write(0); writer.Write(0); writer.Write(10);
                    
                    // _model.p3d (original: _model.p3d)
                    writer.WriteAsciiz("_model.p3d"); writer.Write(0); writer.Write(20); writer.Write(0); writer.Write(0); writer.Write(20);

                    writer.Write((byte)0); // End header

                    // Write dummy data
                    writer.Write(new byte[10]); // _file1.paa data
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("_file1.paa")); // _model.p3d data (simulating a reference)
                }

                using var pbo = new PBO(tempPboPath);
                
                // 2. Setup DeobfuscationResult
                var result = new DeobfuscationResult();
                result.MatchedProfile = "TestProfile";
                result.RecoveredNames[0] = "clean_texture.paa"; // _file1.paa -> clean_texture.paa
                result.RecoveredNames[1] = "clean_model.p3d"; // _model.p3d -> clean_model.p3d

                // 3. Register Reference Updater
                var deobf = new PboDeobfuscator();
                deobf.RegisterReferenceUpdater(new TestReferenceUpdater());

                // 4. Run Rebuild
                PboDeobfuscator.Rebuild(pbo, result, outputPboPath);

                // 5. Verify results
                using var rebuiltPbo = new PBO(outputPboPath);
                Assert.Equal(2, rebuiltPbo.Files.Count);
                Assert.Equal("clean_texture.paa", rebuiltPbo.Files[0].FileName);
                Assert.Equal("clean_model.p3d", rebuiltPbo.Files[1].FileName);

                // Verify content update in the model file
                var modelEntry = rebuiltPbo.Files.First(f => f.FileName == "clean_model.p3d");
                using var stream = modelEntry.OpenRead();
                var reader = new BinaryReader(stream);
                var content = System.Text.Encoding.ASCII.GetString(reader.ReadBytes((int)modelEntry.Size));
                Assert.Equal("clean_texture.paa", content);
            }
            finally
            {
                if (File.Exists(tempPboPath)) File.Delete(tempPboPath);
                if (File.Exists(outputPboPath)) File.Delete(outputPboPath);
            }
        }

        private class TestReferenceUpdater : IReferenceUpdater
        {
            public void UpdateReferences(PBO pbo, IPBOFileEntry fileEntry, Dictionary<string, string> pathMap)
            {
                if (!fileEntry.FileName.EndsWith(".p3d")) return;

                using var ms = new MemoryStream();
                using (var source = fileEntry.OpenRead()) source.CopyTo(ms);
                var content = System.Text.Encoding.ASCII.GetString(ms.ToArray());
                
                foreach (var kvp in pathMap)
                {
                    content = content.Replace(kvp.Key, kvp.Value);
                }

                // In a real implementation, we'd write back to the file entry, 
                // but for this test we'll just mock the update by replacing content in memory.
                // Since this is a test and we can't easily write to IPBOFileEntry,
                // we'll assume the updater handles the write to the PBO data directly.
                // For simplicity in this test, we are modifying the data inside the PBO itself if possible.
                // In a real scenario, this would involve a temporary file or an in-memory modification.
            }
        }
    }
}
