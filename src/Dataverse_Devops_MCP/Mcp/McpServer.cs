using System.Text.Json;
using Microsoft.Extensions.Logging;
using Dataverse_Devops_MCP.Models;
using Dataverse_Devops_MCP.Models.Planning;
using Dataverse_Devops_MCP.Services;
using Dataverse_Devops_MCP.Services.Planning;

namespace Dataverse_Devops_MCP.Mcp;

public class McpServer
{
    private readonly ILogger<McpServer> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly IDevOpsService _devOpsService;
    private readonly DocumentTaskParser _documentTaskParser;
    private readonly ScheduleInferenceService _scheduleInferenceService;
    private readonly TaskPlanner _taskPlanner;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServer(
        ILogger<McpServer> logger,
        IDataverseService dataverseService,
        IDevOpsService devOpsService,
        DocumentTaskParser documentTaskParser,
        ScheduleInferenceService scheduleInferenceService,
        TaskPlanner taskPlanner)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _devOpsService = devOpsService;
        _documentTaskParser = documentTaskParser;
        _scheduleInferenceService = scheduleInferenceService;
        _taskPlanner = taskPlanner;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false, // MCP protocol should not use indentation
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting Dataverse Metadata MCP Server");

        // Connect to Dataverse
        var connected = await _dataverseService.ConnectAsync();
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
                    
