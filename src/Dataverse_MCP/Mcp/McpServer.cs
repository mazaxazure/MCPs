using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
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

        // Attempt an initial connection, but do NOT abort if it fails: the MCP handshake
        // (initialize / tools/list) must work regardless so the client can discover the
        // server. Tool calls will (re)connect lazily and report a clear error if the
        // connection is unavailable.
        var connected = await _dataverseService.ConnectAsync();
        if (!connected)
        {
            _logger.LogWarning("Initial Dataverse connection failed. The server will keep serving metadata/handshake requests and retry connecting on the first tool call.");
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
                if (line == null)
                {
                    // stdin closed (EOF): the client disconnected. Exit cleanly instead
                    // of busy-looping on repeated null reads.
                    break;
                }
                if (line.Length == 0)
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
                Name = "whoami",
                Description = "Returns the identity and organization the server is connected to (WhoAmI). Useful to verify connectivity.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = new string[] { }
                }
            },
            new()
            {
                Name = "list_entities",
                Description = "Lists all entities (tables) in the Dataverse environment",
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
                Description = "Gets all attributes for an entity INCLUDING the information needed to write values: attribute type, whether it is required, option-set options (value + label) for picklists/multiselect/status, lookup target entities, boolean labels, string max length, number min/max, and datetime behavior. Always call this before create_record/update_record to know what value each attribute expects.",
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
                Description = "Gets all relationships for an entity (OneToMany, ManyToOne and ManyToMany). Use the ManyToMany schema names with associate_records/disassociate_records.",
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
                Name = "get_global_optionset",
                Description = "Gets the options (value + label) of a global option set by name.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["name"] = new
                        {
                            type = "string",
                            description = "The name of the global option set"
                        }
                    },
                    required = new[] { "name" }
                }
            },
            new()
            {
                Name = "create_record",
                Description = "Creates a new record. Values are converted automatically using metadata. Formats: lookup -> GUID string, or object { \"entity\": \"contact\", \"id\": \"<guid>\" }, or { \"entity\": \"contact\", \"name\": \"<primary name>\" } (required for polymorphic lookups like Customer). OptionSet -> numeric value or its label. Multi-select option set -> array of values/labels (e.g., [1,2]). Boolean -> true/false or its label. DateTime -> ISO 8601 string. Money/Decimal/Number -> number.",
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
                            description = "Key-value pairs of attributes to set. See tool description for value formats per type."
                        }
                    },
                    required = new[] { "entityLogicalName", "attributes" }
                }
            },
            new()
            {
                Name = "get_record",
                Description = "Retrieves a specific record by ID. Returns typed attribute values plus a 'formattedValues' object with the display labels (option-set labels, lookup names, formatted dates/money).",
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
                Description = "Updates an existing record. Same value formats as create_record (lookups, option sets, multi-select, booleans, dates, etc.).",
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
                            description = "Key-value pairs of attributes to update. See create_record for value formats per type."
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
                Description = "Query multiple records. The filter supports several conditions joined by ' and ' or ' or ' (not mixed), operators eq/ne/gt/lt/ge/le/like/contains and the checks 'attr null'/'attr notnull'. Values are coerced to the attribute type (option sets accept value or label, lookups accept GUIDs).",
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
                            description = "Optional. e.g. \"statecode eq 0 and name like Contoso\" or \"revenue gt 1000\" or \"primarycontactid notnull\""
                        },
                        ["columns"] = new
                        {
                            type = "array",
                            description = "Optional: Array of column names to retrieve. If omitted, all columns are returned",
                            items = new { type = "string" }
                        },
                        ["orderBy"] = new
                        {
                            type = "string",
                            description = "Optional: attribute logical name to sort by"
                        },
                        ["orderDescending"] = new
                        {
                            type = "boolean",
                            description = "Optional: sort descending (default false)"
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
                Name = "associate_records",
                Description = "Creates N:N associations between a record and one or more related records using a ManyToMany relationship schema name.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = new { type = "string", description = "The logical name of the primary entity" },
                        ["id"] = new { type = "string", description = "The GUID of the primary record" },
                        ["relationshipName"] = new { type = "string", description = "The ManyToMany relationship schema name" },
                        ["relatedEntity"] = new { type = "string", description = "The logical name of the related entity" },
                        ["relatedIds"] = new { type = "array", description = "GUIDs of the related records", items = new { type = "string" } }
                    },
                    required = new[] { "entityLogicalName", "id", "relationshipName", "relatedEntity", "relatedIds" }
                }
            },
            new()
            {
                Name = "disassociate_records",
                Description = "Removes N:N associations between a record and one or more related records using a ManyToMany relationship schema name.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = new { type = "string", description = "The logical name of the primary entity" },
                        ["id"] = new { type = "string", description = "The GUID of the primary record" },
                        ["relationshipName"] = new { type = "string", description = "The ManyToMany relationship schema name" },
                        ["relatedEntity"] = new { type = "string", description = "The logical name of the related entity" },
                        ["relatedIds"] = new { type = "array", description = "GUIDs of the related records", items = new { type = "string" } }
                    },
                    required = new[] { "entityLogicalName", "id", "relationshipName", "relatedEntity", "relatedIds" }
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

        // Every tool needs a live connection. ConnectAsync is idempotent and cheap when
        // already connected, so this lazily (re)establishes the connection and surfaces a
        // clear, agent-readable error instead of crashing when credentials are missing.
        if (!await _dataverseService.ConnectAsync())
        {
            return new McpResponse
            {
                Id = request.Id.Value,
                Result = new ToolResult
                {
                    IsError = true,
                    Content = new List<ContentItem>
                    {
                        new() { Type = "text", Text = "Not connected to Dataverse. Verify DATAVERSE_INSTANCE_URL, DATAVERSE_CLIENT_ID and DATAVERSE_CLIENT_SECRET." }
                    }
                }
            };
        }

        try
        {
            var result = toolName switch
            {
                "whoami" => await HandleWhoAmIAsync(),
                "list_entities" => await HandleListEntitiesAsync(),
                "get_entity_metadata" => await HandleGetEntityMetadataAsync(arguments),
                "get_entity_attributes" => await HandleGetEntityAttributesAsync(arguments),
                "get_entity_relationships" => await HandleGetEntityRelationshipsAsync(arguments),
                "get_global_optionset" => await HandleGetGlobalOptionSetAsync(arguments),
                "create_record" => await HandleCreateRecordAsync(arguments),
                "get_record" => await HandleGetRecordAsync(arguments),
                "update_record" => await HandleUpdateRecordAsync(arguments),
                "delete_record" => await HandleDeleteRecordAsync(arguments),
                "query_records" => await HandleQueryRecordsAsync(arguments),
                "associate_records" => await HandleAssociateAsync(arguments),
                "disassociate_records" => await HandleDisassociateAsync(arguments),
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
            // MCP convention: surface tool failures as an error result so the agent can
            // read and react to them, instead of a protocol-level JSON-RPC error.
            _logger.LogError(ex, "Error executing tool: {ToolName}", toolName);
            return new McpResponse
            {
                Id = request.Id.Value,
                Result = new ToolResult
                {
                    IsError = true,
                    Content = new List<ContentItem>
                    {
                        new() { Type = "text", Text = $"Error executing '{toolName}': {ex.Message}" }
                    }
                }
            };
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
            .Where(a => a.AttributeType != AttributeTypeCode.Virtual || a is MultiSelectPicklistAttributeMetadata)
            .OrderBy(a => a.LogicalName)
            .Select(BuildAttributeDetail)
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

    /// <summary>
    /// Builds a rich, agent-friendly description of an attribute, including everything
    /// needed to write a value: type, required level, option-set options, lookup targets,
    /// boolean labels, string limits, number ranges and datetime behavior.
    /// </summary>
    private static object BuildAttributeDetail(AttributeMetadata a)
    {
        var detail = new Dictionary<string, object?>
        {
            ["logicalName"] = a.LogicalName,
            ["schemaName"] = a.SchemaName,
            ["displayName"] = a.DisplayName?.UserLocalizedLabel?.Label,
            ["description"] = a.Description?.UserLocalizedLabel?.Label,
            ["attributeType"] = a.AttributeType?.ToString(),
            ["isValidForCreate"] = a.IsValidForCreate,
            ["isValidForUpdate"] = a.IsValidForUpdate,
            ["isCustomAttribute"] = a.IsCustomAttribute,
            ["isPrimaryId"] = a.IsPrimaryId,
            ["isPrimaryName"] = a.IsPrimaryName,
            ["requiredLevel"] = a.RequiredLevel?.Value.ToString()
        };

        switch (a)
        {
            case MultiSelectPicklistAttributeMetadata ms:
                detail["kind"] = "multiSelectOptionSet";
                detail["options"] = MapOptions(ms.OptionSet?.Options);
                break;
            case EnumAttributeMetadata en: // Picklist, State, Status
                detail["kind"] = "optionSet";
                detail["options"] = MapOptions(en.OptionSet?.Options);
                if (en.OptionSet?.IsGlobal == true)
                    detail["globalOptionSetName"] = en.OptionSet?.Name;
                break;
            case BooleanAttributeMetadata b:
                detail["trueOption"] = new { value = b.OptionSet?.TrueOption?.Value, label = b.OptionSet?.TrueOption?.Label?.UserLocalizedLabel?.Label };
                detail["falseOption"] = new { value = b.OptionSet?.FalseOption?.Value, label = b.OptionSet?.FalseOption?.Label?.UserLocalizedLabel?.Label };
                break;
            case LookupAttributeMetadata lookup:
                detail["targets"] = lookup.Targets;
                break;
            case StringAttributeMetadata s:
                detail["maxLength"] = s.MaxLength;
                detail["format"] = s.Format?.ToString();
                break;
            case MemoAttributeMetadata memo:
                detail["maxLength"] = memo.MaxLength;
                break;
            case IntegerAttributeMetadata i:
                detail["minValue"] = i.MinValue;
                detail["maxValue"] = i.MaxValue;
                break;
            case BigIntAttributeMetadata bi:
                detail["minValue"] = bi.MinValue;
                detail["maxValue"] = bi.MaxValue;
                break;
            case DecimalAttributeMetadata dec:
                detail["minValue"] = dec.MinValue;
                detail["maxValue"] = dec.MaxValue;
                detail["precision"] = dec.Precision;
                break;
            case DoubleAttributeMetadata dbl:
                detail["minValue"] = dbl.MinValue;
                detail["maxValue"] = dbl.MaxValue;
                detail["precision"] = dbl.Precision;
                break;
            case MoneyAttributeMetadata money:
                detail["minValue"] = money.MinValue;
                detail["maxValue"] = money.MaxValue;
                detail["precision"] = money.Precision;
                break;
            case DateTimeAttributeMetadata dt:
                detail["format"] = dt.Format?.ToString();
                detail["dateTimeBehavior"] = dt.DateTimeBehavior?.Value;
                break;
        }

        return detail;
    }

    private static List<object> MapOptions(IEnumerable<OptionMetadata>? options)
    {
        if (options == null)
            return new List<object>();

        return options
            .Select(o => (object)new
            {
                value = o.Value,
                label = o.Label?.UserLocalizedLabel?.Label
            })
            .ToList();
    }

    private async Task<ToolResult> HandleGetEntityRelationshipsAsync(Dictionary<string, JsonElement>? arguments)
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

        var oneToMany = (metadata.OneToManyRelationships ?? Array.Empty<OneToManyRelationshipMetadata>())
            .OrderBy(r => r.SchemaName)
            .Select(r => new
            {
                schemaName = r.SchemaName,
                referencingEntity = r.ReferencingEntity,
                referencingAttribute = r.ReferencingAttribute,
                referencedEntity = r.ReferencedEntity,
                referencedAttribute = r.ReferencedAttribute
            })
            .ToList();

        var manyToOne = (metadata.ManyToOneRelationships ?? Array.Empty<OneToManyRelationshipMetadata>())
            .OrderBy(r => r.SchemaName)
            .Select(r => new
            {
                schemaName = r.SchemaName,
                referencingEntity = r.ReferencingEntity,
                referencingAttribute = r.ReferencingAttribute,
                referencedEntity = r.ReferencedEntity,
                referencedAttribute = r.ReferencedAttribute
            })
            .ToList();

        var manyToMany = (metadata.ManyToManyRelationships ?? Array.Empty<ManyToManyRelationshipMetadata>())
            .OrderBy(r => r.SchemaName)
            .Select(r => new
            {
                schemaName = r.SchemaName,
                entity1LogicalName = r.Entity1LogicalName,
                entity2LogicalName = r.Entity2LogicalName,
                intersectEntityName = r.IntersectEntityName
            })
            .ToList();

        var result = new { oneToMany, manyToOne, manyToMany };

        var text = JsonSerializer.Serialize(result, _jsonOptions);
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

        var text = JsonSerializer.Serialize(SerializeEntity(entity), _jsonOptions);
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

        string? orderBy = null;
        if (arguments.TryGetValue("orderBy", out var orderByElement))
        {
            orderBy = orderByElement.GetString();
        }

        bool orderDescending = false;
        if (arguments.TryGetValue("orderDescending", out var orderDescElement)
            && (orderDescElement.ValueKind == JsonValueKind.True || orderDescElement.ValueKind == JsonValueKind.False))
        {
            orderDescending = orderDescElement.GetBoolean();
        }

        var entities = await _dataverseService.QueryRecordsAsync(entityLogicalName, filter, columns, maxResults, orderBy, orderDescending);

        var records = entities.Select(SerializeEntity).ToList();

        var text = JsonSerializer.Serialize(records, _jsonOptions);
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    /// <summary>
    /// Serializes an entity into an agent-friendly object: typed attribute values plus a
    /// 'formattedValues' map with display labels (option-set labels, lookup names, etc.).
    /// </summary>
    private static Dictionary<string, object?> SerializeEntity(Entity entity)
    {
        var recordData = new Dictionary<string, object?>
        {
            ["id"] = entity.Id.ToString(),
            ["entityLogicalName"] = entity.LogicalName
        };

        foreach (var attr in entity.Attributes)
        {
            if (attr.Key == entity.LogicalName + "id")
                continue;
            recordData[attr.Key] = ConvertAttributeForOutput(attr.Value);
        }

        if (entity.FormattedValues.Count > 0)
        {
            recordData["formattedValues"] = entity.FormattedValues
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        }

        return recordData;
    }

    private static object? ConvertAttributeForOutput(object? value)
    {
        return value switch
        {
            null => null,
            Money money => money.Value,
            OptionSetValue optionSet => optionSet.Value,
            OptionSetValueCollection collection => collection.Select(o => o.Value).ToArray(),
            EntityReference entityRef => new
            {
                id = entityRef.Id,
                entity = entityRef.LogicalName,
                name = entityRef.Name
            },
            AliasedValue aliased => ConvertAttributeForOutput(aliased.Value),
            Guid guid => guid.ToString(),
            _ => value
        };
    }

    private async Task<ToolResult> HandleWhoAmIAsync()
    {
        var info = await _dataverseService.WhoAmIAsync();
        return new ToolResult
        {
            Content = new List<ContentItem>
            {
                new() { Type = "text", Text = info ?? "{}" }
            }
        };
    }

    private async Task<ToolResult> HandleGetGlobalOptionSetAsync(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("name", out var nameElement))
        {
            throw new ArgumentException("name is required");
        }

        var name = nameElement.GetString() ?? throw new ArgumentException("name cannot be null");
        var options = await _dataverseService.GetGlobalOptionSetAsync(name);

        var result = new
        {
            name,
            options = MapOptions(options)
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

    private async Task<ToolResult> HandleAssociateAsync(Dictionary<string, JsonElement>? arguments)
    {
        var (entityLogicalName, id, relationshipName, relatedEntity, relatedIds) = ParseAssociationArguments(arguments);
        await _dataverseService.AssociateAsync(entityLogicalName, id, relationshipName, relatedEntity, relatedIds);

        var result = new
        {
            entityLogicalName,
            id = id.ToString(),
            relationshipName,
            relatedEntity,
            relatedIds = relatedIds.Select(r => r.ToString()).ToArray(),
            message = "Records associated successfully"
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

    private async Task<ToolResult> HandleDisassociateAsync(Dictionary<string, JsonElement>? arguments)
    {
        var (entityLogicalName, id, relationshipName, relatedEntity, relatedIds) = ParseAssociationArguments(arguments);
        await _dataverseService.DisassociateAsync(entityLogicalName, id, relationshipName, relatedEntity, relatedIds);

        var result = new
        {
            entityLogicalName,
            id = id.ToString(),
            relationshipName,
            relatedEntity,
            relatedIds = relatedIds.Select(r => r.ToString()).ToArray(),
            message = "Records disassociated successfully"
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

    private static (string entityLogicalName, Guid id, string relationshipName, string relatedEntity, Guid[] relatedIds) ParseAssociationArguments(Dictionary<string, JsonElement>? arguments)
    {
        if (arguments == null)
            throw new ArgumentException("arguments are required");

        string GetString(string key) => arguments.TryGetValue(key, out var el)
            ? el.GetString() ?? throw new ArgumentException($"{key} cannot be null")
            : throw new ArgumentException($"{key} is required");

        var entityLogicalName = GetString("entityLogicalName");
        var relationshipName = GetString("relationshipName");
        var relatedEntity = GetString("relatedEntity");

        if (!Guid.TryParse(GetString("id"), out var id))
            throw new ArgumentException("id must be a valid GUID");

        if (!arguments.TryGetValue("relatedIds", out var relatedIdsElement) || relatedIdsElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("relatedIds is required and must be an array");

        var relatedIds = relatedIdsElement.EnumerateArray()
            .Select(e => Guid.TryParse(e.GetString(), out var g) ? g : throw new ArgumentException($"'{e.GetString()}' is not a valid GUID"))
            .ToArray();

        if (relatedIds.Length == 0)
            throw new ArgumentException("relatedIds must contain at least one GUID");

        return (entityLogicalName, id, relationshipName, relatedEntity, relatedIds);
    }

    private McpResponse CreateErrorResponse(object id, int code, string message)
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
