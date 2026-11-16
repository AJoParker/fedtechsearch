namespace FedTechSearch.Core.Model;

/// <summary>One duty location of a job, with coordinates, for radius search.</summary>
public sealed class JobLocation
{
    public long Id { get; set; }
    public string JobId { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Country { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
