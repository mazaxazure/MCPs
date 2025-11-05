using System.Text.Json.Serialization;

namespace Dataverse_Devops_MCP.Models.Planning;

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
