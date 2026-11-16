using FedTechSearch.Core.Qualifications;

namespace FedTechSearch.Core.Tests;

/// <summary>Fixtures are verbatim qualification texts from live USAJOBS postings.</summary>
public sealed class QualificationParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "qualifications", name + ".txt"));

    private static ParseResult Parse(string name, int low, int high) =>
        QualificationParser.Parse(Fixture(name), null, null, "GS", low, high);

    [Fact]
    public void Spec_example_gs11_data_management_extracts_exact_values()
    {
        var (info, rejected) = Parse("dha-2210-11", 11, 11);

        Assert.True(info.Parsed);
        Assert.Empty(rejected);
        Assert.Equal("US Citizens", info.WhoMayApply);
        var block = Assert.Single(info.Blocks);
        Assert.Equal("GS-11", block.Grade);
        Assert.Equal("GS-09", block.EquivalentGrade);
        Assert.Equal("One year of specialized experience", block.ExperienceLabel);
        Assert.Equal(["Coordinating data management activities", "Assisting with analyzing data requirements", "Developing reports"],
            block.ExperienceDuties);

        var education = block.Education!;
        Assert.StartsWith("Ph.D. or equivalent doctoral degree or 3 full years of progressively higher level graduate education", education.Requirement);
        Assert.Equal(["computer science", "engineering", "information science", "information systems management",
            "mathematics", "operations research", "statistics", "technology management"], education.Fields);
        Assert.StartsWith("Three full years of graduate education", Assert.Single(education.Alternatives));

        Assert.Equal(["Attention to detail", "Customer service", "Oral communication", "Problem solving"], info.StandardCompetencies);
        Assert.Contains(info.Notes, n => n.Contains("volunteer experience"));
        Assert.Contains(info.Notes, n => n.Contains("transcripts will be required"));
    }

    [Fact]
    public void Two_grades_give_two_blocks_with_their_own_duties()
    {
        var (info, _) = Parse("two-grades", 14, 15);

        Assert.Equal(["GS-14", "GS-15"], info.Blocks.Select(b => b.Grade));
        Assert.Equal(["GS-13", "GS-14"], info.Blocks.Select(b => b.EquivalentGrade));
        Assert.Equal(6, info.Blocks[0].ExperienceDuties.Count);
        Assert.StartsWith("Advanced proficiency in developing complex WebFOCUS reports", info.Blocks[0].ExperienceDuties[0]);
        Assert.Equal(4, info.Blocks[1].ExperienceDuties.Count);
        Assert.StartsWith("Leading the strategic development", info.Blocks[1].ExperienceDuties[0]);
    }

    [Fact]
    public void Numbered_lists_split_per_number_and_items_stay_whole()
    {
        var (info, _) = Parse("numbered-list", 12, 12);

        var duties = Assert.Single(info.Blocks).ExperienceDuties;
        Assert.Equal(5, duties.Count);
        Assert.Equal("Performing operational system configuration audits, identifying discrepancies, modifying and documenting system drawings accordingly", duties[0]);
        Assert.Equal("Assisting in troubleshooting circuit outages", duties[3]); // trailing "and" before "5)" trimmed
        Assert.Equal("GS-11", info.Blocks[0].EquivalentGrade);
    }

    [Fact]
    public void Duties_right_after_the_phrase_are_found_and_and_lists_split()
    {
        var (network, _) = Parse("network-gerund", 12, 12);
        var block = Assert.Single(network.Blocks);
        Assert.Equal(["Preparing information management/technology strategic plans", "Conducting customer needs assessments"], block.ExperienceDuties);
        Assert.Equal("US Citizens", network.WhoMayApply); // "Who May Apply: US Citizens Basic Requirements for …"
        Assert.Equal("GS-11", block.EquivalentGrade);

        var (equivalent, _) = Parse("or-equivalent-gerund", 14, 15);
        Assert.Equal(["GS-14", "GS-15"], equivalent.Blocks.Select(b => b.Grade));
        Assert.Equal("Designing, developing, and maintaining mainframe applications using COBOL, CICS, VSAM, and JCL",
            equivalent.Blocks[0].ExperienceDuties[0]);
        Assert.Equal(3, equivalent.Blocks[0].ExperienceDuties.Count);
    }

    [Fact]
    public void Ses_postings_get_technical_and_executive_core_qualifications()
    {
        var (info, rejected) = QualificationParser.Parse(Fixture("ses-tq-ecq"), null, null, "ES", null, null);

        Assert.True(info.Parsed);
        Assert.Empty(rejected);
        Assert.Empty(info.Blocks);
        Assert.Equal(["TQ 1", "TQ 2"], info.TechnicalQualifications.Select(t => t.Label));
        Assert.Equal("Enterprise Customer Experience and Integrated Service Delivery", info.TechnicalQualifications[0].Title);
        Assert.StartsWith("Demonstrated executive experience leading an organization-wide customer experience program(s)", info.TechnicalQualifications[0].Text);
        Assert.Equal(["Commitment to the rule of law", "Driving efficiency", "Merit and competence", "Leading people", "Achieving results"],
            info.ExecutiveCoreQualifications);
        Assert.Contains(info.Notes, n => n.Contains("do not need to respond to the ECQs"));
        Assert.Contains(info.Notes, n => n.Contains("2-page limit"));
    }

    [Fact]
    public void Senior_technical_postings_have_tqs_without_titles_or_ecqs()
    {
        var (info, _) = QualificationParser.Parse(Fixture("st-tq-only"), null, null, "ST", null, null);

        Assert.True(info.Parsed);
        Assert.Equal(2, info.TechnicalQualifications.Count);
        Assert.All(info.TechnicalQualifications, t => Assert.Null(t.Title));
        Assert.StartsWith("Experience developing, integrating, and transitioning theater-level undersea warfare", info.TechnicalQualifications[0].Text);
        Assert.Empty(info.ExecutiveCoreQualifications);
    }

    [Fact]
    public void Demonstrated_by_lists_with_dotted_numbers_run_to_the_last_item()
    {
        var (info, rejected) = Parse("ces-demonstrated-by", 11, 11);

        Assert.Empty(rejected);
        var block = Assert.Single(info.Blocks);
        Assert.Equal([
            "Developing procurement documents and interprets design criteria for Military Construction (MCA) and installation projects based on customer requirements",
            "Preparing Performance Work Schedules, cost estimates, and documentation for inside/outside plant acquisitions",
            "Acting as Contracting Officer's Representative overseeing IT project integration, facilities inspection, and cybersecurity compliance per the IT Portfolio",
            "Coordinating Telecom projects to ensure standard telecommunication installation follows policies and guidelines",
        ], block.ExperienceDuties);
        Assert.NotNull(block.Education);
        Assert.Equal(4, info.StandardCompetencies.Count);
        Assert.Null(info.WhoMayApply); // "Who May Apply: Only applicants who meet…" names no audience
    }

    [Theory]
    [InlineData("Specialized experience would be demonstrated by: 1 - Installing network cabling for sites; 2 - Testing circuits and links.")]
    [InlineData("Specialized experience would be demonstrated by (1) Installing network cabling for sites; (2) Testing circuits and links.")]
    public void Other_numbering_styles_split_the_same_way(string text)
    {
        var (info, _) = QualificationParser.Parse(text, null, null, "GG", 11, 11);
        Assert.Equal(["Installing network cabling for sites", "Testing circuits and links"], Assert.Single(info.Blocks).ExperienceDuties);
    }

    [Fact]
    public void Comma_lists_whose_pieces_are_not_whole_phrases_are_not_split()
    {
        const string text = "Specialized experience must include: Designing, developing, and maintaining Health IT interoperability solutions, platforms, and standards, including data integration, analysis, and testing.";
        var (info, _) = QualificationParser.Parse(text, null, null, "GS", 13, 13);

        Assert.Equal(["Designing, developing, and maintaining Health IT interoperability solutions, platforms, and standards, including data integration, analysis, and testing"],
            Assert.Single(info.Blocks).ExperienceDuties);
    }

    [Fact]
    public void Postings_without_the_template_are_not_parsed_and_keep_their_text()
    {
        var original = Fixture("no-template");
        var (info, _) = QualificationParser.Parse(original, null, null, "GS", 12, 13);

        Assert.False(info.Parsed);
        Assert.Empty(info.Blocks);
        Assert.Equal(original, Fixture("no-template")); // parser never rewrites its input
    }

    [Fact]
    public void Education_from_the_details_field_is_checked_against_that_field()
    {
        const string quals = "Specialized experience includes coordinating data management activities.";
        const string education = "A degree from an accredited institution in computer science, engineering, mathematics, or statistics.";
        var (info, rejected) = QualificationParser.Parse(quals, education, null, "GS", 11, 11);

        Assert.Empty(rejected);
        Assert.Equal(["computer science", "engineering", "mathematics", "statistics"], Assert.Single(info.Blocks).Education!.Fields);
    }

    [Fact]
    public void Verbatim_gate_rejects_fabricated_and_reworded_items()
    {
        var source = Verbatim.Normalize(Fixture("dha-2210-11"));

        Assert.True(Verbatim.Contains(source, "Coordinating data management activities"));
        Assert.True(Verbatim.Contains(source, "COORDINATING   data management\nactivities")); // case and spacing are forgiven
        Assert.False(Verbatim.Contains(source, "Managing cloud data platforms"));            // fabricated
        Assert.False(Verbatim.Contains(source, "Coordinating data governance activities"));  // one word changed
        Assert.False(Verbatim.Contains(source, ""));
    }

    [Fact]
    public void Tidy_only_trims_and_capitalizes()
    {
        Assert.Equal("Developing reports", Verbatim.Tidy("  and developing reports. "));
        Assert.Equal("Assisting in troubleshooting circuit outages", Verbatim.Tidy("assisting in troubleshooting circuit outages and"));
        Assert.Equal("Assisting customers with support", Verbatim.Tidy("Examples of qualifying experience may look like: Assisting customers with support"));
    }

    [Fact]
    public void Competency_boilerplate_is_found_despite_wording_drift()
    {
        // Headings renamed, definitions intact: two distinctive phrases are enough.
        const string text = "Detail orientation: Is thorough when performing work and conscientious about attending to detail. "
            + "Working with customers: Works with clients and customers to assess their needs. "
            + "Speaking: Expresses information to groups and makes clear and convincing oral presentations. "
            + "Analysis: Identifies problems; determines accuracy and relevance of information.";
        var (info, _) = QualificationParser.Parse(text, null, null, "GS", 11, 11);

        Assert.Equal(["Attention to detail", "Customer service", "Oral communication", "Problem solving"], info.StandardCompetencies);
        Assert.False(info.Parsed); // boilerplate alone is not a qualification card
    }
}
