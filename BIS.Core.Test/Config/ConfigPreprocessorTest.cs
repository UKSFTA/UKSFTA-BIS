using System;
using System.IO;
using BIS.Core.Config;
using Xunit;

namespace BIS.Core.Test.Config
{
    public class ConfigPreprocessorTest : IDisposable
    {
        private readonly string _testFile;

        public ConfigPreprocessorTest()
        {
            _testFile = Path.GetTempFileName();
        }

        private ConfigPreprocessor CreatePreprocessor(string content)
        {
            File.WriteAllText(_testFile, content);
            var resolver = new DefaultIncludeResolver(new[] { Path.GetTempPath() });
            return new ConfigPreprocessor(resolver);
        }

        [Fact]
        public void Preprocess_BasicDefine_ExpandsSuccessfully()
        {
            File.WriteAllText(_testFile, "#define FOO 123\nFOO");
            var preproc = new ConfigPreprocessor(new DefaultIncludeResolver(new[] { Path.GetTempPath() }));
            var tokens = preproc.Preprocess(_testFile);
            Assert.Empty(preproc.Diagnostics);
            Assert.Contains(tokens, t => t.Value == "123");
        }

        [Fact]
        public void Define_SameValue_NoWarning()
        {
            var preproc = CreatePreprocessor("#define FOO 123\n#define FOO 123");
            preproc.Preprocess(_testFile);
            Assert.Empty(preproc.Diagnostics);
        }

        [Fact]
        public void Define_DifferentValue_EmitsPW1()
        {
            var preproc = CreatePreprocessor("#define FOO 123\n#define FOO 456");
            preproc.Preprocess(_testFile);
            var diag = Assert.Single(preproc.Diagnostics);
            Assert.Equal("PW1", diag.Code);
        }

        [Fact]
        public void Define_EmptyToValue_EmitsPW1()
        {
            var preproc = CreatePreprocessor("#define FOO\n#define FOO 123");
            preproc.Preprocess(_testFile);
            var diag = Assert.Single(preproc.Diagnostics);
            Assert.Equal("PW1", diag.Code);
        }

        [Fact]
        public void Define_InitialBuiltinRedefine_EmitsPW1()
        {
            var defines = new Dictionary<string, string?> { ["FOO"] = "original" };
            File.WriteAllText(_testFile, "#define FOO overwritten");
            var resolver = new DefaultIncludeResolver(new[] { Path.GetTempPath() });
            var preproc = new ConfigPreprocessor(resolver, defines);
            preproc.Preprocess(_testFile);
            var diag = Assert.Single(preproc.Diagnostics);
            Assert.Equal("PW1", diag.Code);
        }

        [Fact]
        public void Undef_ExistingMacro_NoWarning()
        {
            var preproc = CreatePreprocessor("#define FOO 123\n#undef FOO");
            preproc.Preprocess(_testFile);
            Assert.Empty(preproc.Diagnostics);
        }

        [Fact]
        public void Undef_NotDefined_EmitsPW5()
        {
            var preproc = CreatePreprocessor("#undef FOO");
            preproc.Preprocess(_testFile);
            var diag = Assert.Single(preproc.Diagnostics);
            Assert.Equal("PW5", diag.Code);
        }

        [Fact]
        public void Undef_AlreadyUndefined_EmitsPW5()
        {
            var preproc = CreatePreprocessor("#define FOO 123\n#undef FOO\n#undef FOO");
            preproc.Preprocess(_testFile);
            var diag = Assert.Single(preproc.Diagnostics);
            Assert.Equal("PW5", diag.Code);
        }

        public void Dispose()
        {
            if (File.Exists(_testFile)) File.Delete(_testFile);
        }
    }
}
