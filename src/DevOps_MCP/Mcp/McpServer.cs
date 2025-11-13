using System.Text.Json;
using Microsoft.Extensions.Logging;
using DevOps_MCP.Models;
using DevOps_MCP.Models.Planning;
using DevOps_MCP.Services;

namespace DevOps_MCP.Mcp;

public class McpServer
{
    private readonly ILogger<McpServer> _logger;
    private readonly IDevOpsService _devOpsService;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServer(
        ILogger<McpServer> logger,
        IDevOpsService devOpsService)
    {
        _logger = logger;
        _devOpsService = devOpsService;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting DevOps MCP Server");

        // Process requests from stdin
        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin);
        using var stdout = Console.OpenStandardOutput();
        using var writer = new StreamWriter(stdout) { AutoFlush = true };

        while (true)
        {
            try
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                _logger.LogDebug($"Received request: {line}");

                var request = JsonSerializer.Deserialize<McpRequest>(line, _jsonOptions);
                if (request != null)
                {
                    var response = await HandleRequestAsync(request);
                    
                    if (response != null)
                    {
                        var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                        _logger.LogDebug($"Sending response: {responseJson}");
                        await writer.WriteLineAsync(responseJson);
                        await writer.FlushAsync();
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON parsing error");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request");
            }
        }
    }

    private async Task<McpResponse?> HandleRequestAsync(McpRequest request)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "initialized" => HandleInitialized(request),
                "tools/list" => HandleToolsList(request),
                "tools/call" => await HandleToolCallAsync(request),
                _ => request.Id.HasValue 
                    ? CreateErrorResponse(request.Id.Value, -32601, $"Method not found: {request.Method}")
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling method: {request.Method}");
            return request.Id.HasValue 
                ? CreateErrorResponse(request.Id.Value, -32603, $"Internal error: {ex.Message}")
                : null;
        }
    }

    private McpResponse? HandleInitialized(McpRequest request)
    {
        _logger.LogInformation("Client initialization completed");
        return null;
    }

    private McpResponse HandleInitialize(McpRequest request)
    {
        _logger.LogInformation("Handling initialize request");
        
        if (!request.Id.HasValue)
        {
            throw new InvalidOperationException("Initialize request must have an ID");
        }
        
        return new McpResponse
        {
            Id = request.Id.Value,
            Result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new Dictionary<string, object>()
                },
                serverInfo = new
                {
                    name = "devops-mcp",
                    version = "1.0.0"
                }
            }
        };
    }

    private McpResponse HandleToolsList(McpRequest request)
    {
        _logger.LogInformation("Handling tools/list request");
        
        if (!request.Id.HasValue)
        {
            throw new InvalidOperationException("tools/list request must have an ID");
        }
        
        var tools = new List<ToolInfo>
        {
            new()
            {
                Name = "create_work_item",
                Description = "Creates a new work item in Azure DevOps",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["title"] = new
                        {
                            type = "string",
                            description = "The title of the work item"
                        },
                        ["description"] = new
                        {
                            type = "string",
                            description = "Optional: The description of the work item"
                        },
                        ["workItemType"] = new
                        {
                            type = "string",
                            description = "Optional: The work item type (Task, User Story, Bug, etc.). Defaults to 'Task'"
                        },
                        ["priority"] = new
                        {
                            type = "number",
                            description = "Optional: Priority (1-4). Defaults to 3"
                        },
                        ["estimatedHours"] = new
                        {
                            type = "number",
                            description = "Optional: Estimated hours to complete"
                        },
                        ["tags"] = new
                        {
                            type = "array",
                            description = "Optional: Array of tags",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "title" }
                }
            },
            new()
            {
                Name = "update_work_item",
                Description = "Updates an existing work item in Azure DevOps",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["workItemId"] = new
                        {
                            type = "number",
                            description = "The ID of the work item to update"
                        },
                        ["fields"] = new
                        {
                            type = "object",
                            description = "Dictionary of field names and values to update (e.g., 'System.Title', 'System.State', 'System.AssignedTo')"
                        }
                    },
                    required = new[] { "workItemId", "fields" }
                }
            },
            new()
            {
                Name = "delete_work_item",
                Description = "Deletes a work item from Azure DevOps",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["workItemId"] = new
                        {
                            type = "number",
                            description = "The ID of the work item to delete"
                        }
                    },
                    required = new[] { "workItemId" }
                }
            },
            new()
            {
                Name = "get_work_item",
                Description = "Gets a work item by ID from Azure DevOps",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["workItemId"] = new
                        {
                            type = "number",
                            description = "The ID of the work item to retrieve"
                        }
                    },
                    required = new[] { "workItemId" }
                }
            },
            new()
            {
                Name = "list_work_items",
                Description = "Lists work items from Azure DevOps using WIQL query",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["wiql"] = new
                        {
                            type = "string",
                            description = "Optional: WIQL (Work Item Query Language) query. If not provided, returns all work items in the project"
                        },
                        ["maxResults"] = new
                        {
                            type = "number",
                            description = "Optional: Maximum number of work items to return"
                        }
                    },
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "create_ado_project_from_tasks",
                Description = "Adds iterations and work items to an existing Azure DevOps project from a task plan. The project must already exist in Azure DevOps.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["plan"] = new
                        {
                            type = "object",
                            description = "The task plan with iterations"
                        },
                        ["schedule"] = new
                        {
                            type = "object",
                            description = "The project schedule"
                        },
                        ["digest"] = new
                        {
                            type = "string",
                            description = "The digest of the source document for idempotency"
                        },
                        ["teamName"] = new
                        {
                            type = "string",
                            description = "Optional: The team name (defaults to project name)"
                        }
                    },
                    required = new[] { "plan", "schedule", "digest" }
                }
            }
        };

        return new McpResponse
        {
            Id = request.Id.Value,
            Result = new { tools }
        };
    }

    private async Task<McpResponse> HandleToolCallAsync(McpRequest request)
    {
        if (!request.Id.HasValue)
        {
            throw new InvalidOperationException("tools/call request must have an ID");
        }
        
        if (request.Params == null || !request.Params.TryGetValue("name", out var toolNameObj))
        {
            return CreateErrorResponse(request.Id.Value, -32602, "Tool name is required");
        }

        var toolName = toolNameObj?.ToString() ?? string.Empty;
        var arguments = request.Params.TryGetValue("arguments", out var argsObj) 
            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsObj.ToString() ?? "{}")
            : new Dictionary<string, JsonElement>();

        try
        {
            var result = toolName switch
            {
                "create_work_item" => await HandleCreateWorkItemAsync(arguments),
                "update_work_item" => await HandleUpdateWorkItemAsync(arguments),
                "delete_work_item" => await HandleDeleteWorkItemAsync(arguments),
                "get_work_item" => await HandleGetWorkItemAsync(arguments),
                "list_work_items" => await HandleListWorkItemsAsync(arguments),
                "create_ado_project_from_tasks" => await HandleCreateAdoProjectFromTasksAsync(arguments),
                _ => throw new Exception($"Unknown tool: {toolName}")
            };

            return new McpResponse
            {
                Id = request.Id.Value,
                Result = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error executing tool: {toolName}");
            return CreateErrorResponse(request.Id.Value, -32603, $"Tool execution error: {ex.Message}");
        }
    }

    private async Task<ToolResult> HandleCreateWorkItemAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("title", out var titleElement))
        {
            throw new ArgumentException("title is required");
        }

        var title = titleElement.GetString() ?? throw new ArgumentException("title cannot be null");

        string? description = null;
        if (arguments.TryGetValue("description", out var descElement))
        {
            description = descElement.GetString();
        }

        var workItemType = "Task";
        if (arguments.TryGetValue("workItemType", out var typeElement))
        {
            workItemType = typeElement.GetString() ?? "Task";
        }

        var priority = 3;
        if (arguments.TryGetValue("priority", out var priorityElement))
        {
            if (priorityElement.TryGetInt32(out var priorityValue))
            {
                priority = priorityValue;
            }
        }

        double? estimatedHours = null;
        if (arguments.TryGetValue("estimatedHours", out var hoursElement))
        {
            if (hoursElement.TryGetDouble(out var hoursValue))
            {
                estimatedHours = hoursValue;
            }
        }

        List<string>? tags = null;
        if (arguments.TryGetValue("tags", out var tagsElement))
        {
            tags = JsonSerializer.Deserialize<List<string>>(tagsElement.GetRawText());
        }

        var workItemId = await _devOpsService.CreateWorkItemAsync(title, description, workItemType, priority, estimatedHours, tags);

        var result = new
        {
            workItemId = workItemId,
            title = title,
            message = "Work item created successfully"
        };

        var text = JsonSerializer.Serialize(result, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleUpdateWorkItemAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("workItemId", out var idElement))
        {
            throw new ArgumentException("workItemId is required");
        }

        if (!arguments.TryGetValue("fields", out var fieldsElement))
        {
            throw new ArgumentException("fields is required");
        }

        if (!idElement.TryGetInt32(out var workItemId))
        {
            throw new ArgumentException("workItemId must be a valid integer");
        }

        var fields = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldsElement.GetRawText()) 
            ?? throw new ArgumentException("fields cannot be null");

        await _devOpsService.UpdateWorkItemAsync(workItemId, fields);

        var result = new
        {
            workItemId = workItemId,
            message = "Work item updated successfully"
        };

        var text = JsonSerializer.Serialize(result, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleDeleteWorkItemAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("workItemId", out var idElement))
        {
            throw new ArgumentException("workItemId is required");
        }

        if (!idElement.TryGetInt32(out var workItemId))
        {
            throw new ArgumentException("workItemId must be a valid integer");
        }

        await _devOpsService.DeleteWorkItemAsync(workItemId);

        var result = new
        {
            workItemId = workItemId,
            message = "Work item deleted successfully"
        };

        var text = JsonSerializer.Serialize(result, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleGetWorkItemAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("workItemId", out var idElement))
        {
            throw new ArgumentException("workItemId is required");
        }

        if (!idElement.TryGetInt32(out var workItemId))
        {
            throw new ArgumentException("workItemId must be a valid integer");
        }

        var workItem = await _devOpsService.GetWorkItemAsync(workItemId);
        var text = JsonSerializer.Serialize(workItem, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleListWorkItemsAsync(Dictionary<string, JsonElement>? arguments)
    {
        string? wiql = null;
        if (arguments != null && arguments.TryGetValue("wiql", out var wiqlElement))
        {
            wiql = wiqlElement.GetString();
        }

        int? maxResults = null;
        if (arguments != null && arguments.TryGetValue("maxResults", out var maxResultsElement))
        {
            if (maxResultsElement.TryGetInt32(out var maxResultsValue))
            {
                maxResults = maxResultsValue;
            }
        }

        var workItems = await _devOpsService.ListWorkItemsAsync(wiql, maxResults);
        var text = JsonSerializer.Serialize(workItems, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleCreateAdoProjectFromTasksAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("plan", out var planElement))
        {
            throw new ArgumentException("plan is required");
        }

        if (!arguments.TryGetValue("schedule", out var scheduleElement))
        {
            throw new ArgumentException("schedule is required");
        }

        if (!arguments.TryGetValue("digest", out var digestElement))
        {
            throw new ArgumentException("digest is required");
        }

        var plan = JsonSerializer.Deserialize<TaskPlan>(planElement.GetRawText()) 
            ?? throw new ArgumentException("plan cannot be null");
        var schedule = JsonSerializer.Deserialize<ProjectSchedule>(scheduleElement.GetRawText()) 
            ?? throw new ArgumentException("schedule cannot be null");
        var digest = digestElement.GetString() ?? throw new ArgumentException("digest cannot be null");

        string? teamName = null;
        if (arguments.TryGetValue("teamName", out var teamNameElement))
        {
            teamName = teamNameElement.GetString();
        }

        var result = await _devOpsService.CreateProjectFromPlanAsync(plan, schedule, digest, teamName);
        var text = JsonSerializer.Serialize(result, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private McpResponse CreateErrorResponse(int id, int code, string message)
    {
        return new McpResponse
        {
            Id = id,
            Error = new McpError
            {
                Code = code,
                Message = message
            }
        };
    }
}
