using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BIS.Core.Compression;
using BIS.Core.Streams;

namespace BIS.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn]
public class LzssDecompressionBenchmarks
{
    private byte[] _compressedWithCsum = [];
    private byte[] _inputData = [];
    private byte[] _optimalCompressed = [];

    [Params(1000, 100_000, 1_000_000)]
    public int DataSize;

    [GlobalSetup]
    public void Setup()
    {
        var raw = TestDataPath.ReadAllBytes("p3d", "*.p3d", DataSize);
        _inputData = raw.Length < DataSize ? raw : raw[..DataSize];

        using var ms = new MemoryStream();
        using var writer = new BinaryWriterEx(ms, true);
        writer.WriteLZSS(_inputData, false);
        _compressedWithCsum = ms.ToArray();

        _optimalCompressed = LzssOptimalEncoder.Compress(_inputData);
    }

    [Benchmark(Baseline = true)]
    public byte[] Decompress_Greedy()
    {
        using var ms = new MemoryStream(_compressedWithCsum);
        var reader = new BinaryReaderEx(ms);
        return reader.ReadLZSS((uint)_inputData.Length, false);
    }

    [Benchmark]
    public byte[] Decompress_Optimal()
    {
        int csum = 0;
        foreach (var b in _inputData) csum += b;
        using var ms = new MemoryStream(_optimalCompressed.Length + 4);
        ms.Write(_optimalCompressed);
        ms.Write(BitConverter.GetBytes(csum));
        ms.Position = 0;
        LZSS.ReadLZSS(ms, out byte[] result, (uint)_inputData.Length, false);
        return result;
    }
}

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn]
public class LzssEncoderComparisonBenchmarks
{
    private byte[] _p3dData50k = [];
    private byte[] _p3dData500k = [];

    [GlobalSetup]
    public void Setup()
    {
        var raw = TestDataPath.ReadAllBytes("p3d", "*.p3d", 500_000);
        _p3dData50k = raw.Length < 50_000 ? raw : raw[..50_000];
        _p3dData500k = raw.Length < 500_000 ? raw : raw[..500_000];
    }

    [Benchmark]
    [BenchmarkCategory("50KB")]
    public int Encode_Greedy_50KB()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriterEx(ms, true);
        writer.WriteLZSS(_p3dData50k, false);
        return (int)ms.Length;
    }

    [Benchmark]
    [BenchmarkCategory("50KB")]
    public int Encode_Optimal_50KB()
    {
        var result = LzssOptimalEncoder.Compress(_p3dData50k);
        return result.Length;
    }

    [Benchmark]
    [BenchmarkCategory("500KB")]
    public int Encode_Greedy_500KB()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriterEx(ms, true);
        writer.WriteLZSS(_p3dData500k, false);
        return (int)ms.Length;
    }

    [Benchmark]
    [BenchmarkCategory("500KB")]
    public int Encode_Optimal_500KB()
    {
        var result = LzssOptimalEncoder.Compress(_p3dData500k);
        return result.Length;
    }
}
