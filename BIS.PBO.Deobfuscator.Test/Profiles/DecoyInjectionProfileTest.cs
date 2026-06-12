using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using BIS.PBO;
using BIS.PBO.Deobfuscator.Profiles;

namespace BIS.PBO.Deobfuscator.Test.Profiles
{
    public class DecoyInjectionProfileTest
    {
        private static readonly byte[] PaaHeader = { 0x00, 0x72, 0x61, 0x53 };
        private static readonly byte[] RVMatContent = Encoding.ASCII.GetBytes("class Stage0 { texture=\"data/tex/old_co.paa\"; };");
        private static readonly byte[] P3DContent = new byte[] { 0x4F, 0x44, 0x4F, 0x4C, 0x00 };

        [Fact]
        public void Deobfuscate_ZeroByteFile_ClassifiesAsDecoy()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("xsOpHgLJR.rsa", new byte[0]));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Single(result.FilteredOut);
            Assert.Contains(0, result.FilteredOut);
            Assert.Equal(1, result.Stats["Decoys"]);
        }

        [Fact]
        public void Deobfuscate_SmallRandomNameFile_ClassifiesAsStub()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("aB3xY9.bin", new byte[10]));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Contains(0, result.FilteredOut);
            Assert.Equal(1, result.Stats["Stubs"]);
        }

        [Fact]
        public void Deobfuscate_RootLevelPaaSmall_ClassifiesAsStub()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("random.paa", new byte[100]));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Contains(0, result.FilteredOut);
            Assert.Equal(1, result.Stats["Stubs"]);
        }

        [Fact]
        public void Deobfuscate_RootLevelP3DLarge_NotClassifiedAsStub()
        {
            var pbo = MakePbo();
            // P3D in root, size > 100000 -> exception, NOT a stub
            pbo.Files.Add(new DummyFileEntry("vehicle.p3d", new byte[100001]));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Empty(result.FilteredOut);
        }

        [Fact]
        public void Deobfuscate_UnknownDirSmallFile_ClassifiesAsStub()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("_unknown\\small.dat", new byte[50]));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Contains(0, result.FilteredOut);
            Assert.Equal(1, result.Stats["Stubs"]);
        }

        [Fact]
        public void Deobfuscate_IncludeWithControlChars_ClassifiesAsStub()
        {
            var pbo = MakePbo();
            // #include with control characters in the path
            var content = new byte[] { 0x23, 0x69, 0x6E, 0x63, 0x6C, 0x75, 0x64, 0x65, 0x20, 0x22, 0x00, 0x01, 0x02, 0x22 };
            pbo.Files.Add(new DummyFileEntry("script.hpp", content));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Contains(0, result.FilteredOut);
            Assert.Equal(1, result.Stats["Stubs"]);
        }

        [Fact]
        public void Deobfuscate_SqfWithExecVM_ClassifiesAsEntryPoint()
        {
            var pbo = MakePbo();
            var sqfContent = Encoding.ASCII.GetBytes("execVM \"init.sqf\";");
            pbo.Files.Add(new DummyFileEntry("start.sqf", sqfContent));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.DoesNotContain(0, result.FilteredOut);
            Assert.Equal(1, result.Stats["EntryPoints"]);
        }

        [Fact]
        public void Deobfuscate_SqfWithCallCompile_ClassifiesAsEntryPoint()
        {
            var pbo = MakePbo();
            var sqfContent = Encoding.ASCII.GetBytes("call compile preprocessFile \"init.sqf\";");
            pbo.Files.Add(new DummyFileEntry("init.sqf", sqfContent));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Equal(1, result.Stats["EntryPoints"]);
        }

        [Fact]
        public void Deobfuscate_NormalAssetFile_NotFiltered()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("data\\model.rvmat", RVMatContent));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Empty(result.FilteredOut);
        }

        [Fact]
        public void Deobfuscate_MultipleDecoysAndStubs_CountsCorrectly()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("decoy1.bin", new byte[0]));     // decoy
            pbo.Files.Add(new DummyFileEntry("stub1.bin", new byte[10]));     // stub (random name)
            pbo.Files.Add(new DummyFileEntry("real.rvmat", RVMatContent));   // normal
            pbo.Files.Add(new DummyFileEntry("decoy2.bin", new byte[0]));     // decoy
            pbo.Files.Add(new DummyFileEntry("stub2.bin", new byte[5]));      // stub
            pbo.Files.Add(new DummyFileEntry("start.sqf", Encoding.ASCII.GetBytes("execVM \"x.sqf\";"))); // entry

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Equal(2, result.Stats["Decoys"]);
            Assert.Equal(2, result.Stats["Stubs"]);
            Assert.Equal(1, result.Stats["EntryPoints"]);
            Assert.Equal(1, result.Stats["Genuine"]);
            Assert.Equal(6, result.Stats["Total"]);
            Assert.Equal(4, result.FilteredOut.Count);
        }

        [Fact]
        public void Deobfuscate_SmallRootLevelFileWithKnownName_NotFiltered()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("mission.sqm", Encoding.ASCII.GetBytes("version=1;")));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Empty(result.FilteredOut);
        }

        [Fact]
        public void Deobfuscate_SmallRootLevelP3dUnder100k_StubIfRandomName()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("xYz.p3d", new byte[100]));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Contains(0, result.FilteredOut);
            Assert.Equal(1, result.Stats["Stubs"]);
        }

        [Fact]
        public void Deobfuscate_DefaultStats_ReturnsEmptyForEmptyPBO()
        {
            var pbo = MakePbo();
            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Equal(0, result.Stats["Decoys"]);
            Assert.Equal(0, result.Stats["Stubs"]);
            Assert.Equal(0, result.Stats["EntryPoints"]);
            Assert.Equal(0, result.Stats["Genuine"]);
            Assert.Equal(0, result.Stats["Total"]);
        }

        [Fact]
        public void Deobfuscate_RecoversDecoyWithDecoyPrefix()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("foo.bar", new byte[0]));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Equal("_decoy/foo.bar", result.RecoveredNames[0]);
        }

        [Fact]
        public void Deobfuscate_RecoversStubWithStubPrefix()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("abc123.tmp", new byte[10]));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Equal("_stub/abc123.tmp", result.RecoveredNames[0]);
        }

        [Fact]
        public void Deobfuscate_RecoversEntryWithEntryPrefix()
        {
            var pbo = MakePbo();
            pbo.Files.Add(new DummyFileEntry("exec.sqf", Encoding.ASCII.GetBytes("execVM \"script.sqf\";")));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.StartsWith("_entry/", result.RecoveredNames[0]);
        }

        [Fact]
        public void Deobfuscate_ContentScan_SmallFileWithNoPattern_NotClassified()
        {
            var pbo = MakePbo();
            // .sqf under 65536 bytes but content has no execVM/call/preprocessFile -> no entry point
            pbo.Files.Add(new DummyFileEntry("util.sqf", Encoding.ASCII.GetBytes("private _x = 1;")));

            var result = new DecoyInjectionProfile().Deobfuscate(pbo);

            Assert.Equal(0, result.Stats["EntryPoints"]);
            Assert.Empty(result.FilteredOut);
        }

        private static PBO MakePbo()
        {
            return new PBO();
        }

        private class DummyFileEntry : IPBOFileEntry
        {
            public string FileName { get; }
            public string RawFileName => FileName;
            public int Size => _data.Length;
            public int TimeStamp => 0;
            public bool IsCompressed => false;
            public int DiskSize => _data.Length;
            private readonly byte[] _data;

            public DummyFileEntry(string fileName, byte[] data)
            {
                FileName = fileName;
                _data = data;
            }

            public Stream OpenRead() => new MemoryStream(_data, false);
        }
    }
}
