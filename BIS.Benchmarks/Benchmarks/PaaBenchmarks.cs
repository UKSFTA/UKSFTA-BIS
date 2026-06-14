using System;
using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace BIS.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn]
public class PaaBenchmarks
{
    private List<byte[]> _paaFiles;

    [GlobalSetup]
    public void Setup()
    {
        if (TestDataPath.Root == null) return;
        var files = Directory.GetFiles(Path.Combine(TestDataPath.Root, "paa"), "*.paa");
        _paaFiles = new List<byte[]>(files.Length);
        foreach (var f in files)
            _paaFiles.Add(File.ReadAllBytes(f));
    }

    [Params(10, 100)]
    public int Iterations { get; set; }

    [Benchmark(Description = "PAA open")]
    [BenchmarkCategory("PAA")]
    public global::BIS.PAA.PAA OpenPaa()
    {
        if (_paaFiles == null) return null;
        var best = default(global::BIS.PAA.PAA);
        for (int iter = 0; iter < Iterations; iter++)
        {
            foreach (var data in _paaFiles)
                best = new global::BIS.PAA.PAA(new MemoryStream(data));
        }
        return best;
    }

    [Benchmark(Description = "PAA analyze")]
    [BenchmarkCategory("PAA")]
    public object AnalyzePaa()
    {
        if (_paaFiles == null) return null;
        object last = null;
        for (int iter = 0; iter < Iterations; iter++)
        {
            foreach (var data in _paaFiles)
            {
                var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(new MemoryStream(data));
                last = analysis;
            }
        }
        return last;
    }

    [Benchmark(Description = "PAA analyze + suggest")]
    [BenchmarkCategory("PAA")]
    public object AnalyzeAndSuggestPaa()
    {
        if (_paaFiles == null) return null;
        object last = null;
        for (int iter = 0; iter < Iterations; iter++)
        {
            foreach (var data in _paaFiles)
            {
                using var ms = new MemoryStream(data);
                var analysis = global::BIS.PAA.PaaAnalyzer.Analyze(ms);
                var suggestion = global::BIS.PAA.PaaAnalyzer.SuggestOptimalFormat(analysis);
                last = suggestion;
            }
        }
        return last;
    }
}
