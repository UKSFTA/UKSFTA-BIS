using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BIS.Core.Streams;

namespace BIS.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn]
public class P3dLoadingBenchmarks
{
    // Pre-loaded ODOL model data: each entry is (fileBytes, filePath)
    private (byte[] data, string path)[] _odolModels = [];
    private byte[] _mlodData = [];
    private string _mlodPath = "";

    [GlobalSetup]
    public void Setup()
    {
        var odolFiles = TestDataPath.GetFiles("p3d", "*.p3d")
            .Where(f => !f.EndsWith("qm.p3d"))
            .Take(10)
            .ToArray();
        _odolModels = odolFiles
            .Select(f => (File.ReadAllBytes(f), f))
            .ToArray();

        var mlodFile = TestDataPath.GetFiles("p3d", "qm.p3d").FirstOrDefault();
        if (mlodFile != null)
        {
            _mlodData = File.ReadAllBytes(mlodFile);
            _mlodPath = mlodFile;
        }
    }

    [Params(10, 100)]
    public int Iterations { get; set; }

    [Benchmark]
    public int Open_Odol_FromMemory()
    {
        if (_odolModels.Length == 0) return 0;
        int count = 0;
        for (int iter = 0; iter < Iterations; iter++)
        {
            foreach (var (data, _) in _odolModels)
            {
                using var ms = new MemoryStream(data);
                var p3d = new BIS.P3D.P3D(ms);
                count += p3d.LODs.Count();
            }
        }
        return count;
    }

    [Benchmark]
    public int OpenAndValidate_Odol_FromMemory()
    {
        if (_odolModels.Length == 0) return 0;
        int count = 0;
        for (int iter = 0; iter < Iterations; iter++)
        {
            foreach (var (data, path) in _odolModels)
            {
                using var ms = new MemoryStream(data);
                var result = BIS.P3D.P3DValidator.Analyse(ms, path);
                count += result.LodCount;
            }
        }
        return count;
    }

    [Benchmark]
    public int OpenAndConvert_Odol_FromMemory()
    {
        if (_odolModels.Length == 0) return 0;
        int count = 0;
        for (int iter = 0; iter < Iterations; iter++)
        {
            foreach (var (data, _) in _odolModels)
            {
                using var ms = new MemoryStream(data);
                var p3d = new BIS.P3D.P3D(ms);
                if (p3d.ODOL != null)
                {
                    var mlod = BIS.P3D.Conversion.ODOL2MLOD.Convert(p3d.ODOL);
                    count += mlod.Lods.Length;
                }
            }
        }
        return count;
    }

    [Benchmark]
    public int Open_Mlod_FromMemory()
    {
        if (_mlodData.Length == 0) return 0;
        using var ms = new MemoryStream(_mlodData);
        var p3d = new BIS.P3D.P3D(ms);
        return p3d.LODs.Count();
    }
}
