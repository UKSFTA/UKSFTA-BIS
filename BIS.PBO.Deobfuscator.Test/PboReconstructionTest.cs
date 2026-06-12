using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using BIS.PBO;
using BIS.PBO.Deobfuscator;

namespace BIS.PBO.Deobfuscator.Test
{
    public class PboReconstructionTest
    {
        [Fact]
        public void P3DUpdater_ReturnsNullForNonP3D()
        {
            var pathMap = new Dictionary<string, string>();
            var entry = new DummyFileEntry("file.txt", new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F });
            var updater = new P3DTextureReferenceUpdater();
            Assert.Null(updater.UpdateReferences(entry, pathMap));
        }

        [Fact]
        public void P3DUpdater_ReturnsNullForInvalidP3D()
        {
            var pathMap = new Dictionary<string, string>();
            var entry = new DummyFileEntry("model.p3d", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
            var updater = new P3DTextureReferenceUpdater();
            Assert.Null(updater.UpdateReferences(entry, pathMap));
        }

        [Fact]
        public void RVMATUpdater_ReturnsNullForNonRVMAT()
        {
            var pathMap = new Dictionary<string, string>();
            var entry = new DummyFileEntry("file.txt", new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F });
            var updater = new RVMATReferenceUpdater();
            Assert.Null(updater.UpdateReferences(entry, pathMap));
        }

        [Fact]
        public void RVMATUpdater_ReturnsNullForInvalidRVMAT()
        {
            var pathMap = new Dictionary<string, string>();
            var entry = new DummyFileEntry("material.rvmat", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
            var updater = new RVMATReferenceUpdater();
            Assert.Null(updater.UpdateReferences(entry, pathMap));
        }

        [Fact]
        public void ReferenceUpdaters_AreRegisteredByDefault()
        {
            var deobf = new PboDeobfuscator();
        }

        private class DummyFileEntry : IPBOFileEntry
        {
            public string FileName { get; }
            public int Size { get; }
            public int TimeStamp => 0;
            public bool IsCompressed => false;
            public int DiskSize => Size;
            private readonly byte[] _data;

            public DummyFileEntry(string fileName, byte[] data)
            {
                FileName = fileName;
                _data = data;
                Size = data.Length;
            }

            public Stream OpenRead() => new MemoryStream(_data, false);
        }
    }
}
