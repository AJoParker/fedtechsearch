using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using System.Web;
using FedTechSearch.Data.Search;

namespace FedTechSearch.Data.Alerts;

/// <summary>
/// Saved searches are stored as the search page's query string (same keys as the URL), so an
/// alert runs exactly the search the person saw. This turns it into a <see cref="JobSearchRequest"/>
/// and back into one stable, canonical form.
/// </summary>
public static class AlertQuery
{
    public const int MaxLength = 2000;

    /// <summary>Paging and sorting are the digest's job; hidden posts live only in the visitor's browser.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(JobSearchRequest.Page), nameof(JobSearchRequest.PageSize), nameof(JobSearchRequest.Sort),
        nameof(JobSearchRequest.Hide), nameof(JobSearchRequest.IncludeClosed)
    };

    /// <summary>The request properties a query string may set, in declaration order.</summary>
    private static readonly PropertyInfo[] Settable = typeof(JobSearchRequest)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.SetMethod is { IsPublic: true } && !Ignored.Contains(p.Name))
        .ToArray();

    public static JobSearchRequest Parse(string? queryString)
    {
        var request = new JobSearchRequest();
        NameValueCollection values = HttpUtility.ParseQueryString(queryString?.TrimStart('?') ?? "");
        foreach (var property in Settable)
        {
            var raw = values.GetValues(Camel(property.Name)) ?? values.GetValues(property.Name);
            if (raw is null) continue;
            var items = raw.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
            if (items.Length == 0) continue;

            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object? value = type switch
            {
                _ when type == typeof(string[]) => items.Distinct().ToArray(),
                _ when type == typeof(string) => items[0],
                _ when type == typeof(int) => int.TryParse(items[0], CultureInfo.InvariantCulture, out var i) ? i : null,
                _ when type == typeof(decimal) => decimal.TryParse(items[0], CultureInfo.InvariantCulture, out var d) ? d : null,
                _ when type == typeof(bool) => bool.TryParse(items[0], out var b) ? b : null,
                _ => null
            };
            if (value is not null) property.SetValue(request, value);
        }
        return request;
    }

    /// <summary>Stable form for storage: declaration order, camelCase keys, repeated keys for lists.</summary>
    public static string Canonicalize(JobSearchRequest request)
    {
        var parts = new List<string>();
        foreach (var property in Settable)
        {
            var key = Camel(property.Name);
            switch (property.GetValue(request))
            {
                case null:
                    break;
                case string[] list:
                    parts.AddRange(list.Select(v => $"{key}={Uri.EscapeDataString(v)}"));
                    break;
                case bool b:
                    parts.Add($"{key}={(b ? "true" : "false")}");
                    break;
                case IFormattable f:
                    parts.Add($"{key}={Uri.EscapeDataString(f.ToString(null, CultureInfo.InvariantCulture))}");
                    break;
                case var v:
                    parts.Add($"{key}={Uri.EscapeDataString(v.ToString()!)}");
                    break;
            }
        }
        return string.Join("&", parts);
    }

    public static string Canonicalize(string? queryString) => Canonicalize(Parse(queryString));

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
