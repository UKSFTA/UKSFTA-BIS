using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BIS.Stringtable;

/// <summary>Reads and writes stringtable.xml files in Arma 3 format.</summary>
public static class StringtableXml
{
    /// <summary>Load a stringtable from an XML file path.</summary>
    public static Stringtable Load(string path)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        return Parse(doc);
    }

    /// <summary>Load a stringtable from a stream.</summary>
    public static Stringtable Load(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        return Parse(doc);
    }

    /// <summary>Load a stringtable from an XML reader.</summary>
    public static Stringtable Load(XmlReader reader)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        return Parse(doc);
    }

    /// <summary>Save a stringtable to an XML file path.</summary>
    public static void Save(Stringtable table, string path)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (path == null) throw new ArgumentNullException(nameof(path));
        var doc = ToXDocument(table);
        doc.Save(path);
    }

    /// <summary>Save a stringtable to a stream.</summary>
    public static void Save(Stringtable table, Stream stream)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        var doc = ToXDocument(table);
        doc.Save(stream);
    }

    /// <summary>Save a stringtable to an XML writer.</summary>
    public static void Save(Stringtable table, XmlWriter writer)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (writer == null) throw new ArgumentNullException(nameof(writer));
        var doc = ToXDocument(table);
        doc.Save(writer);
    }

    /// <summary>Convert a Stringtable to its XML string representation.</summary>
    public static string ToXmlString(Stringtable table)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        var doc = ToXDocument(table);
        using var ms = new MemoryStream();
        doc.Save(ms);
        ms.Position = 0;
        using var sr = new StreamReader(ms);
        return sr.ReadToEnd();
    }

    private static Stringtable Parse(XDocument doc)
    {
        var projectEl = doc.Root;
        if (projectEl == null || projectEl.Name.LocalName != "Project")
            throw new InvalidOperationException("Root element must be <Project>.");

        var projectName = projectEl.Attribute("name")?.Value
            ?? throw new InvalidOperationException("<Project> requires a 'name' attribute.");

        var packages = new List<Package>();
        foreach (var packageEl in projectEl.Elements("Package"))
        {
            var packageName = packageEl.Attribute("name")?.Value
                ?? throw new InvalidOperationException("<Package> requires a 'name' attribute.");

            var keys = new List<Key>();
            foreach (var keyEl in packageEl.Elements("Key"))
            {
                var keyId = keyEl.Attribute("ID")?.Value
                    ?? throw new InvalidOperationException("<Key> requires an 'ID' attribute.");

                var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var langEl in keyEl.Elements())
                {
                    var langName = langEl.Name.LocalName;
                    var text = langEl.Value;
                    translations[langName] = text;
                }

                keys.Add(new Key(keyId, translations));
            }

            packages.Add(new Package(packageName, keys));
        }

        return new Stringtable(new Project(projectName, packages));
    }

    private static XDocument ToXDocument(Stringtable table)
    {
        var doc = new XDocument(new XDeclaration("1.0", null, null));
        var projectEl = new XElement("Project", new XAttribute("name", table.Project.Name));

        foreach (var package in table.Project.Packages)
        {
            var packageEl = new XElement("Package", new XAttribute("name", package.Name));
            foreach (var key in package.Keys)
            {
                var keyEl = new XElement("Key", new XAttribute("ID", key.ID));
                foreach (var (lang, text) in key.Translations)
                {
                    keyEl.Add(new XElement(lang, text));
                }
                packageEl.Add(keyEl);
            }
            projectEl.Add(packageEl);
        }

        doc.Add(projectEl);
        return doc;
    }
}
