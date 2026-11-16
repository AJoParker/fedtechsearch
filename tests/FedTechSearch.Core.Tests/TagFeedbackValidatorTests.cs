using FedTechSearch.Core.Feedback;
using FedTechSearch.Core.Model;
using FedTechSearch.Core.Taxonomy;

namespace FedTechSearch.Core.Tests;

public sealed class TagFeedbackValidatorTests
{
    private static readonly CompiledTaxonomy Taxonomy = CompiledTaxonomy.LoadEmbedded();

    private static readonly Job Job = new()
    {
        Id = "1", RoleFamily = "Software Engineering", WorkingTitle = "Full Stack Software Developer",
        Tech = ["Python", "React"], Seniority = ["Mid", "Senior"], Clearance = "Secret", WorkArrangement = "On-site"
    };

    private static FeedbackResult Check(string? field, string? current, string? suggested = null, string? note = null) =>
        TagFeedbackValidator.Validate(Job, new FeedbackInput(field, current, suggested, note), Taxonomy);

    [Theory]
    [InlineData("salary")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_fields_are_rejected(string? field) => Assert.False(Check(field, "Python").Ok);

    [Fact]
    public void Current_value_must_be_shown_on_the_job()
    {
        Assert.False(Check("tech", "Kubernetes").Ok);
        Assert.False(Check("roleFamily", "Cybersecurity").Ok);
        Assert.True(Check("tech", "react").Ok); // case-insensitive, stored as shown
        Assert.Equal("React", Check("tech", "react").Feedback!.CurrentValue);
    }

    [Fact]
    public void Suggestions_must_be_known_and_come_back_canonical()
    {
        Assert.False(Check("tech", "Python", "Fortran 77 Deluxe").Ok);
        Assert.False(Check("roleFamily", "Software Engineering", "Wizardry").Ok);
        Assert.Equal("Kubernetes", Check("tech", null, "kubernetes").Feedback!.SuggestedValue);
        Assert.Equal("DevOps & Cloud", Check("roleFamily", "Software Engineering", "devops & cloud").Feedback!.SuggestedValue);
        Assert.Equal("Top Secret", Check("clearance", "Secret", "top secret").Feedback!.SuggestedValue);
    }

    [Fact]
    public void Missing_tech_is_allowed_only_for_tech_and_must_name_one()
    {
        Assert.True(Check("tech", null, "Kubernetes").Ok);
        Assert.False(Check("tech", null).Ok);                  // nothing named
        Assert.False(Check("tech", null, "Python").Ok);        // already tagged
        Assert.False(Check("roleFamily", null, "DevOps & Cloud").Ok);
    }

    [Fact]
    public void A_plain_remove_report_needs_no_suggestion()
    {
        var result = Check("tech", "React", note: "\"react to changing priorities\" isn't React");
        Assert.True(result.Ok);
        Assert.Null(result.Feedback!.SuggestedValue);
    }

    [Fact]
    public void Suggesting_the_current_value_is_rejected() => Assert.False(Check("seniority", "Mid", "mid").Ok);

    [Fact]
    public void Lengths_are_limited_and_text_is_cleaned()
    {
        Assert.False(Check("tech", "Python", note: new string('x', 501)).Ok);
        Assert.True(Check("tech", "Python", note: new string('x', 500)).Ok);
        Assert.False(Check("workingTitle", "Full Stack Software Developer", new string('t', 81)).Ok);

        var title = Check("workingTitle", "Full Stack Software Developer", "  Data\u0000   Engineer ​ ").Feedback!;
        Assert.Equal("Data Engineer", title.SuggestedValue);

        var note = Check("tech", "Python", note: "line one\r\nline two\u0007").Feedback!;
        Assert.Equal("line one\nline two", note.Note);
    }
}
