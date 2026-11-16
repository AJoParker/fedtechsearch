using System.Text.Json;
using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Taxonomy;
using FedTechSearch.Data.Llm;
using FedTechSearch.Data.UsaJobs;

namespace FedTechSearch.Core.Tests;

public sealed class FixtureData
{
    public CompiledTaxonomy Taxonomy { get; } = CompiledTaxonomy.LoadEmbedded();
    public Dictionary<string, RawJob> Jobs { get; }

    public FixtureData()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample-search.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Jobs = UsaJobsMapper.Items(doc.RootElement)
            .Select(UsaJobsMapper.Map)
            .OfType<RawJob>()
            .ToDictionary(j => j.AnnouncementNumber);
    }
}

public sealed class RuleExtractorTests(FixtureData data) : IClassFixture<FixtureData>
{
    private EnrichedFields Extract(string announcement) =>
        new RuleExtractor(data.Taxonomy).Extract(data.Jobs[announcement]);

    [Fact]
    public void Taxonomy_loads_and_every_pattern_compiles()
    {
        Assert.NotEmpty(data.Taxonomy.RoleFamilies);
        Assert.True(data.Taxonomy.Tech.Count > 50);
        Assert.Equal("APPSW", data.Taxonomy.SpecialtyCodeByAlias["APPLICATIONS SOFTWARE"]);
    }

    [Fact]
    public void Mapper_reads_the_fields_the_api_docs_leave_out()
    {
        var job = data.Jobs["VA-26-12345-APPSW"];
        Assert.Equal("812345600", job.Id);
        Assert.Equal(4, job.Duties.Count);
        Assert.Equal(12, job.LowGrade);
        Assert.Equal(13, job.HighGrade);
        Assert.Equal("GS", job.PayPlan);
        Assert.Equal(104604m, job.SalaryMin);
        Assert.True(job.TeleworkEligible);
        Assert.False(job.RemoteIndicator);
        Assert.Contains("public", job.HiringPaths);
        Assert.Equal(new[] { "District of Columbia" }, job.States);
        Assert.Equal(DateTimeKind.Utc, job.CloseDate!.Value.Kind);
        Assert.Equal("https://www.usajobs.gov/job/812345600", job.Url);
    }

    [Fact]
    public void AppSw_posting_becomes_software_engineer_with_its_stack()
    {
        var f = Extract("VA-26-12345-APPSW");
        Assert.Equal(new[] { "APPSW" }, f.SpecialtyCodes);
        Assert.Equal("Software Engineering", f.RoleFamily);
        Assert.False(f.LowConfidence);
        Assert.Equal("Full Stack Software Developer", f.WorkingTitle);
        Assert.Equal(WorkingTitleSource.Posting, f.WorkingTitleSource);
        foreach (var t in new[] { "C#", ".NET", "ASP.NET", "Angular", "TypeScript", "SQL Server", "Azure DevOps", "CI/CD", "REST APIs" })
            Assert.Contains(t, f.Tech);
        Assert.Equal(new[] { Seniority.Mid, Seniority.Senior }, f.Seniority);
        Assert.Equal(WorkArrangement.Telework, f.WorkArrangement);
        Assert.Equal(Clearance.None, f.Clearance);
        Assert.True(f.OpenToPublic);
    }

    [Fact]
    public void Duties_beat_the_parenthetical_when_a_sysadmin_title_hides_a_devops_job()
    {
        var f = Extract("DISA-26-0042");
        Assert.Equal(new[] { "SYSADMIN", "NETWORK" }, f.SpecialtyCodes);
        Assert.Equal("DevOps & Cloud", f.RoleFamily);
        Assert.True(f.LowConfidence); // close call against Systems Administration: worth an LLM second opinion
        foreach (var t in new[] { "Kubernetes", "Terraform", "AWS", "Helm", "GitLab", "Linux" })
            Assert.Contains(t, f.Tech);
        Assert.Equal(Clearance.Secret, f.Clearance);
        Assert.False(f.OpenToPublic);
        Assert.Equal("Senior DevOps / Cloud Engineer", f.WorkingTitle);
    }

    [Fact]
    public void Cybersecurity_posting_gets_ts_sci_and_security_tools()
    {
        var f = Extract("DHS-CISA-26-777");
        Assert.Equal("Cybersecurity", f.RoleFamily);
        Assert.Equal(Clearance.TsSci, f.Clearance);
        Assert.Equal(WorkArrangement.OnSite, f.WorkArrangement);
        Assert.Contains("SIEM / Splunk", f.Tech);
        Assert.Contains("RMF / NIST", f.Tech);
        Assert.Contains("eMASS", f.Tech);
    }

