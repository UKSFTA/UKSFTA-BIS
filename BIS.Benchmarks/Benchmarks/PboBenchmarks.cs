using System.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace BIS.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn]
public class PboBenchmarks
{
    private string[] _tempFiles = [];

    [GlobalSetup]
    public void Setup()
    {
        var srcFiles = TestDataPath.GetFiles("pbo", "*.pbo").Take(10).ToArray();
        _tempFiles = new string[srcFiles.Length];
        for (int i = 0; i < srcFiles.Length; i++)
        {
            _tempFiles[i] = Path.GetTempFileName() + ".pbo";
            File.Copy(srcFiles[i], _tempFiles[i], true);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Params(1, 10)]
    public int Iterations { get; set; }

    [Benchmark]
    public int Open_PboFiles()
    {
        int count = 0;
        for (int iter = 0; iter < Iterations; iter++)
        {
            foreach (var file in _tempFiles)
            {
                var pbo = new BIS.PBO.PBO(file);
                count += pbo.Files.Count;
            }
        }
        return count;
    }

    [Benchmark]
    public long ExtractFirstFile_PboFiles()
    {
        long total = 0;
        for (int iter = 0; iter < Iterations; iter++)
        {
            foreach (var file in _tempFiles)
            {
                var pbo = new BIS.PBO.PBO(file);
                foreach (var entry in pbo.Files)
                {
                    if (entry.Size > 0)
                    {
                        using var stream = entry.OpenRead();
                        var buf = new byte[entry.Size];
                        total += stream.Read(buf);
                        break;
                    }
                }
            }
        }
        return total;
    }
}
