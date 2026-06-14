using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BIS.Core.Config;
using BIS.Core.Streams;

namespace BIS.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn]
public class ConfigBenchmarks
{
    private byte[] _configBin;

    [GlobalSetup]
    public void Setup()
    {
        if (TestDataPath.Root == null) return;
        var cfgDir = Path.Combine(TestDataPath.Root, "config");
        if (!Directory.Exists(cfgDir)) return;
        var files = Directory.GetFiles(cfgDir, "config.bin");
        if (files.Length == 0) return;
        _configBin = File.ReadAllBytes(files[0]);
    }

    [Params(100, 1000)]
    public int Iterations { get; set; }

    [Benchmark(Description = "Config serialize to text")]
    [BenchmarkCategory("Config")]
    public string SerializeToText()
    {
        if (_configBin == null) return null;
        string result = null;
        for (int i = 0; i < Iterations; i++)
        {
            using var input = new MemoryStream(_configBin);
            using var output = new MemoryStream();
            ConfigSerializer.Serialize(input, output);
            output.Position = 0;
            result = new StreamReader(output).ReadToEnd();
        }
        return result;
    }

    [Benchmark(Description = "Config binary roundtrip")]
    [BenchmarkCategory("Config")]
    public ParamFile BinaryRoundtrip()
    {
        if (_configBin == null) return null;
        ParamFile result = null;
        for (int i = 0; i < Iterations; i++)
        {
            using var input = new MemoryStream(_configBin);
            var parsed = new ParamFile(input);
            using var output = new MemoryStream();
            var writer = new BinaryWriterEx(output, true);
            parsed.Write(writer);
            writer.Flush();
            output.Position = 0;
            result = new ParamFile(output);
        }
        return result;
    }
}
