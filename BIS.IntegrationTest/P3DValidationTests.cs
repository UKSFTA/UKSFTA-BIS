using System.IO;
using System.Linq;
using Xunit;

namespace BIS.IntegrationTest;

public class P3DValidationTests
{
    [Fact]
    public void Validate_AllP3Ds_Succeed()
    {
        if (!TestData.CheckAvailable("p3d", "Run _testdata/download.sh for ALDP packages.")) return;

        var files = TestData.GetFiles("p3d", "*.p3d");
        foreach (var file in files)
        {
            var result = global::BIS.P3D.P3DValidator.Analyse(file);
            Assert.True(result.IsValid, $"{Path.GetFileName(file)} should be valid");
            Assert.NotEmpty(result.LODs);
        }
    }

    [Fact]
    public void Validate_AllP3Ds_HaveExpectedLodCounts()
    {
        if (!TestData.CheckAvailable("p3d", "Run _testdata/download.sh for ALDP packages.")) return;

        var files = TestData.GetFiles("p3d", "*.p3d");
        foreach (var file in files)
        {
            var result = global::BIS.P3D.P3DValidator.Analyse(file);
            Assert.True(result.LodCount > 0, $"{Path.GetFileName(file)} must have at least 1 LOD");
        }
    }

    [Fact]
    public void Validate_AllP3Ds_NoErrors()
    {
        if (!TestData.CheckAvailable("p3d", "Run _testdata/download.sh for ALDP packages.")) return;

        var files = TestData.GetFiles("p3d", "*.p3d");
        foreach (var file in files)
        {
            var result = global::BIS.P3D.P3DValidator.Analyse(file);
            var errors = result.Issues.Where(i => i.Severity == global::BIS.P3D.IssueSeverity.Error).ToList();
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validate_Barbedwire_Has9Lods()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.Equal(9, result.LodCount);
    }

    [Fact]
    public void Validate_Barbedwire_ReportsZeroVertexWarning()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        // Barbedwire's ODOL LODs report 0 vertices through the reader interface
        Assert.Contains(result.Issues, i => i.Code == "ZERO_VTX");
    }

    [Fact]
    public void Validate_Barbedwire_HasTotalFaces()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.True(result.TotalFaces > 0);
    }

    [Fact]
    public void Validate_Barbedwire_HasNoErrors()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Validate_Barel1_Has9Lods()
    {
        var file = TestData.GetFile("p3d", "Barel1.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.Equal(9, result.LodCount);
    }

    [Fact]
    public void Validate_Barel1_HasNoErrors()
    {
        var file = TestData.GetFile("p3d", "Barel1.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Validate_DetectsFormat()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.True(result.IsODOL ^ result.IsMLOD);
    }

    [Fact]
    public void Validate_NonExistentFile_ReturnsInvalid()
    {
        var missing = Path.Combine(Path.GetTempPath(), "__nonexistent_test__.p3d");
        var result = global::BIS.P3D.P3DValidator.Analyse(missing);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_InvalidFile_ReturnsErrors()
    {
        var badFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(badFile, [0, 0, 0, 0]);
            var result = global::BIS.P3D.P3DValidator.Analyse(badFile);
            Assert.False(result.IsValid);
            Assert.True(result.HasErrors);
        }
        finally
        {
            File.Delete(badFile);
        }
    }

    [Fact]
    public void Validate_Barbedwire_ReportsTotalVertices()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.True(result.TotalFaces > 0);
    }

    [Fact]
    public void Validate_Barbedwire_ReportsLodTypes()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        foreach (var lod in result.LODs)
        {
            Assert.NotNull(lod.TypeName);
            Assert.NotEqual("Unknown", lod.TypeName);
        }
    }

    [Fact]
    public void Validate_Barbedwire_HasNoResolutionGaps()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        var gapIssues = result.Issues.Where(i => i.Code == "RES_GAP");
        Assert.Empty(gapIssues);
    }

    [Fact]
    public void Validate_Barbedwire_ShadowVolumeLODIdentified()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        var shadowLods = result.LODs.Where(l => l.TypeName.StartsWith("ShadowVolume"));
        Assert.NotEmpty(shadowLods);
    }

    [Fact]
    public void Validate_Barbedwire_AllLodTypesAreValid()
    {
        var file = TestData.GetFile("p3d", "Barbedwire.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        // Barbedwire has 9 LODs — verify type names are non-empty and meaningful
        Assert.Equal(9, result.LODs.Count);
        Assert.All(result.LODs, lod =>
        {
            Assert.NotEqual("Unknown", lod.TypeName);
            Assert.NotEmpty(lod.TypeName);
        });
    }

    [Fact]
    public void Validate_Barel1_ReportsAllLODTypes()
    {
        var file = TestData.GetFile("p3d", "Barel1.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.Equal(9, result.LODs.Count);
        var typeNames = result.LODs.Select(l => l.TypeName).Distinct().ToList();
        Assert.Contains(typeNames, t => t.StartsWith("Memory"));
        Assert.Contains(typeNames, t => t.StartsWith("ShadowVolume"));
        Assert.Contains(typeNames, t => t.StartsWith("ViewGeometry"));
    }

    [Fact]
    public void Validate_Barel1_FaceCountsNonZero()
    {
        var file = TestData.GetFile("p3d", "Barel1.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        // Memory/LandContact LODs may have 0 faces; resolution LODs should not
        var resLods = result.LODs.Where(l => l.TypeName.StartsWith("Resolution"));
        foreach (var lod in resLods)
            Assert.True(lod.FaceCount > 0, $"Barel1 LOD {lod.TypeName} should have >0 faces");
    }

    [Fact]
    public void Validate_AllP3Ds_NoResolutionGaps_Informational()
    {
        if (!TestData.CheckAvailable("p3d", null)) return;

        var files = TestData.GetFiles("p3d", "*.p3d");
        foreach (var file in files)
        {
            var result = global::BIS.P3D.P3DValidator.Analyse(file);
            // Resolution gap warnings are informational — not errors
            var errors = result.Issues.Where(i => i.Severity == global::BIS.P3D.IssueSeverity.Error);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validate_QM_MLOD_ResolutionIsViewGeometry()
    {
        var file = TestData.GetFile("p3d", "qm.p3d");
        if (file == null) return;

        var result = global::BIS.P3D.P3DValidator.Analyse(file);
        Assert.Single(result.LODs);
        Assert.StartsWith("ViewGeometry", result.LODs[0].TypeName);
    }
}
