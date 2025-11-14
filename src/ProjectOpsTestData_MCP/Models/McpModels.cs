using System.Text.Json.Serialization;

namespace ProjectOpsTestData_MCP.Models;

public class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; set; }
}

public class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

public class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public class ToolInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; set; } = new { };
}

public class ToolResult
{
    [JsonPropertyName("content")]
    public List<ContentItem> Content { get; set; } = new();
}

public class ContentItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

// Project Operations Test Data Generation Models
public class TestDataGenerationOptions
{
    public int AccountCount { get; set; } = 20;
    public int ResourceCount { get; set; } = 15;
    public int ProjectCount { get; set; } = 10;
    public int TasksPerProject { get; set; } = 5;
}

public class TestDataStatistics
{
    [JsonPropertyName("accounts")]
    public int Accounts { get; set; }

    [JsonPropertyName("resources")]
    public int Resources { get; set; }

    [JsonPropertyName("projects")]
    public int Projects { get; set; }

    [JsonPropertyName("projectTasks")]
    public int ProjectTasks { get; set; }

    [JsonPropertyName("totalEntities")]
    public int TotalEntities => Accounts + Resources + Projects + ProjectTasks;

    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

public class ProjectTestData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("customerId")]
    public Guid? CustomerId { get; set; }

    [JsonPropertyName("projectManagerId")]
    public Guid? ProjectManagerId { get; set; }

    [JsonPropertyName("scheduledStart")]
    public DateTime? ScheduledStart { get; set; }

    [JsonPropertyName("scheduledEnd")]
    public DateTime? ScheduledEnd { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "EUR";

    [JsonPropertyName("tasks")]
    public List<TaskTestData> Tasks { get; set; } = new();
}

public class TaskTestData
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("effort")]
    public double? Effort { get; set; }

    [JsonPropertyName("scheduledStart")]
    public DateTime? ScheduledStart { get; set; }

    [JsonPropertyName("scheduledEnd")]
    public DateTime? ScheduledEnd { get; set; }

    [JsonPropertyName("assignedResourceId")]
    public Guid? AssignedResourceId { get; set; }

    [JsonPropertyName("progressPercent")]
    public int ProgressPercent { get; set; } = 0;
}

public class ResourceTestData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("calendarId")]
    public string? CalendarId { get; set; }

    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; } = "User"; // User, Equipment, Facility

    [JsonPropertyName("accountId")]
    public Guid? AccountId { get; set; }

    [JsonPropertyName("userId")]
    public Guid? UserId { get; set; }

    [JsonPropertyName("costPrice")]
    public decimal? CostPrice { get; set; }

    [JsonPropertyName("salesPrice")]
    public decimal? SalesPrice { get; set; }
}

public class AccountTestData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("primaryContact")]
    public string? PrimaryContact { get; set; }

    [JsonPropertyName("emailAddress")]
    public string? EmailAddress { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("websiteUrl")]
    public string? WebsiteUrl { get; set; }

    [JsonPropertyName("industry")]
    public string? Industry { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("revenue")]
    public decimal? Revenue { get; set; }

    [JsonPropertyName("numberOfEmployees")]
    public int? NumberOfEmployees { get; set; }
}