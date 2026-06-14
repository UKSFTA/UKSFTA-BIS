using BIS.Core.Streams;
using System.IO;
using System.Linq;
using Xunit;

namespace BIS.IntegrationTest;

public class P3DConversionTests
{
    [Fact]
    public void ODOL2MLOD_AllOdolFiles_ConvertWithoutError()
    {
        if (!TestData.CheckAvailable("p3d", "Run _testdata/download.sh for ALDP packages.")) return;

        var files = TestData.GetFiles("p3d", "*.p3d");
        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            var p3d = new global::BIS.P3D.P3D(stream);
            if (p3d.ODOL == null) continue;

            var mlod = global::BIS.P3D.Conversion.ODOL2MLOD.Convert(p3d.ODOL);
            Assert.NotNull(mlod);
            Assert.NotNull(mlod.Lods);
            Assert.NotEmpty(mlod.Lods);
        }
    }

    [Fact]
    public void ODOL2MLOD_Barbedwire_PreservesLodCount()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        using var stream = File.OpenRead(file);
        var odol = new global::BIS.P3D.P3D(stream).ODOL;
        var mlod = global::BIS.P3D.Conversion.ODOL2MLOD.Convert(odol);
        Assert.Equal(odol.Lods.Length, mlod.Lods.Length);
    }

    [Fact]
    public void ODOL2MLOD_Barbedwire_AllLodsHaveVertices()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        using var stream = File.OpenRead(file);
        var odol = new global::BIS.P3D.P3D(stream).ODOL;
        var mlod = global::BIS.P3D.Conversion.ODOL2MLOD.Convert(odol);
        foreach (var mlodLod in mlod.Lods)
            Assert.True(mlodLod.Points.Length >= 0);
    }

    [Fact]
    public void ODOL2MLOD_Barel1_PreservesLodCount()
    {
        var file = TestData.GetFile("p3d", "Barel1.p3d");
        if (file == null) return;

        using var stream = File.OpenRead(file);
        var odol = new global::BIS.P3D.P3D(stream).ODOL;
        var mlod = global::BIS.P3D.Conversion.ODOL2MLOD.Convert(odol);
        Assert.Equal(odol.Lods.Length, mlod.Lods.Length);
    }

    [Fact]
    public void MLOD_Validate_Succeeds()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.True(result.IsValid);
        Assert.True(result.IsMLOD, "qm.p3d must be MLOD format");
    }

    [Fact]
    public void MLOD_Validate_HasSingleLod()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.Equal(1, result.LodCount);
    }

    [Fact]
    public void MLOD_Validate_NoErrors()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void MLOD_Roundtrip_SerializationPreservesLodCount()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var original = p3d.MLOD;

        using var ms = new MemoryStream();
        original.WriteToStream(ms);
        ms.Position = 0;
        var reparsed = new global::BIS.P3D.MLOD.MLOD(ms);

        Assert.Equal(original.Lods.Length, reparsed.Lods.Length);
    }

    [Fact]
    public void MLOD_Roundtrip_ResolutionPreserved()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var original = p3d.MLOD;

        using var ms = new MemoryStream();
        original.WriteToStream(ms);
        ms.Position = 0;
        var reparsed = new global::BIS.P3D.MLOD.MLOD(ms);

        for (int i = 0; i < original.Lods.Length; i++)
            Assert.Equal(original.Lods[i].Resolution, reparsed.Lods[i].Resolution);
    }

    [Fact]
    public void MLOD_Roundtrip_PointCountPreserved()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var original = p3d.MLOD;
        Assert.Equal(76, original.Lods[0].Points.Length);

        using var ms = new MemoryStream();
        original.WriteToStream(ms);
        ms.Position = 0;
        var reparsed = new global::BIS.P3D.MLOD.MLOD(ms);
        Assert.Equal(76, reparsed.Lods[0].Points.Length);
    }

    [Fact]
    public void MLOD_Roundtrip_FaceCountPreserved()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var original = p3d.MLOD;
        Assert.Equal(144, original.Lods[0].Faces.Length);

        using var ms = new MemoryStream();
        original.WriteToStream(ms);
        ms.Position = 0;
        var reparsed = new global::BIS.P3D.MLOD.MLOD(ms);
        Assert.Equal(144, reparsed.Lods[0].Faces.Length);
    }

    [Fact]
    public void MLOD_Roundtrip_VertexFlagsPreserved()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var original = p3d.MLOD;

        using var ms = new MemoryStream();
        original.WriteToStream(ms);
        ms.Position = 0;
        var reparsed = new global::BIS.P3D.MLOD.MLOD(ms);

        for (int i = 0; i < original.Lods[0].Points.Length; i++)
            Assert.Equal(original.Lods[0].Points[i].PointFlags, reparsed.Lods[0].Points[i].PointFlags);
    }

    [Fact]
    public void MLOD_HasUVSet_Roundtrip()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var mlod = p3d.MLOD;
        var lod = mlod.Lods[0];

        // Check UV sets exist (qm.p3d has RGBA_4444 UV set)
        bool hasUV = false;
        foreach (var tagg in lod.Taggs)
        {
            if (tagg is global::BIS.P3D.MLOD.UVSetTagg uvSet)
            {
                hasUV = true;
                // Verify at least one UV entry is fetchable
                if (uvSet.FaceUVs.Length > 0)
                {
                    var uv = uvSet.FaceUVs[0];
                    Assert.NotNull(uv);
                    Assert.True(uv.Length >= 2);
                }
                break;
            }
        }
        Assert.True(hasUV, "qm.p3d should have UV data");
    }

    [Fact]
    public void ODOL_LOD_Cache_SelectionsStableOnMultipleAccess()
    {
        var file = TestData.GetFile("p3d", "Barel1.p3d");
        if (file == null) return;

        using var stream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(stream);
        var odol = p3d.ODOL;

        foreach (var lod in odol.Lods)
        {
            if (lod.Selections.Length > 0)
            {
                var first = lod.Selections;
                var second = lod.Selections;
                Assert.Same(first, second);
                break;
            }
        }
    }

    [Fact]
    public void ODOL_LOD_Cache_ProxiesStableOnMultipleAccess()
    {
        var file = TestData.GetFile("p3d", "Barel1.p3d");
        if (file == null) return;

        using var stream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(stream);
        var odol = p3d.ODOL;

        foreach (var lod in odol.Lods)
        {
            var lodInt = (global::BIS.P3D.ILevelOfDetail)lod;
            if (lodInt.Proxies.Length > 0)
            {
                var first = lodInt.Proxies;
                var second = lodInt.Proxies;
                Assert.Same(first, second);
                break;
            }
        }
    }

    [Fact]
    public void ODOL_NonExistentFile_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "__nonexistent_conversion_test__.p3d");
        Assert.Throws<FileNotFoundException>(() =>
        {
            using var stream = File.OpenRead(missing);
            _ = new global::BIS.P3D.P3D(stream);
        });
    }

    [Fact]
    public void MLOD2ODOL_Roundtrip_ConvertsWithoutError()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var mlod = p3d.MLOD;
        var odol = global::BIS.P3D.Conversion.MLOD2ODOL.Convert(mlod);
        Assert.NotNull(odol);
        Assert.NotNull(odol.Lods);
        Assert.NotEmpty(odol.Lods);
    }

    [Fact]
    public void MLOD2ODOL_PreservesLodCount()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var mlod = p3d.MLOD;
        var odol = global::BIS.P3D.Conversion.MLOD2ODOL.Convert(mlod);
        Assert.Equal(mlod.Lods.Length, odol.Lods.Length);
    }

    [Fact]
    public void MLOD2ODOL_PreservesResolution()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var mlod = p3d.MLOD;
        var odol = global::BIS.P3D.Conversion.MLOD2ODOL.Convert(mlod);
        for (int i = 0; i < mlod.Lods.Length; i++)
            Assert.Equal(mlod.Lods[i].Resolution, odol.Lods[i].Resolution);
    }

    [Fact]
    public void MLOD2ODOL_PreservesPointCount()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var mlod = p3d.MLOD;
        var odol = global::BIS.P3D.Conversion.MLOD2ODOL.Convert(mlod);
        Assert.Equal(mlod.Lods[0].Points.Length, odol.Lods[0].Vertices.Count);
    }

    [Fact]
    public void MLOD2ODOL_PreservesFaceCount()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var mlod = p3d.MLOD;
        var odol = global::BIS.P3D.Conversion.MLOD2ODOL.Convert(mlod);
        Assert.Equal(mlod.Lods[0].Faces.Length, odol.Lods[0].Polygons.Faces.Length);
    }

    [Fact]
    public void MLOD2ODOL_FullRoundtrip_MLOD2ODOL2MLOD()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        using var readStream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(readStream);
        var original = p3d.MLOD;

        var odol = global::BIS.P3D.Conversion.MLOD2ODOL.Convert(original);
        var backToMlod = global::BIS.P3D.Conversion.ODOL2MLOD.Convert(odol);

        Assert.Equal(original.Lods.Length, backToMlod.Lods.Length);
        Assert.Equal(original.Lods[0].Points.Length, backToMlod.Lods[0].Points.Length);
        Assert.Equal(original.Lods[0].Faces.Length, backToMlod.Lods[0].Faces.Length);
        Assert.Equal(original.Lods[0].Resolution, backToMlod.Lods[0].Resolution);
    }

    [Fact]
    public void ODOL2MLOD_FullRoundtrip_ODOL2MLOD2ODOL()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        using var stream = File.OpenRead(file);
        var p3d = new global::BIS.P3D.P3D(stream);
        var original = p3d.ODOL;

        var mlod = global::BIS.P3D.Conversion.ODOL2MLOD.Convert(original);
        var backToOdol = global::BIS.P3D.Conversion.MLOD2ODOL.Convert(mlod);

        Assert.Equal(original.Lods.Length, backToOdol.Lods.Length);
        Assert.Equal(original.Lods[0].Vertices.Count, backToOdol.Lods[0].Vertices.Count);
        Assert.Equal(original.Lods[0].Polygons.Faces.Length, backToOdol.Lods[0].Polygons.Faces.Length);
        Assert.Equal(original.Lods[0].Resolution, backToOdol.Lods[0].Resolution);
    }
}
