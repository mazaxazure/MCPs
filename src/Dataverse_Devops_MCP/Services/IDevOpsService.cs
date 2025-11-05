using Dataverse_Devops_MCP.Models.Planning;

namespace Dataverse_Devops_MCP.Services;

public interface IDevOpsService
{
    /// <summary>
    /// Creates iterations in Azure DevOps based on schedule
    /// </summary>
    Task<List<string>> CreateIterationsAsync(List<IterationDef> iterations);

    /// <summary>
    /// Creates a work item in Azure DevOps
    /// </summary>
    Task<int> CreateWorkItemAsync(string title, string? description, string workItemType, int priority, double? estimatedHours, List<string>? tags);

    /// <summary>
    /// Updates an existing work item
    /// </summary>
    Task UpdateWorkItemAsync(int workItemId, Dictionary<string, object?> fields);

    /// <summary>
    /// Deletes a work item
    /// </summary>
    Task DeleteWorkItemAsync(int workItemId);

    /// <summary>
    /// Gets a work item by ID
    /// </summary>
    Task<Dictionary<string, object?>> GetWorkItemAsync(int workItemId);

    /// <summary>
    /// Lists work items with optional filtering
    /// </summary>
    Task<List<Dictionary<string, object?>>> ListWorkItemsAsync(string? wiql = null, int? maxResults = null);

    /// <summary>
    /// Creates work items for all tasks in a plan and assigns them to iterations
    /// </summary>
    Task<AzureDevOpsProjectResult> CreateProjectFromPlanAsync(TaskPlan plan, ProjectSchedule schedule, string digest, string? teamName = null);
}
