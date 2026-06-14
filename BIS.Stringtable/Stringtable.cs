using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BIS.Stringtable;

/// <summary>Top-level stringtable container, corresponding to a single stringtable.xml file.</summary>
public class Stringtable
{
    /// <summary>The Project element wrapping all packages.</summary>
    public Project Project { get; }

    public Stringtable(Project project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
    }

    /// <summary>All keys across all packages.</summary>
    public IEnumerable<Key> AllKeys() => Project.Packages.SelectMany(p => p.Keys);

    /// <summary>All unique languages present across all keys.</summary>
    public ISet<string> Languages()
    {
        var langs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in AllKeys())
        {
            foreach (var lang in key.Translations.Keys)
                langs.Add(lang);
        }
        return langs;
    }
}

/// <summary>A Project element containing one or more Package elements.</summary>
public class Project
{
    public string Name { get; }
    public IReadOnlyList<Package> Packages { get; }

    public Project(string name, IReadOnlyList<Package> packages)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Packages = packages ?? throw new ArgumentNullException(nameof(packages));
    }

    public Project(string name, params Package[] packages)
        : this(name, (IReadOnlyList<Package>)packages)
    {
    }
}

/// <summary>A Package element containing zero or more Key elements.</summary>
public class Package
{
    public string Name { get; }
    public IReadOnlyList<Key> Keys { get; }

    public Package(string name, IReadOnlyList<Key> keys)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public Package(string name, params Key[] keys)
        : this(name, (IReadOnlyList<Key>)keys)
    {
    }
}

/// <summary>A single localizable string with translations across languages.</summary>
public class Key
{
    /// <summary>The stringtable ID (e.g., "STR_MyMod_MyString").</summary>
    public string ID { get; }

    /// <summary>Language → translated text. Language codes are case-insensitive.</summary>
    public IReadOnlyDictionary<string, string> Translations { get; }

    public Key(string id, IReadOnlyDictionary<string, string> translations)
    {
        ID = id ?? throw new ArgumentNullException(nameof(id));
        Translations = translations != null
            ? new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(translations, StringComparer.OrdinalIgnoreCase))
            : throw new ArgumentNullException(nameof(translations));
    }
}
