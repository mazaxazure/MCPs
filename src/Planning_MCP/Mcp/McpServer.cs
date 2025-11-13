using System.Text.Json;
using Microsoft.Extensions.Logging;
using Planning_MCP.Models;
using Planning_MCP.Models.Planning;
using Planning_MCP.Services.Planning;

namespace Planning_MCP.Mcp;

public class McpServer
{
    private readonly ILogger<McpServer> _logger;
    private readonly DocumentTaskParser _documentTaskParser;
    private readonly ScheduleInferenceService _scheduleInferenceService;
    private readonly TaskPlanner _taskPlanner;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServer(
        ILogger<McpServer> logger,
        DocumentTaskParser documentTaskParser,
        ScheduleInferenceService scheduleInferenceService,
        TaskPlanner taskPlanner)
    {
        _logger = logger;
        _documentTaskParser = documentTaskParser;
        _scheduleInferenceService = scheduleInferenceService;
        _taskPlanner = taskPlanner;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting Planning MCP Server");

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
                    name = "planning-mcp",
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
                Name = "parse_document_to_tasks",
                Description = "Extracts tasks from a document (PDF/DOCX/MD/TXT) using heuristic rules",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["path"] = new
                        {
                            type = "string",
                            description = "The absolute path to the document file"
                        },
                        ["language"] = new
                        {
                            type = "string",
                            description = "Optional: The language of the document for better parsing"
                        }
                    },
                    required = new[] { "path" }
                }
            },
            new()
            {
                Name = "infer_schedule_from_document",
                Description = "Infers project schedule (start date, duration, sprint length) from document text and tasks",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["rawText"] = new
                        {
                            type = "string",
                            description = "The raw text from the document"
                        },
                        ["tasks"] = new
                        {
                            type = "array",
                            description = "The list of parsed tasks",
                            items = new { type = "object" }
                        },
                        ["defaultCapacityHoursPerSprint"] = new
                        {
                            type = "number",
                            description = "Optional: Default capacity in hours per sprint (default: 80)"
                        }
                    },
                    required = new[] { "rawText", "tasks" }
                }
            },
            new()
            {
                Name = "plan_tasks_into_iterations",
                Description = "Assigns tasks to iterations based on schedule, capacity, and dependencies",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["tasks"] = new
                        {
                            type = "array",
                            description = "The list of parsed tasks",
                            items = new { type = "object" }
                        },
                        ["schedule"] = new
                        {
                            type = "object",
                            description = "The inferred project schedule"
                        }
                    },
                    required = new[] { "tasks", "schedule" }
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
                "parse_document_to_tasks" => await HandleParseDocumentToTasksAsync(arguments),
                "infer_schedule_from_document" => await HandleInferScheduleAsync(arguments),
                "plan_tasks_into_iterations" => await HandlePlanTasksIntoIterationsAsync(arguments),
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

    private async Task<ToolResult> HandleParseDocumentToTasksAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("path", out var pathElement))
        {
            throw new ArgumentException("path is required");
        }

        var path = pathElement.GetString() ?? throw new ArgumentException("path cannot be null");
        
        string? language = null;
        if (arguments.TryGetValue("language", out var languageElement))
        {
            language = languageElement.GetString();
        }

        var result = await _documentTaskParser.ParseDocumentAsync(path, language);
        var text = JsonSerializer.Serialize(result, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleInferScheduleAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("rawText", out var rawTextElement))
        {
            throw new ArgumentException("rawText is required");
        }

        if (!arguments.TryGetValue("tasks", out var tasksElement))
        {
            throw new ArgumentException("tasks is required");
        }

        var rawText = rawTextElement.GetString() ?? throw new ArgumentException("rawText cannot be null");
        var tasks = JsonSerializer.Deserialize<List<ParsedTask>>(tasksElement.GetRawText()) 
            ?? throw new ArgumentException("tasks cannot be null");

        double? capacity = null;
        if (arguments.TryGetValue("defaultCapacityHoursPerSprint", out var capacityElement))
        {
            if (capacityElement.TryGetDouble(out var capacityValue))
            {
                capacity = capacityValue;
            }
        }

        var schedule = _scheduleInferenceService.InferSchedule(rawText, tasks, capacity);
        var text = JsonSerializer.Serialize(schedule, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandlePlanTasksIntoIterationsAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("tasks", out var tasksElement))
        {
            throw new ArgumentException("tasks is required");
        }

        if (!arguments.TryGetValue("schedule", out var scheduleElement))
        {
            throw new ArgumentException("schedule is required");
        }

        var tasks = JsonSerializer.Deserialize<List<ParsedTask>>(tasksElement.GetRawText()) 
            ?? throw new ArgumentException("tasks cannot be null");
        var schedule = JsonSerializer.Deserialize<ProjectSchedule>(scheduleElement.GetRawText()) 
            ?? throw new ArgumentException("schedule cannot be null");

        var plan = _taskPlanner.PlanTasksIntoIterations(tasks, schedule);
        var text = JsonSerializer.Serialize(plan, _jsonOptions);
        
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
