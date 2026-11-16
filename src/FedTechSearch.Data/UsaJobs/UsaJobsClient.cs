using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FedTechSearch.Data.UsaJobs;

public sealed class UsaJobsOptions
{
    public const string Section = "UsaJobs";

    /// <summary>Free key from https://developer.usajobs.gov/apirequest/</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>The email you registered the key with; USAJOBS requires it as the User-Agent.</summary>
    public string UserAgentEmail { get; set; } = "";

    public string BaseUrl { get; set; } = "https://data.usajobs.gov";

    /// <summary>
    /// Occupational series to pull, semicolon separated. 2210 IT Management, 1550 Computer Science,
    /// 1560 Data Science, 0854 Computer Engineering.
    /// </summary>
    public string SeriesCodes { get; set; } = "2210;1550;1560;0854";

    public int ResultsPerPage { get; set; } = 500;

    /// <summary>Safety stop so a bad NumberOfPages value cannot loop forever.</summary>
    public int MaxPages { get; set; } = 40;
}

public sealed class UsaJobsClient
{
    private readonly HttpClient _http;
    private readonly UsaJobsOptions _options;

    public UsaJobsClient(HttpClient http, IOptions<UsaJobsOptions> options)
    {
        _options = options.Value;
        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgentEmail);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization-Key", _options.ApiKey);
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.UserAgentEmail);

    /// <summary>Streams every result page for the configured series.</summary>
    public async IAsyncEnumerable<JsonDocument> SearchAllPagesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var totalPages = 1;
        for (var page = 1; page <= totalPages && page <= _options.MaxPages; page++)
        {
            var url = "api/search"
                      + $"?JobCategoryCode={Uri.EscapeDataString(_options.SeriesCodes)}"
                      + $"&ResultsPerPage={_options.ResultsPerPage}"
                      + $"&Page={page}"
                      + "&Fields=Full";

            using var response = await _http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            totalPages = UsaJobsMapper.NumberOfPages(doc.RootElement);
            var hasItems = UsaJobsMapper.Items(doc.RootElement).Any();
            yield return doc;
            if (!hasItems) yield break;
        }
    }
}
