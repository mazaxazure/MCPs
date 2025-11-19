using System.Text.Json;
using Microsoft.Extensions.Logging;
using Dataverse_MCP.Models;
using Dataverse_MCP.Services;

namespace Dataverse_MCP.Mcp;

public class McpServer
{
    private readonly ILogger<McpServer> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServer(
        ILogger<McpServer> logger,
        IDataverseService dataverseService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting Dataverse MCP Server");

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
                    name = "dataverse-mcp",
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
            new()
            {
                Name = "list_webresources",
                Description = "Lists web resources deployed in the Dataverse environment. Can filter by type (3=JavaScript, 1=HTML, 2=CSS) and name pattern",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["webResourceType"] = new
                        {
                            type = "number",
                            description = "Optional: Filter by resource type. 1=HTML, 2=CSS, 3=JavaScript, 4=XML, 5=PNG, 6=JPG, 7=GIF, 8=XAP, 9=XSL, 10=ICO, 11=SVG, 12=RESX"
                        },
                        ["nameFilter"] = new
                        {
                            type = "string",
                            description = "Optional: Filter by name pattern (partial match supported)"
                        },
                        ["maxResults"] = new
                        {
                            type = "number",
                            description = "Optional: Maximum number of records to return"
                        }
                    },
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "get_webresource_content",
                Description = "Gets the decoded content of a web resource. Can search by ID or by name",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["webResourceId"] = new
                        {
                            type = "string",
                            description = "Optional: The GUID of the web resource"
                        },
                        ["name"] = new
                        {
                            type = "string",
                            description = "Optional: The name of the web resource (e.g., 'new_/scripts/myfile.js')"
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
                "list_webresources" => await HandleListWebResourcesAsync(arguments),
                "get_webresource_content" => await HandleGetWebResourceContentAsync(arguments),
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

    private async Task<ToolResult> HandleListWebResourcesAsync(Dictionary<string, JsonElement>? arguments)
    {
        int? webResourceType = null;
        if (arguments?.TryGetValue("webResourceType", out var typeElement) == true)
        {
            if (typeElement.TryGetInt32(out var typeInt))
            {
                webResourceType = typeInt;
            }
        }

        string? nameFilter = null;
        if (arguments?.TryGetValue("nameFilter", out var nameFilterElement) == true)
        {
            nameFilter = nameFilterElement.GetString();
        }

        int? maxResults = null;
        if (arguments?.TryGetValue("maxResults", out var maxResultsElement) == true)
        {
            if (maxResultsElement.TryGetInt32(out var maxResultsInt))
            {
                maxResults = maxResultsInt;
            }
        }

        var entities = await _dataverseService.ListWebResourcesAsync(webResourceType, nameFilter, maxResults);

        var webResources = entities.Select(entity =>
        {
            var resourceData = new Dictionary<string, object?>
            {
                ["webresourceid"] = entity.Id.ToString(),
                ["name"] = entity.GetAttributeValue<string>("name"),
                ["displayname"] = entity.GetAttributeValue<string>("displayname"),
                ["webresourcetype"] = entity.GetAttributeValue<int>("webresourcetype"),
                ["modifiedon"] = entity.GetAttributeValue<DateTime?>("modifiedon")?.ToString("yyyy-MM-dd HH:mm:ss"),
                ["description"] = entity.GetAttributeValue<string>("description")
            };

            return resourceData;
        }).ToList();

        var text = JsonSerializer.Serialize(webResources, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private async Task<ToolResult> HandleGetWebResourceContentAsync(Dictionary<string, JsonElement>? arguments)
    {
        string? content = null;
        string? resourceIdentifier = null;

        // Try to get by ID first
        if (arguments?.TryGetValue("webResourceId", out var idElement) == true)
        {
            var idString = idElement.GetString();
            if (!string.IsNullOrEmpty(idString) && Guid.TryParse(idString, out var webResourceId))
            {
                resourceIdentifier = idString;
                content = await _dataverseService.GetWebResourceContentAsync(webResourceId);
            }
        }

        // If not found by ID, try by name
        if (content == null && arguments?.TryGetValue("name", out var nameElement) == true)
        {
            var name = nameElement.GetString();
            if (!string.IsNullOrEmpty(name))
            {
                resourceIdentifier = name;
                content = await _dataverseService.GetWebResourceContentByNameAsync(name);
            }
        }

        if (content == null)
        {
            if (string.IsNullOrEmpty(resourceIdentifier))
            {
                throw new ArgumentException("Either webResourceId or name must be provided");
            }
            
            throw new Exception($"WebResource not found or has no content: {resourceIdentifier}");
        }

        var result = new
        {
            identifier = resourceIdentifier,
            content = content,
            contentLength = content.Length
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