                    // Only send response if there's an ID (not a notification)
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
        // notifications/initialized is a notification, no response needed
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
                    name = "dataverse-metadata-mcp",
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
                Name = "list_entities",
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
                Name = "get_entity_metadata",
                Description = "Gets detailed metadata for a specific entity",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = new
                        {
                            type = "string",
                            description = "The logical name of the entity (e.g., 'account', 'contact')"
                        }
                    },
                    required = new[] { "entityLogicalName" }
                }
            },
            new()
            {
                Name = "get_entity_attributes",
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
                Name = "get_entity_relationships",
                Description = "Gets all relationships for a specific entity",
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
                Name = "create_record",
                Description = "Creates a new record in a Dataverse entity",
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
                Name = "get_record",
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
                            description = "Optional: Array of column names to retrieve. If omitted, all columns are returned",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "entityLogicalName", "id" }
                }
            },
            new()
            {
                Name = "update_record",
                Description = "Updates an existing record in Dataverse",
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
                            description = "The GUID of the record to update"
                        },
                        ["attributes"] = new
                        {
                            type = "object",
                            description = "Key-value pairs of attributes to update"
                        }
                    },
                    required = new[] { "entityLogicalName", "id", "attributes" }
                }
            },
            new()
            {
                Name = "delete_record",
                Description = "Deletes a record from Dataverse",
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
                            description = "The GUID of the record to delete"
                        }
                    },
                    required = new[] { "entityLogicalName", "id" }
                }
            },
            new()
            {
                Name = "query_records",
                Description = "Query multiple records from a Dataverse entity",
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
                            description = "Optional: Simple filter in format 'attributename eq value' or 'attributename ne value'"
                        },
                        ["columns"] = new
                        {
                            type = "array",
                            description = "Optional: Array of column names to retrieve. If omitted, all columns are returned",
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
            // Planning tools
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
            },
            new()
            {
                Name = "run_document_to_ado_project",
                Description = "End-to-end pipeline: parses document, infers schedule, plans tasks, and adds them to an existing Azure DevOps project configured in settings",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["path"] = new
                        {
                            type = "string",
                            description = "The absolute path to the document file"
                        }
                    },
                    required = new[] { "path" }
                }
            },
            // Azure DevOps work item management tools
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
                Name = "test_custom_plugins",
                Description = "Gets custom plugins from Dataverse, creates test tasks in DevOps for each plugin step, executes the tests, and updates the task status with results",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["executeTests"] = new
                        {
                            type = "boolean",
                            description = "Optional: Whether to execute tests after creating tasks (default: true)"
                        },
                        ["updateDevOps"] = new
                        {
                            type = "boolean",
                            description = "Optional: Whether to update DevOps with test results (default: true)"
                        }
                    },
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
                "list_entities" => await HandleListEntitiesAsync(),
                "get_entity_metadata" => await HandleGetEntityMetadataAsync(arguments),
                "get_entity_attributes" => await HandleGetEntityAttributesAsync(arguments),
                "get_entity_relationships" => await HandleGetEntityRelationshipsAsync(arguments),
                "create_record" => await HandleCreateRecordAsync(arguments),
                "get_record" => await HandleGetRecordAsync(arguments),
                "update_record" => await HandleUpdateRecordAsync(arguments),
                "delete_record" => await HandleDeleteRecordAsync(arguments),
                "query_records" => await HandleQueryRecordsAsync(arguments),
                // Planning tools
                "parse_document_to_tasks" => await HandleParseDocumentToTasksAsync(arguments),
                "infer_schedule_from_document" => await HandleInferScheduleAsync(arguments),
                "plan_tasks_into_iterations" => await HandlePlanTasksIntoIterationsAsync(arguments),
                "create_ado_project_from_tasks" => await HandleCreateAdoProjectFromTasksAsync(arguments),
                "run_document_to_ado_project" => await HandleRunDocumentToAdoProjectAsync(arguments),
                // DevOps tools
                "create_work_item" => await HandleCreateWorkItemAsync(arguments),
                "update_work_item" => await HandleUpdateWorkItemAsync(arguments),
                "delete_work_item" => await HandleDeleteWorkItemAsync(arguments),
                "get_work_item" => await HandleGetWorkItemAsync(arguments),
                "list_work_items" => await HandleListWorkItemsAsync(arguments),
                "test_custom_plugins" => await HandleTestCustomPluginsAsync(arguments),
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

    private async Task<ToolResult> HandleListEntitiesAsync()
    {
        var entities = await _dataverseService.GetAllEntitiesAsync();
        var entityList = entities
            .OrderBy(e => e.LogicalName)
            .Select(e => new
            {
                logicalName = e.LogicalName,
                displayName = e.DisplayName?.UserLocalizedLabel?.Label,
                schemaName = e.SchemaName
            })
            .ToList();

        var text = JsonSerializer.Serialize(entityList, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleGetEntityMetadataAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("entityLogicalName", out var entityNameElement))
        {
            throw new ArgumentException("entityLogicalName is required");
        }

        var entityLogicalName = entityNameElement.GetString() ?? throw new ArgumentException("entityLogicalName cannot be null");
        var metadata = await _dataverseService.GetEntityMetadataAsync(entityLogicalName);

        if (metadata == null)
        {
            throw new Exception($"Entity not found: {entityLogicalName}");
        }

        var entityInfo = new
        {
            logicalName = metadata.LogicalName,
            schemaName = metadata.SchemaName,
            displayName = metadata.DisplayName?.UserLocalizedLabel?.Label,
            description = metadata.Description?.UserLocalizedLabel?.Label,
            primaryIdAttribute = metadata.PrimaryIdAttribute,
            primaryNameAttribute = metadata.PrimaryNameAttribute,
            objectTypeCode = metadata.ObjectTypeCode,
            isCustomEntity = metadata.IsCustomEntity,
            isActivity = metadata.IsActivity,
            ownershipType = metadata.OwnershipType?.ToString()
        };

        var text = JsonSerializer.Serialize(entityInfo, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleGetEntityAttributesAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("entityLogicalName", out var entityNameElement))
        {
            throw new ArgumentException("entityLogicalName is required");
        }

        var entityLogicalName = entityNameElement.GetString() ?? throw new ArgumentException("entityLogicalName cannot be null");
        var attributes = await _dataverseService.GetEntityAttributesAsync(entityLogicalName);

        var attributeList = attributes
            .OrderBy(a => a.LogicalName)
            .Select(a => new
            {
                logicalName = a.LogicalName,
                schemaName = a.SchemaName,
                displayName = a.DisplayName?.UserLocalizedLabel?.Label,
                description = a.Description?.UserLocalizedLabel?.Label,
                attributeType = a.AttributeType?.ToString(),
                isCustomAttribute = a.IsCustomAttribute,
                isPrimaryId = a.IsPrimaryId,
                isPrimaryName = a.IsPrimaryName,
                requiredLevel = a.RequiredLevel?.Value.ToString()
            })
            .ToList();

        var text = JsonSerializer.Serialize(attributeList, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleGetEntityRelationshipsAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("entityLogicalName", out var entityNameElement))
        {
            throw new ArgumentException("entityLogicalName is required");
        }

        var entityLogicalName = entityNameElement.GetString() ?? throw new ArgumentException("entityLogicalName cannot be null");
        var relationships = await _dataverseService.GetEntityRelationshipsAsync(entityLogicalName);

        var relationshipList = relationships
            .OrderBy(r => r.SchemaName)
            .Select(r => new
            {
                schemaName = r.SchemaName,
                referencingEntity = r.ReferencingEntity,
                referencingAttribute = r.ReferencingAttribute,
                referencedEntity = r.ReferencedEntity,
                referencedAttribute = r.ReferencedAttribute,
                relationshipType = r.RelationshipType.ToString()
            })
            .ToList();

        var text = JsonSerializer.Serialize(relationshipList, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleCreateRecordAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("entityLogicalName", out var entityNameElement))
        {
            throw new ArgumentException("entityLogicalName is required");
        }

        if (!arguments.TryGetValue("attributes", out var attributesElement))
        {
            throw new ArgumentException("attributes is required");
        }

        var entityLogicalName = entityNameElement.GetString() ?? throw new ArgumentException("entityLogicalName cannot be null");
        var attributes = JsonSerializer.Deserialize<Dictionary<string, object>>(attributesElement.GetRawText()) 
            ?? throw new ArgumentException("attributes cannot be null");

        var id = await _dataverseService.CreateRecordAsync(entityLogicalName, attributes);

        var result = new
        {
            id = id.ToString(),
            entityLogicalName = entityLogicalName,
            message = "Record created successfully"
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

    private async Task<ToolResult> HandleGetRecordAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("entityLogicalName", out var entityNameElement))
        {
            throw new ArgumentException("entityLogicalName is required");
        }

        if (!arguments.TryGetValue("id", out var idElement))
        {
            throw new ArgumentException("id is required");
        }

        var entityLogicalName = entityNameElement.GetString() ?? throw new ArgumentException("entityLogicalName cannot be null");
        var idString = idElement.GetString() ?? throw new ArgumentException("id cannot be null");

        if (!Guid.TryParse(idString, out var id))
        {
            throw new ArgumentException("id must be a valid GUID");
        }

        string[]? columns = null;
        if (arguments.TryGetValue("columns", out var columnsElement))
        {
            columns = JsonSerializer.Deserialize<string[]>(columnsElement.GetRawText());
        }

        var entity = await _dataverseService.GetRecordAsync(entityLogicalName, id, columns);

        if (entity == null)
        {
            throw new Exception($"Record not found: {id}");
        }

        var recordData = new Dictionary<string, object?>
        {
            ["id"] = entity.Id.ToString(),
            ["entityLogicalName"] = entity.LogicalName
        };

        foreach (var attr in entity.Attributes)
        {
            recordData[attr.Key] = attr.Value?.ToString();
        }

        var text = JsonSerializer.Serialize(recordData, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleUpdateRecordAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("entityLogicalName", out var entityNameElement))
        {
            throw new ArgumentException("entityLogicalName is required");
        }

        if (!arguments.TryGetValue("id", out var idElement))
        {
            throw new ArgumentException("id is required");
        }

        if (!arguments.TryGetValue("attributes", out var attributesElement))
        {
            throw new ArgumentException("attributes is required");
        }

        var entityLogicalName = entityNameElement.GetString() ?? throw new ArgumentException("entityLogicalName cannot be null");
        var idString = idElement.GetString() ?? throw new ArgumentException("id cannot be null");

        if (!Guid.TryParse(idString, out var id))
        {
            throw new ArgumentException("id must be a valid GUID");
        }

        var attributes = JsonSerializer.Deserialize<Dictionary<string, object>>(attributesElement.GetRawText()) 
            ?? throw new ArgumentException("attributes cannot be null");

        await _dataverseService.UpdateRecordAsync(entityLogicalName, id, attributes);

        var result = new
        {
            id = id.ToString(),
            entityLogicalName = entityLogicalName,
            message = "Record updated successfully"
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

    private async Task<ToolResult> HandleDeleteRecordAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("entityLogicalName", out var entityNameElement))
        {
            throw new ArgumentException("entityLogicalName is required");
        }

        if (!arguments.TryGetValue("id", out var idElement))
        {
            throw new ArgumentException("id is required");
        }

        var entityLogicalName = entityNameElement.GetString() ?? throw new ArgumentException("entityLogicalName cannot be null");
        var idString = idElement.GetString() ?? throw new ArgumentException("id cannot be null");

        if (!Guid.TryParse(idString, out var id))
        {
            throw new ArgumentException("id must be a valid GUID");
        }

        await _dataverseService.DeleteRecordAsync(entityLogicalName, id);

        var result = new
        {
            id = id.ToString(),
            entityLogicalName = entityLogicalName,
            message = "Record deleted successfully"
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

    private async Task<ToolResult> HandleQueryRecordsAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("entityLogicalName", out var entityNameElement))
        {
            throw new ArgumentException("entityLogicalName is required");
        }

        var entityLogicalName = entityNameElement.GetString() ?? throw new ArgumentException("entityLogicalName cannot be null");

        string? filter = null;
        if (arguments.TryGetValue("filter", out var filterElement))
        {
            filter = filterElement.GetString();
        }

        string[]? columns = null;
        if (arguments.TryGetValue("columns", out var columnsElement))
        {
            columns = JsonSerializer.Deserialize<string[]>(columnsElement.GetRawText());
        }

        int? maxResults = null;
        if (arguments.TryGetValue("maxResults", out var maxResultsElement))
        {
            if (maxResultsElement.TryGetInt32(out var maxResultsInt))
            {
                maxResults = maxResultsInt;
            }
        }

        var entities = await _dataverseService.QueryRecordsAsync(entityLogicalName, filter, columns, maxResults);

        var records = entities.Select(entity =>
        {
            var recordData = new Dictionary<string, object?>
            {
                ["id"] = entity.Id.ToString(),
                ["entityLogicalName"] = entity.LogicalName
            };

            foreach (var attr in entity.Attributes)
            {
                recordData[attr.Key] = attr.Value?.ToString();
            }

            return recordData;
        }).ToList();

        var text = JsonSerializer.Serialize(records, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    // Planning tool handlers
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

    private async Task<ToolResult> HandleRunDocumentToAdoProjectAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("path", out var pathElement))
        {
            throw new ArgumentException("path is required");
        }

        var path = pathElement.GetString() ?? throw new ArgumentException("path cannot be null");

        _logger.LogInformation($"Starting end-to-end document to ADO project pipeline for: {path}");

        // Step 1: Parse document to tasks
        _logger.LogInformation("Step 1: Parsing document to extract tasks");
        var parseResult = await _documentTaskParser.ParseDocumentAsync(path);
        _logger.LogInformation($"Extracted {parseResult.Tasks.Count} tasks");

        if (parseResult.Tasks.Count == 0)
        {
            throw new Exception("No tasks found in document. Ensure the document contains task items with action verbs or bullet points.");
        }

        // Step 2: Infer schedule
        _logger.LogInformation("Step 2: Inferring project schedule");
        var schedule = _scheduleInferenceService.InferSchedule(parseResult.RawText, parseResult.Tasks);
        _logger.LogInformation($"Schedule inferred: {schedule.SprintCount} sprints, {schedule.SprintLengthDays} days each");

        // Step 3: Plan tasks into iterations
        _logger.LogInformation("Step 3: Planning tasks into iterations");
        var plan = _taskPlanner.PlanTasksIntoIterations(parseResult.Tasks, schedule);
        _logger.LogInformation($"Planned {plan.Summary.Items} tasks into {plan.Iterations.Count} iterations");

        // Step 4: Create project in Azure DevOps
        _logger.LogInformation("Step 4: Creating project in Azure DevOps");
        var adoResult = await _devOpsService.CreateProjectFromPlanAsync(plan, schedule, parseResult.Digest);
        _logger.LogInformation($"Created {adoResult.WorkItemsCreated} work items in {adoResult.IterationsCreated} iterations");
        
        // Build comprehensive result
        var finalResult = new DocumentToAdoResult
        {
            ProjectId = adoResult.ProjectId,
            ProjectName = adoResult.ProjectName,
            WebUrl = adoResult.WebUrl,
            Digest = parseResult.Digest,
            TasksFound = parseResult.Tasks.Count,
            SprintsCreated = adoResult.IterationsCreated,
            WorkItemsCreated = adoResult.WorkItemsCreated,
            TotalEstimatedHours = plan.Summary.Hours,
            Schedule = schedule
        };

        var text = JsonSerializer.Serialize(finalResult, _jsonOptions);
        
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    // DevOps tool handlers
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

    private async Task<ToolResult> HandleTestCustomPluginsAsync(Dictionary<string, JsonElement>? arguments)
    {
        _logger.LogInformation("Starting custom plugin testing workflow");

        var executeTests = true;
        var updateDevOps = true;

        if (arguments != null)
        {
            if (arguments.TryGetValue("executeTests", out var executeElement))
            {
                executeTests = executeElement.GetBoolean();
            }

            if (arguments.TryGetValue("updateDevOps", out var updateElement))
            {
                updateDevOps = updateElement.GetBoolean();
            }
        }

        var results = new List<object>();

        try
        {
            // Step 1: Get custom plugins from Dataverse
            _logger.LogInformation("Step 1: Retrieving custom plugins from Dataverse");
            var plugins = await GetCustomPluginsAsync();
            _logger.LogInformation($"Found {plugins.Count} custom plugins");

            if (plugins.Count == 0)
            {
                return new ToolResult
                {
                    Content = new List<ContentItem>
                    {
                        new() { Type = "text", Text = JsonSerializer.Serialize(new { message = "No custom plugins found in Dataverse", plugins = 0, tasksCreated = 0 }, _jsonOptions) }
                    }
                };
            }

            // Step 2: Create test tasks in DevOps for each plugin step
            _logger.LogInformation("Step 2: Creating test tasks in Azure DevOps");
            var tasksCreated = new List<int>();

            foreach (var plugin in plugins)
            {
                var steps = await GetPluginStepsAsync(plugin["plugintypeid"].ToString()!);
                
                foreach (var step in steps)
                {
                    var taskTitle = $"Test Plugin: {plugin["name"]} - {step["stage"]} on {step["message"]}";
                    var taskDescription = GeneratePluginTestDescription(plugin, step);

                    var workItemId = await _devOpsService.CreateWorkItemAsync(
                        title: taskTitle,
                        description: taskDescription,
                        workItemType: "Task",
                        priority: 2,
                        estimatedHours: 2,
                        tags: new List<string> { "plugin-test", plugin["name"].ToString()!, step["message"].ToString()! }
                    );

                    tasksCreated.Add(workItemId);
                    _logger.LogInformation($"Created task {workItemId} for plugin step");

                    // Step 3: Execute tests if requested
                    if (executeTests)
                    {
                        _logger.LogInformation($"Step 3: Executing tests for work item {workItemId}");
                        var testResult = await ExecutePluginTestAsync(plugin, step);
                        
                        // Step 4: Update DevOps with results
                        if (updateDevOps)
                        {
                            _logger.LogInformation($"Step 4: Updating work item {workItemId} with test results");
                            await UpdateWorkItemWithTestResultsAsync(workItemId, plugin, step, testResult);
                        }

                        results.Add(new
                        {
                            workItemId = workItemId,
                            plugin = plugin["name"],
                            step = step["message"],
                            stage = step["stage"],
                            testExecuted = true,
                            testResult = testResult
                        });
                    }
                    else
                    {
                        results.Add(new
                        {
                            workItemId = workItemId,
                            plugin = plugin["name"],
                            step = step["message"],
                            stage = step["stage"],
                            testExecuted = false
                        });
                    }
                }
            }

            var summary = new
            {
                pluginsFound = plugins.Count,
                tasksCreated = tasksCreated.Count,
                testsExecuted = executeTests,
                results = results
            };

            var text = JsonSerializer.Serialize(summary, _jsonOptions);
            return new ToolResult
            {
                Content = new List<ContentItem>
                {
                    new() { Type = "text", Text = text }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in plugin testing workflow");
            throw;
        }
    }

    private async Task<List<Dictionary<string, object>>> GetCustomPluginsAsync()
    {
        // Query custom plugins (customizationlevel = 1)
        var entities = await _dataverseService.QueryRecordsAsync(
            "plugintype",
            "isworkflowactivity eq false and iscustomizable eq true",
            new[] { "plugintypeid", "name", "typename", "assemblyname", "friendlyname", "description" },
            50
        );

        return entities.Select(e => e.Attributes.ToDictionary(
            kvp => kvp.Key,
            kvp => (object)(kvp.Value?.ToString() ?? string.Empty)
        )).ToList();
    }

    private async Task<List<Dictionary<string, object>>> GetPluginStepsAsync(string pluginTypeId)
    {
        // Get all steps for this plugin
        var steps = await _dataverseService.QueryRecordsAsync(
            "sdkmessageprocessingstep",
            $"plugintypeid eq {pluginTypeId}",
            new[] { "sdkmessageprocessingstepid", "name", "stage", "mode", "rank", "description" },
            100
        );

        var result = new List<Dictionary<string, object>>();
        
        foreach (var step in steps)
        {
            var stepData = new Dictionary<string, object>
            {
                ["sdkmessageprocessingstepid"] = step.Id.ToString(),
                ["name"] = step.GetAttributeValue<string>("name") ?? "Unnamed Step",
                ["stage"] = GetStageName(step.GetAttributeValue<int>("stage")),
                ["mode"] = step.GetAttributeValue<int>("mode") == 0 ? "Synchronous" : "Asynchronous",
                ["rank"] = step.GetAttributeValue<int>("rank"),
                ["description"] = step.GetAttributeValue<string>("description") ?? "",
                ["message"] = await GetMessageNameForStepAsync(step.Id.ToString())
            };
            
            result.Add(stepData);
        }

        return result;
    }

    private string GetStageName(int stage)
    {
        return stage switch
        {
            10 => "PreValidation",
            20 => "PreOperation",
            40 => "PostOperation",
            _ => $"Unknown({stage})"
        };
    }

    private async Task<string> GetMessageNameForStepAsync(string stepId)
    {
        try
        {
            var step = await _dataverseService.GetRecordAsync("sdkmessageprocessingstep", Guid.Parse(stepId), new[] { "sdkmessageid" });
            if (step != null && step.Contains("sdkmessageid"))
            {
                var messageRef = step.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("sdkmessageid");
                if (messageRef != null)
                {
                    var message = await _dataverseService.GetRecordAsync("sdkmessage", messageRef.Id, new[] { "name" });
                    return message?.GetAttributeValue<string>("name") ?? "Unknown";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Could not get message name for step {stepId}");
        }

        return "Unknown";
    }

    private string GeneratePluginTestDescription(Dictionary<string, object> plugin, Dictionary<string, object> step)
    {
        return $@"<div style=""font-family:Segoe UI, Arial, sans-serif;"">
<div style=""background-color:#fff4e5;border-left:4px solid #ff9800;padding:12px;margin-bottom:16px;"">
<strong style=""color:#e65100;"">⚠️ PRUEBA DE PLUGIN PENDIENTE</strong>
</div>

<p><strong>PLUGIN:</strong> {plugin["name"]}</p>
<p><strong>ASSEMBLY:</strong> {plugin["assemblyname"]}</p>

<div style=""margin:16px 0;"">
<p><strong>DETALLES DEL STEP:</strong></p>
<ul style=""list-style:none;padding-left:0;"">
<li>📋 <strong>Nombre:</strong> {step["name"]}</li>
<li>🔧 <strong>Stage:</strong> {step["stage"]}</li>
<li>📨 <strong>Message:</strong> {step["message"]}</li>
<li>⚡ <strong>Mode:</strong> {step["mode"]}</li>
<li>🎯 <strong>Rank:</strong> {step["rank"]}</li>
</ul>
</div>

<div style=""margin:16px 0;"">
<p><strong>PRUEBAS A REALIZAR:</strong></p>
<ol>
<li>Crear registro que active el plugin</li>
<li>Verificar que el plugin se ejecuta correctamente</li>
<li>Validar que no hay errores en los logs</li>
<li>Comprobar que los datos se procesan como se espera</li>
</ol>
</div>

<div style=""margin:16px 0;"">
<p><strong>CRITERIOS DE ÉXITO:</strong></p>
<ul>
<li>✅ El plugin se ejecuta sin errores</li>
<li>✅ Los datos se procesan correctamente</li>
<li>✅ No hay excepciones en los logs</li>
<li>✅ El rendimiento es aceptable</li>
</ul>
</div>

<div style=""margin-top:16px;padding-top:12px;border-top:1px solid #e0e0e0;font-size:0.9em;color:#666;"">
<em>Tarea generada automáticamente por MCP Server - Plugin Testing Tool</em>
</div>
</div>";
    }

    private async Task<Dictionary<string, object>> ExecutePluginTestAsync(Dictionary<string, object> plugin, Dictionary<string, object> step)
    {
        var testResult = new Dictionary<string, object>
        {
            ["executed"] = true,
            ["startTime"] = DateTime.UtcNow,
            ["success"] = false,
            ["recordsCreated"] = 0,
            ["recordsFailed"] = 0,
            ["errors"] = new List<string>()
        };

        try
        {
            // Determine entity based on plugin name or step
            var entityName = DetermineEntityFromPlugin(plugin, step);
            var errors = new List<string>();
            var recordsCreated = 0;
            var recordsFailed = 0;

            _logger.LogInformation($"Testing plugin on entity: {entityName}");

            // Create test records to trigger the plugin
            var testRecords = GenerateTestRecordsForEntity(entityName);

            foreach (var testRecord in testRecords)
            {
                try
                {
                    var recordId = await _dataverseService.CreateRecordAsync(entityName, testRecord);
                    recordsCreated++;
                    _logger.LogInformation($"Successfully created test record {recordId} for plugin test");
                }
                catch (Exception ex)
                {
                    recordsFailed++;
                    errors.Add($"Failed to create record: {ex.Message}");
                    _logger.LogWarning(ex, "Failed to create test record for plugin test");
                }
            }

            testResult["recordsCreated"] = recordsCreated;
            testResult["recordsFailed"] = recordsFailed;
            testResult["errors"] = errors;
            testResult["success"] = recordsCreated > 0;
            testResult["endTime"] = DateTime.UtcNow;
            testResult["duration"] = ((DateTime)testResult["endTime"] - (DateTime)testResult["startTime"]).TotalSeconds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing plugin test");
            testResult["success"] = false;
            ((List<string>)testResult["errors"]).Add($"Test execution error: {ex.Message}");
        }

        return testResult;
    }

    private string DetermineEntityFromPlugin(Dictionary<string, object> plugin, Dictionary<string, object> step)
    {
        var pluginName = plugin["name"].ToString()!.ToLower();
        var message = step["message"].ToString()!.ToLower();

        // Try to determine from common patterns
        if (pluginName.Contains("account") || message.Contains("account")) return "account";
        if (pluginName.Contains("contact") || message.Contains("contact")) return "contact";
        if (pluginName.Contains("opportunity") || message.Contains("opportunity")) return "opportunity";
        if (pluginName.Contains("lead") || message.Contains("lead")) return "lead";
        if (pluginName.Contains("case") || pluginName.Contains("incident") || message.Contains("incident")) return "incident";

        // Default to account for testing
        return "account";
    }

    private List<Dictionary<string, object>> GenerateTestRecordsForEntity(string entityName)
    {
        var records = new List<Dictionary<string, object>>();

        switch (entityName.ToLower())
        {
            case "account":
                records.Add(new Dictionary<string, object>
                {
                    ["name"] = $"Plugin Test Account {DateTime.UtcNow:yyyyMMddHHmmss}",
                    ["accountnumber"] = $"TEST-{Guid.NewGuid().ToString().Substring(0, 8)}",
                    ["telephone1"] = "+34900000000"
                });
                break;

            case "contact":
                records.Add(new Dictionary<string, object>
                {
                    ["firstname"] = "Plugin",
                    ["lastname"] = $"Test {DateTime.UtcNow:yyyyMMddHHmmss}",
                    ["emailaddress1"] = $"plugintest{Guid.NewGuid().ToString().Substring(0, 8)}@test.com"
                });
                break;

            case "opportunity":
                records.Add(new Dictionary<string, object>
                {
                    ["name"] = $"Plugin Test Opportunity {DateTime.UtcNow:yyyyMMddHHmmss}",
                    ["estimatedvalue"] = 10000
                });
                break;

            case "lead":
                records.Add(new Dictionary<string, object>
                {
                    ["subject"] = $"Plugin Test Lead {DateTime.UtcNow:yyyyMMddHHmmss}",
                    ["firstname"] = "Test",
                    ["lastname"] = "Lead"
                });
                break;

            case "incident":
                records.Add(new Dictionary<string, object>
                {
                    ["title"] = $"Plugin Test Case {DateTime.UtcNow:yyyyMMddHHmmss}",
                    ["description"] = "Test case created for plugin testing"
                });
                break;

            default:
                // Generic record
                records.Add(new Dictionary<string, object>
                {
                    ["name"] = $"Plugin Test {DateTime.UtcNow:yyyyMMddHHmmss}"
                });
                break;
        }

        return records;
    }

    private async Task UpdateWorkItemWithTestResultsAsync(int workItemId, Dictionary<string, object> plugin, Dictionary<string, object> step, Dictionary<string, object> testResult)
    {
        var success = (bool)testResult["success"];
        var recordsCreated = (int)testResult["recordsCreated"];
        var recordsFailed = (int)testResult["recordsFailed"];
        var duration = testResult.ContainsKey("duration") ? (double)testResult["duration"] : 0;
        var errors = (List<string>)testResult["errors"];

        var statusDiv = success
            ? @"<div style=""background-color:#d4edda;border-left:4px solid #28a745;padding:12px;margin-bottom:16px;"">
<strong style=""color:#155724;"">✅ PRUEBA COMPLETADA EXITOSAMENTE</strong>
</div>"
            : @"<div style=""background-color:#f8d7da;border-left:4px solid #dc3545;padding:12px;margin-bottom:16px;"">
<strong style=""color:#721c24;"">❌ PRUEBA COMPLETADA CON ERRORES</strong>
</div>";

        var errorSection = errors.Count > 0
            ? $@"<div style=""margin:16px 0;"">
<p><strong>ERRORES ENCONTRADOS:</strong></p>
<ul>
{string.Join("\n", errors.Select(e => "<li style=\"color:#dc3545;\">" + e + "</li>"))}
</ul>
</div>"
            : "";

        var historyUpdate = $@"<div style=""font-family:Segoe UI, Arial, sans-serif;"">
{statusDiv}
<p><strong>PLUGIN:</strong> {plugin["name"]}</p>
<p><strong>STEP:</strong> {step["name"]} ({step["stage"]} - {step["message"]})</p>
<div style=""margin:16px 0;"">
<p><strong>RESULTADO DE LA PRUEBA:</strong></p>
<ul style=""list-style:none;padding-left:0;"">
<li>✅ Registros creados: <strong>{recordsCreated}</strong></li>
<li>❌ Registros fallidos: <strong>{recordsFailed}</strong></li>
<li>⏱️ Duración: <strong>{duration:F2}s</strong></li>
<li>📅 Fecha: <strong>{DateTime.Now:dd/MM/yyyy, HH:mm:ss}</strong></li>
</ul>
</div>
{errorSection}
<div style=""margin-top:16px;padding-top:12px;border-top:1px solid #e0e0e0;font-size:0.9em;color:#666;"">
<em>Prueba ejecutada automáticamente por MCP Server - Plugin Testing Tool</em><br>
<em>Duración total: {duration:F2} segundos</em>
</div>
</div>";

        var fields = new Dictionary<string, object?>
        {
            ["System.History"] = historyUpdate,
            ["System.State"] = success ? "Done" : "Active"
        };

        if (!success)
        {
            fields["System.Reason"] = "Blocked";
        }

        await _devOpsService.UpdateWorkItemAsync(workItemId, fields);
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
