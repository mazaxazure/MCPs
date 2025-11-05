using System.Text.Json.Serialization;

namespace Dataverse_Devops_MCP.Models.Planning;

public class ProjectSchedule
{
    [JsonPropertyName("startDate")]
    public string StartDate { get; set; } = string.Empty; // yyyy-MM-dd

    [JsonPropertyName("totalDurationDays")]
    public int TotalDurationDays { get; set; }

    [JsonPropertyName("sprintLengthDays")]
    public int SprintLengthDays { get; set; }

    [JsonPropertyName("sprintCount")]
    public int SprintCount { get; set; }

    [JsonPropertyName("capacityHoursPerSprint")]
    public double CapacityHoursPerSprint { get; set; }

    [JsonPropertyName("rationale")]
    public string Rationale { get; set; } = string.Empty;
}
