using System.Text.Json.Serialization;

namespace Dataverse_Devops_MCP.Models.Planning;

public class AzureDevOpsProjectResult
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; } = string.Empty;

    [JsonPropertyName("iterationsCreated")]
    public int IterationsCreated { get; set; }

    [JsonPropertyName("workItemsCreated")]
    public int WorkItemsCreated { get; set; }
}

public class DocumentToAdoResult
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    [JsonPropertyName("tasksFound")]
    public int TasksFound { get; set; }

    [JsonPropertyName("sprintsCreated")]
    public int SprintsCreated { get; set; }

    [JsonPropertyName("workItemsCreated")]
    public int WorkItemsCreated { get; set; }

    [JsonPropertyName("totalEstimatedHours")]
    public double TotalEstimatedHours { get; set; }

    [JsonPropertyName("schedule")]
    public ProjectSchedule? Schedule { get; set; }
}
