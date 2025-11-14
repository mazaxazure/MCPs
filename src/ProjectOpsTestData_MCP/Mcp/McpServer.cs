using System.Text.Json;
using Microsoft.Extensions.Logging;
using ProjectOpsTestData_MCP.Models;
using ProjectOpsTestData_MCP.Services;

namespace ProjectOpsTestData_MCP.Mcp;

public class McpServer
{
    private readonly ILogger<McpServer> _logger;
    private readonly IProjectOpsTestDataService _testDataService;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServer(
        ILogger<McpServer> logger,
        IProjectOpsTestDataService testDataService)
    {
        _logger = logger;
        _testDataService = testDataService;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting Project Operations Test Data MCP Server");

        // Connect to Dataverse
        var connected = await _testDataService.ConnectAsync();
        if (!connected)
        {
            _logger.LogError("Failed to connect to Dataverse");
            return;
        }

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
                    name = "project-ops-testdata-mcp",
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
            // Metadata tools (copied from working Dataverse_MCP)
            new()
            {
                Name = "mcp_dataverse_mcp_list_entities",
                Description = "Lists all entities in the Dataverse environment",
                InputSchema = new 
                { 
                    type = "object", 
                    properties = new Dictionary<string, object>(), 
                    required = new string[] { } 
                }
            },
            new()
            {
                Name = "mcp_dataverse_mcp_get_entity_metadata",
                Description = "Gets detailed metadata for a specific entity",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = new
                        {
                            type = "string",
                            description = "The logical name of the entity (e.g., 'account', 'contact', 'msdyn_project')"
                        }
                    },
                    required = new[] { "entityLogicalName" }
                }
            },
            new()
            {
                Name = "mcp_dataverse_mcp_get_entity_attributes",
                Description = "Gets all attributes for a specific entity",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = new
                        {
                            type = "string",
                            description = "The logical name of the entity"
                        }
                    },
                    required = new[] { "entityLogicalName" }
                }
            },
            new()
            {
                Name = "mcp_dataverse_mcp_create_record",
                Description = "Creates a new record in a Dataverse entity with proper type validation",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = new
                        {
                            type = "string",
                            description = "The logical name of the entity"
                        },
                        ["attributes"] = new
                        {
                            type = "object",
                            description = "Key-value pairs of attributes to set on the new record"
                        }
                    },
                    required = new[] { "entityLogicalName", "attributes" }
                }
            },
            new()
            {
                Name = "mcp_dataverse_mcp_get_record",
                Description = "Retrieves a specific record by ID",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = new
                        {
                            type = "string",
                            description = "The logical name of the entity"
                        },
                        ["id"] = new
                        {
                            type = "string",
                            description = "The GUID of the record"
                        },
                        ["columns"] = new
                        {
                            type = "array",
                            description = "Optional: Array of column names to retrieve",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "entityLogicalName", "id" }
                }
            },
            new()
            {
                Name = "mcp_dataverse_mcp_query_records",
                Description = "Queries multiple records with filtering",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = new
                        {
                            type = "string",
                            description = "The logical name of the entity"
                        },
                        ["filter"] = new
                        {
                            type = "string",
                            description = "Optional: Filter expression (e.g., 'name eq SampleAccount')"
                        },
                        ["columns"] = new
                        {
                            type = "array",
                            description = "Optional: Array of column names to retrieve",
                            items = new { type = "string" }
                        },
                        ["maxResults"] = new
                        {
                            type = "number",
                            description = "Optional: Maximum number of records to return"
                        }
                    },
                    required = new[] { "entityLogicalName" }
                }
            },
            new()
            {
                Name = "generate_accounts",
                Description = "Generate test account data for Project Operations",
                InputSchema = new 
                { 
                    type = "object", 
                    properties = new Dictionary<string, object>
                    {
                        ["count"] = new
                        {
                            type = "number",
                            description = "Number of accounts to generate (default: 20)",
                            minimum = 1,
                            maximum = 100
                        }
                    }, 
                    required = new string[] { } 
                }
            },
            new()
            {
                Name = "generate_resources",
                Description = "Generate test resource data for Project Operations",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["count"] = new
                        {
                            type = "number",
                            description = "Number of resources to generate (default: 15)",
                            minimum = 1,
                            maximum = 50
                        }
                    },
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "generate_projects",
                Description = "Generate test project data with tasks for Project Operations",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["projectCount"] = new
                        {
                            type = "number",
                            description = "Number of projects to generate (default: 10)",
                            minimum = 1,
                            maximum = 50
                        },
                        ["tasksPerProject"] = new
                        {
                            type = "number", 
                            description = "Number of tasks per project (default: 5)",
                            minimum = 1,
                            maximum = 20
                        }
                    },
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "create_accounts",
                Description = "Create test accounts in Dataverse",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["count"] = new
                        {
                            type = "number",
                            description = "Number of accounts to create (default: 20)",
                            minimum = 1,
                            maximum = 100
                        }
                    },
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "create_resources",
                Description = "Create test resources in Dataverse",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["count"] = new
                        {
                            type = "number",
                            description = "Number of resources to create (default: 15)",
                            minimum = 1,
                            maximum = 50
                        }
                    },
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "create_projects",
                Description = "Create test projects with tasks in Dataverse",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["projectCount"] = new
                        {
                            type = "number",
                            description = "Number of projects to create (default: 10)",
                            minimum = 1,
                            maximum = 50
                        },
                        ["tasksPerProject"] = new
                        {
                            type = "number",
                            description = "Number of tasks per project (default: 5)",
                            minimum = 1,
                            maximum = 20
                        }
                    },
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "create_project_tasks_dynamics365",
                Description = "Create project tasks using Dynamics 365 custom action (msdyn_PssCreateV1)",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["projectId"] = new
                        {
                            type = "string",
                            description = "GUID of the project to create tasks for"
                        },
                        ["taskCount"] = new
                        {
                            type = "number",
                            description = "Number of tasks to create (default: 5)",
                            minimum = 1,
                            maximum = 20
                        }
                    },
                    required = new[] { "projectId" }
                }
            },
            new()
            {
                Name = "create_complete_dataset",
                Description = "Create a complete test data set with accounts, resources, and projects",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["accountCount"] = new
                        {
                            type = "number",
                            description = "Number of accounts to create (default: 20)",
                            minimum = 1,
                            maximum = 100
                        },
                        ["resourceCount"] = new
                        {
                            type = "number",
                            description = "Number of resources to create (default: 15)",
                            minimum = 1,
                            maximum = 50
                        },
                        ["projectCount"] = new
                        {
                            type = "number",
                            description = "Number of projects to create (default: 10)",
                            minimum = 1,
                            maximum = 50
                        },
                        ["tasksPerProject"] = new
                        {
                            type = "number",
                            description = "Number of tasks per project (default: 5)",
                            minimum = 1,
                            maximum = 20
                        }
                    },
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "delete_all_test_data",
                Description = "Delete all test project and task data from Dataverse (preserves accounts and resources)",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "get_statistics",
                Description = "Get statistics about existing test data in Dataverse",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = new string[] { }
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
                "generate_accounts" => await HandleGenerateAccountsAsync(arguments),
                "generate_resources" => await HandleGenerateResourcesAsync(arguments),
                "generate_projects" => await HandleGenerateProjectsAsync(arguments),
                "create_accounts" => await HandleCreateAccountsAsync(arguments),
                "create_resources" => await HandleCreateResourcesAsync(arguments),
                "create_projects" => await HandleCreateProjectsAsync(arguments),
                "create_project_tasks_dynamics365" => await HandleCreateProjectTasksDynamics365Async(arguments),
                "create_complete_dataset" => await HandleCreateCompleteDatasetAsync(arguments),
                "delete_all_test_data" => await HandleDeleteAllTestDataAsync(),
                "get_statistics" => await HandleGetStatisticsAsync(),
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

    private async Task<ToolResult> HandleGenerateAccountsAsync(Dictionary<string, JsonElement>? arguments)
    {
        var count = 20;
        if (arguments?.TryGetValue("count", out var countElement) == true && countElement.TryGetInt32(out var countValue))
        {
            count = countValue;
        }

        var accounts = await _testDataService.GenerateAccountTestDataAsync(count);
        var text = JsonSerializer.Serialize(accounts, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = $"Generated {accounts.Count} test accounts:\\n{text}" }
            }
        };
    }

    private async Task<ToolResult> HandleGenerateResourcesAsync(Dictionary<string, JsonElement>? arguments)
    {
        var count = 15;
        if (arguments?.TryGetValue("count", out var countElement) == true && countElement.TryGetInt32(out var countValue))
        {
            count = countValue;
        }

        var resources = await _testDataService.GenerateResourceTestDataAsync(count);
        var text = JsonSerializer.Serialize(resources, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = $"Generated {resources.Count} test resources:\\n{text}" }
            }
        };
    }

    private async Task<ToolResult> HandleGenerateProjectsAsync(Dictionary<string, JsonElement>? arguments)
    {
        var options = new TestDataGenerationOptions();
        
        if (arguments?.TryGetValue("projectCount", out var projectCountElement) == true && projectCountElement.TryGetInt32(out var projectCount))
        {
            options.ProjectCount = projectCount;
        }

        if (arguments?.TryGetValue("tasksPerProject", out var tasksElement) == true && tasksElement.TryGetInt32(out var tasksPerProject))
        {
            options.TasksPerProject = tasksPerProject;
        }

        var projects = await _testDataService.GenerateProjectTestDataAsync(options);
        var text = JsonSerializer.Serialize(projects, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = $"Generated {projects.Count} test projects with {options.TasksPerProject} tasks each:\\n{text}" }
            }
        };
    }

    private async Task<ToolResult> HandleCreateAccountsAsync(Dictionary<string, JsonElement>? arguments)
    {
        var count = 20;
        if (arguments?.TryGetValue("count", out var countElement) == true && countElement.TryGetInt32(out var countValue))
        {
            count = countValue;
        }

        var accounts = await _testDataService.GenerateAccountTestDataAsync(count);
        var result = await _testDataService.CreateAccountsAsync(accounts);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = result }
            }
        };
    }

    private async Task<ToolResult> HandleCreateResourcesAsync(Dictionary<string, JsonElement>? arguments)
    {
        var count = 15;
        if (arguments?.TryGetValue("count", out var countElement) == true && countElement.TryGetInt32(out var countValue))
        {
            count = countValue;
        }

        var resources = await _testDataService.GenerateResourceTestDataAsync(count);
        var result = await _testDataService.CreateResourcesAsync(resources);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = result }
            }
        };
    }

    private async Task<ToolResult> HandleCreateProjectsAsync(Dictionary<string, JsonElement>? arguments)
    {
        var options = new TestDataGenerationOptions();
        
        if (arguments?.TryGetValue("projectCount", out var projectCountElement) == true && projectCountElement.TryGetInt32(out var projectCount))
        {
            options.ProjectCount = projectCount;
        }

        if (arguments?.TryGetValue("tasksPerProject", out var tasksElement) == true && tasksElement.TryGetInt32(out var tasksPerProject))
        {
            options.TasksPerProject = tasksPerProject;
        }

        var projects = await _testDataService.GenerateProjectTestDataAsync(options);
        var result = await _testDataService.CreateProjectsAsync(projects);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = result }
            }
        };
    }

    private async Task<ToolResult> HandleCreateProjectTasksDynamics365Async(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments?.TryGetValue("projectId", out var projectIdElement) != true || 
            !Guid.TryParse(projectIdElement.GetString(), out var projectId))
        {
            throw new ArgumentException("Valid projectId is required");
        }

        var taskCount = 5;
        if (arguments.TryGetValue("taskCount", out var taskCountElement) && taskCountElement.TryGetInt32(out var taskCountValue))
        {
            taskCount = taskCountValue;
        }

        // Generate test tasks for the project
        var options = new TestDataGenerationOptions { TasksPerProject = taskCount };
        var projects = await _testDataService.GenerateProjectTestDataAsync(options);
        var tasks = projects.FirstOrDefault()?.Tasks ?? new List<TaskTestData>();

        // Create tasks using Dynamics 365 custom action
        var (successCount, failedCount) = await _testDataService.CreateProjectTasksUsingDynamics365ActionAsync(projectId, tasks);

        var result = $"Created {successCount} tasks using Dynamics 365 custom action. Failed: {failedCount}";
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = result }
            }
        };
    }

    private async Task<ToolResult> HandleCreateCompleteDatasetAsync(Dictionary<string, JsonElement>? arguments)
    {
        var options = new TestDataGenerationOptions();
        
        if (arguments?.TryGetValue("accountCount", out var accountCountElement) == true && accountCountElement.TryGetInt32(out var accountCount))
        {
            options.AccountCount = accountCount;
        }

        if (arguments?.TryGetValue("resourceCount", out var resourceCountElement) == true && resourceCountElement.TryGetInt32(out var resourceCount))
        {
            options.ResourceCount = resourceCount;
        }

        if (arguments?.TryGetValue("projectCount", out var projectCountElement) == true && projectCountElement.TryGetInt32(out var projectCount))
        {
            options.ProjectCount = projectCount;
        }

        if (arguments?.TryGetValue("tasksPerProject", out var tasksElement) == true && tasksElement.TryGetInt32(out var tasksPerProject))
        {
            options.TasksPerProject = tasksPerProject;
        }

        var result = await _testDataService.CreateCompleteTestDataSetAsync(options);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = result }
            }
        };
    }

    private async Task<ToolResult> HandleDeleteAllTestDataAsync()
    {
        var result = await _testDataService.DeleteAllTestDataAsync();
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = result }
            }
        };
    }

    private async Task<ToolResult> HandleGetStatisticsAsync()
    {
        var stats = await _testDataService.GetTestDataStatisticsAsync();
        var text = JsonSerializer.Serialize(stats, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = $"Test Data Statistics:\\n{text}" }
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