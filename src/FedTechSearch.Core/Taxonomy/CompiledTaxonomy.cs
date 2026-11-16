using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FedTechSearch.Core.Taxonomy;

public sealed class TaxonomyDocument
{
    public int Version { get; init; }
    public List<SpecialtyDef> Specialties { get; init; } = [];
    public List<RoleFamilyDef> RoleFamilies { get; init; } = [];
    public List<TechDef> Tech { get; init; } = [];
}

public sealed class SpecialtyDef
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public List<string> Aliases { get; init; } = [];
}

public sealed class RoleFamilyDef
{
    public string Name { get; init; } = "";
    public string WorkingTitle { get; init; } = "";
    public List<string> Specialties { get; init; } = [];
    public List<string> Series { get; init; } = [];
    public List<string> TitlePatterns { get; init; } = [];
    public List<string> TextPatterns { get; init; } = [];
}

public sealed class TechDef
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public List<string> Patterns { get; init; } = [];
    public bool CaseSensitive { get; init; }
}

public sealed record CompiledRoleFamily(
    RoleFamilyDef Definition,
    IReadOnlyList<Regex> TitlePatterns,
    IReadOnlyList<Regex> TextPatterns);

public sealed record CompiledTech(TechDef Definition, IReadOnlyList<Regex> Patterns);

/// <summary>
/// The taxonomy (specialty codes, role families, tech terms) with every pattern compiled once.
/// It is data-driven so new terms can be added to taxonomy.json without code changes.
/// </summary>
public sealed class CompiledTaxonomy
{
    public const string GeneralFamily = "General IT";
    public const string GeneralWorkingTitle = "IT Specialist";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public int Version { get; }
    public IReadOnlyList<CompiledRoleFamily> RoleFamilies { get; }
    public IReadOnlyList<CompiledTech> Tech { get; }
    public IReadOnlyList<SpecialtyDef> Specialties { get; }

    /// <summary>Upper-cased alias (e.g. "APPLICATIONS SOFTWARE") to specialty code (e.g. "APPSW").</summary>
    public IReadOnlyDictionary<string, string> SpecialtyCodeByAlias { get; }

    /// <summary>Specialty code to display name.</summary>
    public IReadOnlyDictionary<string, string> SpecialtyNameByCode { get; }

    /// <summary>Tech name (case-insensitive) to its canonical spelling.</summary>
    public IReadOnlyDictionary<string, string> CanonicalTechName { get; }

    public IReadOnlyList<string> RoleFamilyNames { get; }

    private CompiledTaxonomy(TaxonomyDocument doc)
    {
        Version = doc.Version;
        Specialties = doc.Specialties;

        RoleFamilies = doc.RoleFamilies
            .Select(f => new CompiledRoleFamily(
                f,
                f.TitlePatterns.Select(p => Compile(p, caseSensitive: false)).ToList(),
                f.TextPatterns.Select(p => Compile(p, caseSensitive: false)).ToList()))
            .ToList();

        Tech = doc.Tech
            .Select(t => new CompiledTech(t, t.Patterns.Select(p => Compile(p, t.CaseSensitive)).ToList()))
            .ToList();

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in doc.Specialties)
        {
            aliases[NormalizeSpecialtyToken(s.Code)] = s.Code;
            foreach (var a in s.Aliases)
                aliases[NormalizeSpecialtyToken(a)] = s.Code;
        }
        SpecialtyCodeByAlias = aliases;
        SpecialtyNameByCode = doc.Specialties.ToDictionary(s => s.Code, s => s.Name, StringComparer.Ordinal);

        CanonicalTechName = doc.Tech
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        RoleFamilyNames = doc.RoleFamilies.Select(f => f.Name).Append(GeneralFamily).ToList();
    }

    public static CompiledTaxonomy FromJson(string json)
    {
        var doc = JsonSerializer.Deserialize<TaxonomyDocument>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Taxonomy JSON was empty.");
        return new CompiledTaxonomy(doc);
    }

    public static CompiledTaxonomy FromFile(string path) => FromJson(File.ReadAllText(path));

    public static CompiledTaxonomy LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("taxonomy.json")
                           ?? throw new InvalidOperationException("Embedded taxonomy.json not found.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    public static string NormalizeSpecialtyToken(string token) =>
        Regex.Replace(token.Trim().ToUpperInvariant(), @"\s+", " ");

    private static Regex Compile(string pattern, bool caseSensitive)
    {
        var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (!caseSensitive) options |= RegexOptions.IgnoreCase;
        return new Regex(pattern, options, MatchTimeout);
    }
}
