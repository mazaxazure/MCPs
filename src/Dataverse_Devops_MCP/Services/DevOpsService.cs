using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dataverse_Devops_MCP.Models.Planning;

namespace Dataverse_Devops_MCP.Services;

public class DevOpsService : IDevOpsService
{
    private readonly ILogger<DevOpsService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _organizationUrl;
    private readonly string _projectName;
    private readonly string _personalAccessToken;
    private readonly JsonSerializerOptions _jsonOptions;

    public DevOpsService(ILogger<DevOpsService> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        // Read configuration - First try environment variables (for MCP server configuration)
        _organizationUrl = configuration["ADO_ORG_URL"] 
            ?? configuration["AzureDevOps:OrganizationUrl"] 
            ?? throw new InvalidOperationException("Azure DevOps Organization URL not configured. Set ADO_ORG_URL or AzureDevOps:OrganizationUrl");
        
        _projectName = configuration["ADO_PROJECT_NAME"] 
            ?? configuration["AzureDevOps:ProjectName"] 
            ?? throw new InvalidOperationException("Azure DevOps Project Name not configured. Set ADO_PROJECT_NAME or AzureDevOps:ProjectName");
        
        _personalAccessToken = configuration["ADO_PAT"] 
            ?? configuration["AzureDevOps:PersonalAccessToken"] 
            ?? throw new InvalidOperationException("Azure DevOps Personal Access Token not configured. Set ADO_PAT or AzureDevOps:PersonalAccessToken");

        // Configure HttpClient
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_organizationUrl)
        };
        
        // Set authentication header
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_personalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _logger.LogInformation($"DevOpsService initialized - Organization: {_organizationUrl}, Project: {_projectName}, BaseAddress: {_httpClient.BaseAddress}");
    }

    public async Task<List<string>> CreateIterationsAsync(List<IterationDef> iterations)
    {
        var createdPaths = new List<string>();

        foreach (var iteration in iterations)
        {
            try
            {
                var iterationPath = await CreateIterationAsync(iteration);
                createdPaths.Add(iterationPath);
                _logger.LogInformation($"Created iteration: {iterationPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create iteration: {iteration.Name}");
                throw;
            }
        }

        return createdPaths;
    }

    private async Task<string> CreateIterationAsync(IterationDef iteration)
    {
        var url = $"{_projectName}/_apis/work/teamsettings/iterations?api-version=7.1";
        
        var payload = new
        {
            name = iteration.Name,
            attributes = new
            {
                startDate = iteration.Start,
                finishDate = iteration.Finish
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create iteration {iteration.Name}: {response.StatusCode} - {errorContent}");
        }

        var result = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(result);
        var path = jsonDoc.RootElement.GetProperty("path").GetString() ?? iteration.Name;
        
        return path;
    }

    public async Task<int> CreateWorkItemAsync(string title, string? description, string workItemType, int priority, double? estimatedHours, List<string>? tags)
    {
        // Azure DevOps API requires the work item type to be prefixed with $
        // Use absolute URL to avoid any issues with BaseAddress configuration
        var url = $"{_organizationUrl}/{_projectName}/_apis/wit/workitems/${workItemType}?api-version=7.1";
        
        // Log complete URL
        _logger.LogInformation($"Creating work item at URL: {url}");

        var operations = new List<object>
        {
            new { op = "add", path = "/fields/System.Title", value = title },
            new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = priority }
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            operations.Add(new { op = "add", path = "/fields/System.Description", value = description });
        }

        if (estimatedHours.HasValue)
        {
            operations.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Scheduling.OriginalEstimate", value = estimatedHours.Value });
        }

        if (tags != null && tags.Count > 0)
        {
            var tagsString = string.Join("; ", tags);
            operations.Add(new { op = "add", path = "/fields/System.Tags", value = tagsString });
        }

        var content = new StringContent(JsonSerializer.Serialize(operations, _jsonOptions), Encoding.UTF8, "application/json-patch+json");
        
        // Create a new request with absolute URL to avoid BaseAddress issues
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_personalAccessToken}")));
        
        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create work item: {response.StatusCode} - {errorContent}");
        }

        var result = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(result);
        var workItemId = jsonDoc.RootElement.GetProperty("id").GetInt32();

        _logger.LogInformation($"Created work item {workItemId}: {title}");
        return workItemId;
    }

    public async Task UpdateWorkItemAsync(int workItemId, Dictionary<string, object?> fields)
    {
        var url = $"{_projectName}/_apis/wit/workitems/{workItemId}?api-version=7.1";

        var operations = fields.Select(kvp => new
        {
            op = "add",
            path = $"/fields/{kvp.Key}",
            value = kvp.Value
        }).ToList();

        var content = new StringContent(JsonSerializer.Serialize(operations, _jsonOptions), Encoding.UTF8, "application/json-patch+json");
        var response = await _httpClient.PatchAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to update work item {workItemId}: {response.StatusCode} - {errorContent}");
        }

        _logger.LogInformation($"Updated work item {workItemId}");
    }

    public async Task DeleteWorkItemAsync(int workItemId)
    {
        var url = $"{_projectName}/_apis/wit/workitems/{workItemId}?api-version=7.1";

        var response = await _httpClient.DeleteAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to delete work item {workItemId}: {response.StatusCode} - {errorContent}");
        }

        _logger.LogInformation($"Deleted work item {workItemId}");
    }

    public async Task<Dictionary<string, object?>> GetWorkItemAsync(int workItemId)
    {
        var url = $"{_projectName}/_apis/wit/workitems/{workItemId}?api-version=7.1";

        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to get work item {workItemId}: {response.StatusCode} - {errorContent}");
        }

        var result = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(result);
        
        var workItem = new Dictionary<string, object?>
        {
            ["id"] = jsonDoc.RootElement.GetProperty("id").GetInt32(),
            ["url"] = jsonDoc.RootElement.GetProperty("url").GetString()
        };

        if (jsonDoc.RootElement.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateObject())
            {
                workItem[field.Name] = field.Value.ToString();
            }
        }

        return workItem;
    }

    public async Task<List<Dictionary<string, object?>>> ListWorkItemsAsync(string? wiql = null, int? maxResults = null)
    {
        // Default WIQL query if none provided
        var query = wiql ?? $"SELECT [System.Id], [System.Title], [System.State], [System.AssignedTo] FROM WorkItems WHERE [System.TeamProject] = '{_projectName}' ORDER BY [System.ChangedDate] DESC";

        var url = $"{_projectName}/_apis/wit/wiql?api-version=7.1";
        
        var payload = new { query };
        var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to execute WIQL query: {response.StatusCode} - {errorContent}");
        }

        var result = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(result);

        var workItemIds = new List<int>();
        if (jsonDoc.RootElement.TryGetProperty("workItems", out var workItems))
        {
            foreach (var item in workItems.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                {
                    workItemIds.Add(idProp.GetInt32());
                    
                    if (maxResults.HasValue && workItemIds.Count >= maxResults.Value)
                        break;
                }
            }
        }

        // Fetch full details for each work item
        var workItemList = new List<Dictionary<string, object?>>();
        foreach (var id in workItemIds)
        {
            try
            {
                var workItem = await GetWorkItemAsync(id);
                workItemList.Add(workItem);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to fetch work item {id}");
            }
        }

        return workItemList;
    }

    public async Task<AzureDevOpsProjectResult> CreateProjectFromPlanAsync(TaskPlan plan, ProjectSchedule schedule, string digest, string? teamName = null)
    {
        _logger.LogInformation($"Creating Azure DevOps project from plan with {plan.Iterations.Count} iterations and {plan.Summary.Items} work items");

        // Step 1: Create iterations
        var iterationPaths = await CreateIterationsAsync(plan.Iterations);

        // Step 2: Create work items for each iteration
        var workItemsCreated = 0;
        var workItemMapping = new Dictionary<string, int>(); // title -> work item ID

        foreach (var iteration in plan.Iterations)
        {
            foreach (var task in iteration.Items)
            {
                try
                {
                    var workItemType = "Task"; // Could be configurable: Task, User Story, Bug, etc.
                    var workItemId = await CreateWorkItemAsync(
                        task.Title,
                        task.Description,
                        workItemType,
                        task.Priority,
                        task.EstimateHours,
                        task.Tags
                    );

                    workItemMapping[task.Title] = workItemId;

                    // Assign to iteration
                    await UpdateWorkItemAsync(workItemId, new Dictionary<string, object?>
                    {
                        ["System.IterationPath"] = $"{_projectName}\\{iteration.Name}"
                    });

                    workItemsCreated++;
                    _logger.LogInformation($"Created work item {workItemId} in iteration {iteration.Name}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to create work item: {task.Title}");
                }
            }
        }

        // Step 3: Handle dependencies (create links between work items)
        foreach (var iteration in plan.Iterations)
        {
            foreach (var task in iteration.Items)
            {
                if (task.DependsOn != null && task.DependsOn.Count > 0 && workItemMapping.ContainsKey(task.Title))
                {
                    var targetWorkItemId = workItemMapping[task.Title];

                    foreach (var dependency in task.DependsOn)
                    {
                        if (workItemMapping.TryGetValue(dependency, out var sourceWorkItemId))
                        {
                            try
                            {
                                await CreateWorkItemLinkAsync(targetWorkItemId, sourceWorkItemId, "System.LinkTypes.Dependency-Forward");
                                _logger.LogInformation($"Created dependency link: {targetWorkItemId} depends on {sourceWorkItemId}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, $"Failed to create dependency link for work item {targetWorkItemId}");
                            }
                        }
                    }
                }
            }
        }

        var projectUrl = $"{_organizationUrl}/{_projectName}";

        return new AzureDevOpsProjectResult
        {
            ProjectId = _projectName,
            ProjectName = _projectName,
            WebUrl = projectUrl,
            IterationsCreated = iterationPaths.Count,
            WorkItemsCreated = workItemsCreated
        };
    }

    private async Task CreateWorkItemLinkAsync(int sourceWorkItemId, int targetWorkItemId, string linkType)
    {
        var url = $"{_projectName}/_apis/wit/workitems/{sourceWorkItemId}?api-version=7.1";

        var operations = new List<object>
        {
            new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = linkType,
                    url = $"{_organizationUrl}/{_projectName}/_apis/wit/workItems/{targetWorkItemId}"
                }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(operations, _jsonOptions), Encoding.UTF8, "application/json-patch+json");
        var response = await _httpClient.PatchAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create work item link: {response.StatusCode} - {errorContent}");
        }
    }
}
