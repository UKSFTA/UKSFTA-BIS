using System.Text;
using System.Xml.Linq;
using Xunit;

namespace BIS.Stringtable.Test;

public class StringtableTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project name="MyMod">
          <Package name="MyMod">
            <Key ID="STR_MyMod_Hello">
              <Original>Hello</Original>
              <English>Hello</English>
              <French>Bonjour</French>
              <German>Hallo</German>
            </Key>
            <Key ID="STR_MyMod_Goodbye">
              <Original>Goodbye</Original>
              <English>Goodbye</English>
              <French>Au revoir</French>
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
    public void Load_ParsesProjectName()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        Assert.Equal("MyMod", table.Project.Name);
    }

    [Fact]
    public void Load_ParsesPackageCount()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        Assert.Equal(2, table.Project.Packages.Count);
    }

    [Fact]
    public void Load_ParsesKeyCounts()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        Assert.Equal(2, table.Project.Packages[0].Keys.Count);
        Assert.Single(table.Project.Packages[1].Keys);
    }

    [Fact]
    public void Load_ParsesKeyIds()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        Assert.Equal("STR_MyMod_Hello", table.Project.Packages[0].Keys[0].ID);
        Assert.Equal("STR_MyMod_Goodbye", table.Project.Packages[0].Keys[1].ID);
    }

    [Fact]
    public void Load_ParsesTranslations()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var hello = table.Project.Packages[0].Keys[0];
        Assert.Equal("Hello", hello.Translations["Original"]);
        Assert.Equal("Hello", hello.Translations["English"]);
        Assert.Equal("Bonjour", hello.Translations["French"]);
        Assert.Equal("Hallo", hello.Translations["German"]);
    }

    [Fact]
    public void Load_AllKeys_ReturnsAllKeys()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        Assert.Equal(3, table.AllKeys().Count());
    }

    [Fact]
    public void Load_Languages_ReturnsUniqueLanguages()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var langs = table.Languages();
        Assert.Equal(4, langs.Count); // Original, English, French, German
        Assert.Contains("French", langs);
        Assert.Contains("German", langs);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip_PreservesContent()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        using var ms = new MemoryStream();
        StringtableXml.Save(table, ms);
        ms.Position = 0;

        var reloaded = StringtableXml.Load(ms);
        Assert.Equal(table.Project.Name, reloaded.Project.Name);
        Assert.Equal(table.Project.Packages.Count, reloaded.Project.Packages.Count);
        Assert.Equal(table.AllKeys().Count(), reloaded.AllKeys().Count());

        var origHello = table.AllKeys().First(k => k.ID == "STR_MyMod_Hello");
        var reloadedHello = reloaded.AllKeys().First(k => k.ID == "STR_MyMod_Hello");
        Assert.Equal(origHello.Translations.Count, reloadedHello.Translations.Count);
        Assert.Equal(origHello.Translations["French"], reloadedHello.Translations["French"]);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip_XmlIsWellFormed()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var xml = StringtableXml.ToXmlString(table);
        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root);
        Assert.Equal("Project", doc.Root.Name.LocalName);
    }

    [Fact]
    public void ToXmlString_ContainsProjectName()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var xml = StringtableXml.ToXmlString(table);
        Assert.Contains("name=\"MyMod\"", xml);
    }

    [Fact]
    public void FindMissingLanguages_ReturnsCorrect()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var missing = StringtableUtil.FindMissingLanguages(table, "French", "German");

        // Hello has both French and German
        Assert.DoesNotContain("STR_MyMod_Hello", missing.Keys);

        // Goodbye has French but not German
        Assert.Contains("STR_MyMod_Goodbye", missing.Keys);
        Assert.Contains("German", missing["STR_MyMod_Goodbye"]);

        // Mission1 has neither French nor German
        Assert.Contains("STR_MyMod_Mission1", missing.Keys);
        Assert.Contains("French", missing["STR_MyMod_Mission1"]);
        Assert.Contains("German", missing["STR_MyMod_Mission1"]);
    }

    [Fact]
    public void FindDuplicateKeys_ReturnsDuplicates()
    {
        var table = new Stringtable(new Project("Test",
            new Package("A", new Key("STR_Dup", new Dictionary<string, string> { ["English"] = "Hi" })),
            new Package("B", new Key("STR_Dup", new Dictionary<string, string> { ["English"] = "Hello" }))
        ));

        var dupes = StringtableUtil.FindDuplicateKeys(table);
        Assert.Single(dupes);
        Assert.Equal("STR_Dup", dupes.First().Key);
    }

    [Fact]
    public void FindMissingKeys_ReturnsCorrect()
    {
        var source = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var target = new Stringtable(new Project("MyMod",
            new Package("MyMod",
                new Key("STR_MyMod_Hello", new Dictionary<string, string> { ["English"] = "Hello" }))
        ));

        var missing = StringtableUtil.FindMissingKeys(source, target).ToList();
        Assert.Equal(2, missing.Count);
        Assert.Contains(missing, k => k.ID == "STR_MyMod_Goodbye");
        Assert.Contains(missing, k => k.ID == "STR_MyMod_Mission1");
    }

    [Fact]
    public void Merge_AddsMissingKeys()
    {
        var target = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var source = new Stringtable(new Project("MyMod",
            new Package("MyMod",
                new Key("STR_MyMod_NewKey", new Dictionary<string, string> { ["English"] = "New" }))
        ));

        var merged = StringtableUtil.Merge(target, source);
        Assert.Equal(4, merged.AllKeys().Count());
        Assert.NotNull(merged.AllKeys().FirstOrDefault(k => k.ID == "STR_MyMod_NewKey"));
    }

    [Fact]
    public void Merge_DoesNotOverwriteByDefault()
    {
        var target = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var source = new Stringtable(new Project("MyMod",
            new Package("MyMod",
                new Key("STR_MyMod_Hello", new Dictionary<string, string> { ["French"] = "SALUT" }))
        ));

        var merged = StringtableUtil.Merge(target, source);
        var hello = merged.AllKeys().First(k => k.ID == "STR_MyMod_Hello");
        Assert.Equal("Bonjour", hello.Translations["French"]); // not overwritten
    }

    [Fact]
    public void Merge_OverwriteWhenSpecified()
    {
        var target = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var source = new Stringtable(new Project("MyMod",
            new Package("MyMod",
                new Key("STR_MyMod_Hello", new Dictionary<string, string> { ["French"] = "SALUT" }))
        ));

        var merged = StringtableUtil.Merge(target, source, overwrite: true);
        var hello = merged.AllKeys().First(k => k.ID == "STR_MyMod_Hello");
        Assert.Equal("SALUT", hello.Translations["French"]);
    }

    [Fact]
    public void CountByLanguage_ReturnsCorrectCounts()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var counts = StringtableUtil.CountByLanguage(table);
        Assert.Equal(3, counts["Original"]);    // all 3 keys
        Assert.Equal(3, counts["English"]);     // all 3 keys
        Assert.Equal(2, counts["French"]);      // Hello + Goodbye
        Assert.Equal(1, counts["German"]);      // Hello only
    }

    [Fact]
    public void ValidateKeyNaming_ReturnsNoErrorsForCorrectPrefix()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var errors = StringtableUtil.ValidateKeyNaming(table, "STR_MyMod_").ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateKeyNaming_FindsWrongPrefix()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var errors = StringtableUtil.ValidateKeyNaming(table, "STR_Wrong_").ToList();
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Load_ThrowsOnNullProject()
    {
        Assert.Throws<ArgumentNullException>(() => new Stringtable(null));
    }

    [Fact]
    public void Load_ThrowsOnMissingProjectName()
    {
        var xml = """<?xml version="1.0" encoding="utf-8"?><Project></Project>""";
        Assert.Throws<InvalidOperationException>(() =>
            StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Load_ThrowsOnMissingPackageName()
    {
        var xml = """<?xml version="1.0" encoding="utf-8"?><Project name="X"><Package></Package></Project>""";
        Assert.Throws<InvalidOperationException>(() =>
            StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Load_ThrowsOnMissingKeyId()
    {
        var xml = """<?xml version="1.0" encoding="utf-8"?><Project name="X"><Package name="X"><Key></Key></Package></Project>""";
        Assert.Throws<InvalidOperationException>(() =>
            StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Load_EmptyPackages()
    {
        var xml = """<?xml version="1.0" encoding="utf-8"?><Project name="Empty"><Package name="Empty"></Package></Project>""";
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        Assert.Single(table.Project.Packages);
        Assert.Empty(table.Project.Packages[0].Keys);
    }

    [Fact]
    public void KnownLanguages_IsPopulated()
    {
        Assert.Contains("English", StringtableUtil.KnownLanguages);
        Assert.Contains("French", StringtableUtil.KnownLanguages);
        Assert.Contains("Korean", StringtableUtil.KnownLanguages);
        Assert.Contains("Chinese", StringtableUtil.KnownLanguages);
        Assert.True(StringtableUtil.KnownLanguages.Count > 20);
    }

    [Fact]
    public void Load_KeysFromFile_Roundtrips()
    {
        var table = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));
        var xml = StringtableXml.ToXmlString(table);

        var reloaded = StringtableXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        foreach (var key in table.AllKeys())
        {
            var rk = reloaded.AllKeys().First(k => k.ID == key.ID);
            Assert.Equal(key.Translations.Count, rk.Translations.Count);
            foreach (var (lang, text) in key.Translations)
                Assert.Equal(text, rk.Translations[lang]);
        }
    }

    [Fact]
    public void TranslationKeys_AreCaseInsensitive()
    {
        var table = new Stringtable(new Project("Test",
            new Package("Test",
                new Key("STR_Test", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["english"] = "Hello",
                    ["FRENCH"] = "Bonjour"
                }))));

        var hello = table.AllKeys().First();
        // Reading with different casing
        Assert.Equal("Hello", hello.Translations["English"]);
        Assert.Equal("Bonjour", hello.Translations["French"]);
    }
}
