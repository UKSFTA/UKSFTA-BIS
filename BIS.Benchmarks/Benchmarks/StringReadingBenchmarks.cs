using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BIS.Core.Streams;

namespace BIS.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn]
public class StringReadingBenchmarks
{
    private byte[] _testData = [];

    [GlobalSetup]
    public void Setup()
    {
        // Build a buffer of 1000 null-terminated strings (simulating PBO header / config data)
        using var ms = new MemoryStream();
        for (int i = 0; i < 1000; i++)
        {
            var chars = $"prefix_name_{i}_value_abcdefgh".ToCharArray();
            foreach (var c in chars) ms.WriteByte((byte)c);
            ms.WriteByte(0);
        }
        _testData = ms.ToArray();
    }

    [Benchmark]
    public int Read_1000_Asciiz_Bulk()
    {
        int total = 0;
        using var ms = new MemoryStream(_testData);
        var reader = new BinaryReaderEx(ms);
        for (int i = 0; i < 1000; i++)
        {
            var s = reader.ReadAsciiz();
            total += s.Length;
        }
        return total;
    }

    [Benchmark]
    public int Read_1000_FixedAscii_Bulk()
    {
        int total = 0;
        using var ms = new MemoryStream(_testData);
        var reader = new BinaryReaderEx(ms);
        for (int i = 0; i < 1000; i++)
        {
            var s = reader.ReadAscii(28); // all strings are same length
            total += s.Length;
        }
        return total;
    }
}
