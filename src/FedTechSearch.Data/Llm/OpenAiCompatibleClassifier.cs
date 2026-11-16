using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Taxonomy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FedTechSearch.Data.Llm;

public sealed class LlmOptions
{
    public const string Section = "Llm";

    public LlmMode Mode { get; set; } = LlmMode.Off;

    /// <summary>
    /// Any OpenAI-compatible /v1 endpoint. Defaults to a local Ollama server so job text
    /// never leaves your machine; LM Studio, vLLM or a hosted provider work the same way.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    public string Model { get; set; } = "llama3.1:8b";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxInputChars { get; set; } = 6000;
}

/// <summary>
/// Calls an OpenAI-compatible chat completions endpoint in JSON mode. Any failure returns null
/// so ingest falls back to the rule-based fields instead of stopping.
/// </summary>
public sealed class OpenAiCompatibleClassifier : ILlmClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<OpenAiCompatibleClassifier> _logger;

    public OpenAiCompatibleClassifier(HttpClient http, IOptions<LlmOptions> options, ILogger<OpenAiCompatibleClassifier> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new("Bearer", _options.ApiKey);
    }

    public async Task<LlmClassification?> ClassifyAsync(
        RawJob job, EnrichedFields ruleFields, CompiledTaxonomy taxonomy, CancellationToken cancellationToken)
    {
        var request = new ChatRequest(
            _options.Model,
            [
                new ChatMessage("system", SystemPrompt(taxonomy)),
                new ChatMessage("user", UserPrompt(job, ruleFields))
            ],
            Temperature: 0,
            ResponseFormat: new ResponseFormat("json_object"));

        try
        {
            using var response = await _http.PostAsJsonAsync("chat/completions", request, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, cancellationToken);
            var content = body?.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(content) ? null : Parse(content);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning("LLM classification failed for {JobId}: {Message}", job.Id, ex.Message);
            return null;
        }
    }

    public static LlmClassification? Parse(string content)
    {
        var json = content.Trim();
        // Some models wrap JSON in a markdown fence even in JSON mode.
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            json = json[start..(end + 1)];
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        string? Get(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var tech = root.TryGetProperty("tech", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : new List<string>();

        return new LlmClassification(Get("workingTitle"), Get("roleFamily"), tech);
    }

    private static string SystemPrompt(CompiledTaxonomy taxonomy)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You classify U.S. federal technology job announcements for a job search site.");
        sb.AppendLine("Federal titles like \"IT Specialist (SYSADMIN)\" hide the real job. Read the duties and say what the private-sector equivalent job is.");
        sb.AppendLine("Respond with one JSON object only, with exactly these keys:");
        sb.AppendLine("  \"workingTitle\": the job title a private-sector engineer would recognize, 2 to 6 words, no grade, no agency (e.g. \"Senior DevOps Engineer\", \"Help Desk Technician\").");
        sb.AppendLine("  \"roleFamily\": exactly one of: " + string.Join(", ", taxonomy.RoleFamilyNames.Select(n => $"\"{n}\"")) + ".");
        sb.AppendLine("  \"tech\": technologies the duties clearly require, chosen only from this list: " + string.Join(", ", taxonomy.Tech.Select(x => x.Definition.Name)) + ".");
        sb.AppendLine("Do not invent technologies that are not in the text.");
        return sb.ToString();
    }

    private string UserPrompt(RawJob job, EnrichedFields rules)
    {
        var body = new StringBuilder();
        body.AppendLine($"Official title: {job.Title}");
        body.AppendLine($"Agency: {job.Agency}");
        body.AppendLine($"Series: {string.Join(", ", job.Series)}");
        if (job.LowGrade is not null) body.AppendLine($"Grade: {job.PayPlan}-{job.LowGrade}" + (job.HighGrade != job.LowGrade ? $" to {job.HighGrade}" : ""));
        if (rules.Specialties.Count > 0) body.AppendLine($"Specialty: {string.Join(", ", rules.Specialties)}");
        body.AppendLine($"Summary: {job.Summary}");
        body.AppendLine("Duties:");
        foreach (var d in job.Duties) body.AppendLine($"- {d}");
        body.AppendLine($"Qualifications: {job.Qualifications}");

        var text = body.ToString();
        return text.Length <= _options.MaxInputChars ? text : text[.._options.MaxInputChars];
    }

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        double Temperature,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type);

    private sealed record ChatResponse(List<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessage? Message);
}
