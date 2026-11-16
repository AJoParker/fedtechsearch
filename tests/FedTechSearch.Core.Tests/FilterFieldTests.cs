using FedTechSearch.Core.Enrichment;

namespace FedTechSearch.Core.Tests;

public sealed class FilterFieldTests(FixtureData data) : IClassFixture<FixtureData>
{
    private EnrichedFields Extract(RawJob job) => new RuleExtractor(data.Taxonomy).Extract(job);

    [Theory]
    [InlineData("public", "Open to the public")]
    [InlineData("VET", "Veterans")]
    [InlineData("disability", "Individuals with disabilities (Schedule A)")]
    [InlineData("fed-competitive", HiringPath.FederalEmployees)]
    [InlineData("fed-transition", HiringPath.FederalEmployees)]
    [InlineData("brand-new-code", "brand-new-code")]
    public void Hiring_path_codes_get_plain_labels(string code, string label) =>
        Assert.Equal(label, HiringPath.LabelFor(code));

    [Fact]
    public void Federal_employee_codes_share_one_label_both_ways()
    {
        Assert.Equal(["fed-competitive", "fed-excepted", "fed-internal-search", "fed-transition"],
            HiringPath.CodesFor(HiringPath.FederalEmployees));
        Assert.Equal([HiringPath.FederalEmployees], HiringPath.LabelsFor(["fed-competitive", "fed-excepted"]));
    }

    [Theory]
    [InlineData("1", ScheduleType.FullTime)]
    [InlineData("2", ScheduleType.PartTime)]
    [InlineData("6", ScheduleType.Multiple)]
    [InlineData("", null)]
    [InlineData("99", null)]
    public void Schedule_codes_map_to_labels(string code, string? label) => Assert.Equal(label, ScheduleType.FromCode(code));

    [Theory]
    [InlineData("15317", AppointmentType.Permanent)]
    [InlineData("15319", AppointmentType.Term)]
    [InlineData("15322", AppointmentType.Temporary)]
    [InlineData("15328", AppointmentType.Internship)]
    [InlineData("15668", AppointmentType.Other)]
    [InlineData(null, null)]
    public void Appointment_codes_are_grouped(string? code, string? label) => Assert.Equal(label, AppointmentType.FromCode(code));

    [Theory]
    [InlineData("0", TravelRequirement.None)]
    [InlineData("2", TravelRequirement.UpTo25)]
    [InlineData("5", TravelRequirement.UpTo50)]
    [InlineData("8", TravelRequirement.Over75)]
    public void Travel_codes_map_to_labels(string code, string label) => Assert.Equal(label, TravelRequirement.FromCode(code));

    [Theory]
    [InlineData("Initial consideration is limited to the first 100 applicants.")]
    [InlineData("This announcement may close early if enough applications are received.")]
    [InlineData("The announcement will close once we receive 150 applications.")]
    [InlineData("An application cap of 200 applies.")]
    public void Applicant_caps_mean_may_close_early(string howToApply)
    {
        var job = data.Jobs["DOI-26-OCIO-15"];
        Assert.True(Extract(Copy(job, howToApply)).MayCloseEarly);
    }

    [Theory]
    [InlineData("Applicants must meet all requirements by the cut-off/closing date of the announcement.")]
    [InlineData("Applications received after the initial cut-off date will be considered only if needed.")]
    [InlineData("Submit your application before the closing date.")]
    public void Routine_closing_date_wording_is_not_may_close_early(string howToApply)
    {
        var job = data.Jobs["DOI-26-OCIO-15"];
        Assert.False(Extract(Copy(job, howToApply)).MayCloseEarly);
    }

    [Fact]
    public void Mapper_reads_codes_promotion_potential_and_how_to_apply()
    {
        var ssa = data.Jobs["SSA-26-HD-09"];
        Assert.Equal(ScheduleType.FullTime, ssa.ScheduleType);
        Assert.Equal(AppointmentType.Permanent, ssa.AppointmentType);
        Assert.Equal(TravelRequirement.None, ssa.TravelRequirement);
        Assert.Equal(9, ssa.PromotionPotential);
        Assert.Contains("first 100 applicants", ssa.HowToApply);
        Assert.True(Extract(ssa).MayCloseEarly);
        Assert.False(Extract(data.Jobs["DISA-26-0042"]).MayCloseEarly);
    }

    private static RawJob Copy(RawJob job, string howToApply) => new()
    {
        Id = job.Id, Title = job.Title, Summary = job.Summary, Duties = job.Duties, Qualifications = job.Qualifications,
        PayPlan = job.PayPlan, LowGrade = job.LowGrade, HighGrade = job.HighGrade, HiringPaths = job.HiringPaths,
        HowToApply = howToApply
    };
}
