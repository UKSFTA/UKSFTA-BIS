using System.Text;
using Xunit;

namespace BIS.Stringtable.Test;

public class StringtableLinterTest
{
    private static Stringtable Load(string xml)
    {
        return StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
    }

    private const string SampleXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project name="MyMod">
          <Package name="MyMod">
            <Key ID="STR_MyMod_Goodbye">
              <Original>Goodbye</Original>
              <English>Goodbye</English>
              <French>Au revoir</French>
            </Key>
            <Key ID="STR_MyMod_Hello">
              <Original>Hello</Original>
              <English>Hello</English>
              <French>Bonjour</French>
              <German>Hallo</German>
            </Key>
          </Package>
          <Package name="MyMod_Missions">
            <Key ID="STR_MyMod_Mission1">
              <Original>First Mission</Original>
              <English>First Mission</English>
            </Key>
          </Package>
        </Project>
        """;

    [Fact]
    public void Lint_WellFormedStringtable_NoDiagnostics()
    {
        var table = Load(SampleXml);
        var linter = new StringtableLinter();
        var results = linter.Lint(table);
        Assert.Empty(results);
    }

    [Fact]
    public void Lint_L01_NotSorted_Diagnostic()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project name="Test">
              <Package name="Test">
                <Key ID="STR_B_Key"><Original>B</Original></Key>
                <Key ID="STR_A_Key"><Original>A</Original></Key>
              </Package>
            </Project>
            """;
        var table = Load(xml);
        var linter = new StringtableLinter();
        var results = linter.Lint(table);
        Assert.Contains(results, d => d.Code == "L-L01");
    }

    [Fact]
    public void Lint_L03_WhitespaceInTranslation_Diagnostic()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project name="Test">
              <Package name="Test">
                <Key ID="STR_Test">
                  <Original>  Hello with leading spaces</Original>
                  <English>Trailing spaces  </English>
                </Key>
              </Package>
            </Project>
            """;
        var table = Load(xml);
        var linter = new StringtableLinter();
        var results = linter.Lint(table);
        Assert.Equal(2, results.Count(d => d.Code == "L-L03"));
    }

    [Fact]
    public void Lint_L04_UnknownLanguage_Diagnostic()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project name="Test">
              <Package name="Test">
                <Key ID="STR_Test">
                  <Original>Hello</Original>
                  <Klingon>qo'</Klingon>
                </Key>
              </Package>
            </Project>
            """;
        var table = Load(xml);
        var linter = new StringtableLinter();
        var results = linter.Lint(table);
        Assert.Contains(results, d => d.Code == "L-L04" && d.Language == "Klingon");
    }

    [Fact]
    public void Lint_L05_EmptyKey_Diagnostic()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project name="Test">
              <Package name="Test">
                <Key ID="STR_Empty">
                </Key>
              </Package>
            </Project>
            """;
        var table = Load(xml);
        var linter = new StringtableLinter();
        var results = linter.Lint(table);
        Assert.Contains(results, d => d.Code == "L-L05");
    }

    [Fact]
    public void Lint_L06_EmptyTranslation_Diagnostic()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project name="Test">
              <Package name="Test">
                <Key ID="STR_Test">
                  <Original>Hello</Original>
                  <English></English>
                  <French>  </French>
                </Key>
              </Package>
            </Project>
            """;
        var table = Load(xml);
        var linter = new StringtableLinter();
        var results = linter.Lint(table);
        Assert.Equal(2, results.Count(d => d.Code == "L-L06"));
    }

    [Fact]
    public void Lint_L07_MissingOriginal_Diagnostic()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project name="Test">
              <Package name="Test">
                <Key ID="STR_Test">
                  <English>Hello</English>
                </Key>
              </Package>
            </Project>
            """;
        var table = Load(xml);
        var linter = new StringtableLinter();
        var results = linter.Lint(table);
        Assert.Contains(results, d => d.Code == "L-L07");
    }

    [Fact]
    public void Lint_MultiplePackages_ChecksAll()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project name="Test">
              <Package name="A">
                <Key ID="STR_A_Z"><Original>Z</Original></Key>
                <Key ID="STR_A_A"><Original>A</Original></Key>
              </Package>
              <Package name="B">
                <Key ID="STR_B_Goodbye"><Original>Goodbye</Original></Key>
                <Key ID="STR_B_Hello"><Original>Hello</Original></Key>
              </Package>
            </Project>
            """;
        var table = Load(xml);
        var linter = new StringtableLinter();
        var results = linter.Lint(table);
        // Package A has unsorted keys (L-L01)
        Assert.Contains(results, d => d.Code == "L-L01" && d.Package == "A");
        // Package B is sorted — no L-L01
        Assert.DoesNotContain(results, d => d.Code == "L-L01" && d.Package == "B");
    }

    [Fact]
    public void Lint_AllCodes_ReportedCorrectly()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project name="Test">
              <Package name="Test">
                <Key ID="STR_B"><Original>B</Original></Key>
                <Key ID="STR_A"><Original>  A  </Original></Key>
                <Key ID="STR_C"><Original>C</Original><Klingon></Klingon></Key>
              </Package>
            </Project>
            """;
        var table = Load(xml);
        var linter = new StringtableLinter();
        var results = linter.Lint(table);

        // L-L01: unsorted (STR_B before STR_A)
        Assert.Contains(results, d => d.Code == "L-L01");
        // L-L03: whitespace on A
        Assert.Contains(results, d => d.Code == "L-L03" && d.KeyId == "STR_A");
        // L-L04: unknown language Klingon
        Assert.Contains(results, d => d.Code == "L-L04" && d.Language == "Klingon");
        // L-L06: empty Klingon translation
        Assert.Contains(results, d => d.Code == "L-L06" && d.Language == "Klingon");
    }
}
