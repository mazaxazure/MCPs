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