    [Fact]
    public void Data_science_series_is_remote_and_spans_senior_to_principal()
    {
        var f = Extract("CMS-26-1560-DS");
        Assert.Equal("Data Science & AI/ML", f.RoleFamily);
        Assert.Equal(WorkArrangement.Remote, f.WorkArrangement);
        Assert.Equal(Clearance.PublicTrust, f.Clearance);
        Assert.Equal(new[] { Seniority.Senior, Seniority.Lead, Seniority.Principal }, f.Seniority);
        Assert.Equal("Senior Data Scientist", f.WorkingTitle);
        Assert.Contains("R", f.Tech);
        Assert.Contains("Databricks", f.Tech);
    }

    [Fact]
    public void Help_desk_posting_is_entry_level_it_support()
    {
        var f = Extract("SSA-26-HD-09");
        Assert.Equal("IT Support", f.RoleFamily);
        Assert.Equal(new[] { Seniority.Entry, Seniority.Junior }, f.Seniority);
        Assert.Contains("ServiceNow", f.Tech);
    }

    [Fact]
    public void Supervisory_direct_hire_policy_role()
    {
        var f = Extract("DOI-26-OCIO-15");
        Assert.Equal("IT Policy & Governance", f.RoleFamily);
        Assert.True(f.Supervisory);
        Assert.True(f.DirectHire);
        Assert.Equal("Supervisory IT Policy Analyst", f.WorkingTitle);
    }

    [Fact]
    public void Common_english_words_do_not_become_tech_tags()
    {
        // The summary says "react to changing priorities", "the rest of the lab" and "the Spring semester".
        var f = Extract("NIST-26-1550-DB");
        Assert.DoesNotContain("React", f.Tech);
        Assert.DoesNotContain("REST APIs", f.Tech);
        Assert.DoesNotContain("Spring", f.Tech);
        Assert.Contains("Go", f.Tech);
        Assert.Contains("PyTorch", f.Tech);
        Assert.Equal("Data Science & AI/ML", f.RoleFamily);
        // ZP is not a GS pay plan, so seniority comes from the salary band.
        Assert.Equal(new[] { Seniority.Mid, Seniority.Senior, Seniority.Lead, Seniority.Principal }, f.Seniority);
    }

    [Theory]
    [InlineData("This role serves as a Lead Cloud Engineer for the platform team.", "Lead Cloud Engineer")]
    [InlineData("You will serve as an IT Specialist for the agency.", null)]
    [InlineData("Serves as the Product Owner of the benefits portal.", "Product Owner")]
    [InlineData("Serves as a member of the team.", null)]
    public void Working_title_is_read_from_serve_as_sentences(string text, string? expected) =>
        Assert.Equal(expected, RuleExtractor.FindServeAsTitle(text));

    [Fact]
    public async Task Llm_answers_are_only_accepted_when_they_fit_the_taxonomy()
    {
        var raw = data.Jobs["DISA-26-0042"];
        var llm = new FakeLlm(new LlmClassification("Senior Platform Engineer", "devops & cloud", ["Docker", "Made Up Tool"]));
        var enricher = new JobEnricher(data.Taxonomy, new RuleExtractor(data.Taxonomy), llm, LlmMode.LowConfidence);

        var f = await enricher.EnrichAsync(raw, CancellationToken.None);

        Assert.Equal("DevOps & Cloud", f.RoleFamily);
        Assert.Equal("Senior Platform Engineer", f.WorkingTitle);
        Assert.Equal(WorkingTitleSource.Llm, f.WorkingTitleSource);
        Assert.Contains("Docker", f.Tech);
        Assert.DoesNotContain("Made Up Tool", f.Tech);
    }

    [Fact]
    public void Llm_json_inside_a_markdown_fence_still_parses()
    {
        var parsed = OpenAiCompatibleClassifier.Parse("```json\n{\"workingTitle\":\"Data Engineer\",\"roleFamily\":\"Data Engineering & Analytics\",\"tech\":[\"Python\"]}\n```");
        Assert.NotNull(parsed);
        Assert.Equal("Data Engineer", parsed!.WorkingTitle);
        Assert.Equal(new[] { "Python" }, parsed.Tech);
    }

    private sealed class FakeLlm(LlmClassification answer) : ILlmClassifier
    {
        public Task<LlmClassification?> ClassifyAsync(RawJob job, EnrichedFields ruleFields, CompiledTaxonomy taxonomy, CancellationToken cancellationToken)
            => Task.FromResult<LlmClassification?>(answer);
    }
}
