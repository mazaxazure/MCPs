using System.Text.Json.Serialization;

namespace Planning_MCP.Models.Planning;

public class ParsedTask
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("desc")]
    public string? Description { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 3; // Default P3

    [JsonPropertyName("estimateHours")]
    public double EstimateHours { get; set; } = 6.0; // Default 6h

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("dependsOn")]
    public List<string> DependsOn { get; set; } = new();
}

public class DocumentParseResult
{
    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    [JsonPropertyName("tasks")]
    public List<ParsedTask> Tasks { get; set; } = new();

    [JsonPropertyName("rawText")]
    public string RawText { get; set; } = string.Empty;
}

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

public class IterationDef
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty; // yyyy-MM-dd

    [JsonPropertyName("finish")]
    public string Finish { get; set; } = string.Empty; // yyyy-MM-dd

    [JsonPropertyName("items")]
    public List<TaskItem> Items { get; set; } = new();

    [JsonPropertyName("loadHours")]
    public double LoadHours { get; set; }
}

public class TaskItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("estimateHours")]
    public double EstimateHours { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("dependsOn")]
    public List<string> DependsOn { get; set; } = new();
}

public class TaskPlan
{
    [JsonPropertyName("iterations")]
    public List<IterationDef> Iterations { get; set; } = new();

    [JsonPropertyName("unplanned")]
    public List<TaskItem> Unplanned { get; set; } = new();

    [JsonPropertyName("summary")]
    public PlanSummary Summary { get; set; } = new();
}

public class PlanSummary
{
    [JsonPropertyName("items")]
    public int Items { get; set; }

    [JsonPropertyName("hours")]
    public double Hours { get; set; }
}
