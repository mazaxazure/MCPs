using System.Text.Json;
using Microsoft.Extensions.Logging;
using DataverseSchema_MCP.Models;
using DataverseSchema_MCP.Services;

namespace DataverseSchema_MCP.Mcp;

public class McpServer
{
    private readonly ILogger<McpServer> _logger;
    private readonly IDataverseSchemaService _schemaService;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonSerializerOptions _dtoOptions;

    public McpServer(ILogger<McpServer> logger, IDataverseSchemaService schemaService)
    {
        _logger = logger;
        _schemaService = schemaService;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        _dtoOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting Dataverse Schema MCP Server");

        // Attempt an initial connection but keep serving handshake/metadata requests even
        // if it fails; tool calls reconnect lazily and report a clear error.
        if (!await _schemaService.ConnectAsync())
        {
            _logger.LogWarning("Initial Dataverse connection failed. The server will keep serving handshake requests and retry connecting on the first tool call.");
        }

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

                _logger.LogDebug("Received request: {Line}", line);

                var request = JsonSerializer.Deserialize<McpRequest>(line, _jsonOptions);
                if (request != null)
                {
                    var response = await HandleRequestAsync(request);
                    if (response != null)
                    {
                        var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                        _logger.LogDebug("Sending response: {ResponseJson}", responseJson);
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
            _logger.LogError(ex, "Error handling method: {Method}", request.Method);
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
                    name = "dataverse-schema-mcp",
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

        object StringProp(string description) => new { type = "string", description };
        object BoolProp(string description) => new { type = "boolean", description };
        object NumberProp(string description) => new { type = "number", description };

        var tools = new List<ToolInfo>
        {
            new()
            {
                Name = "whoami",
                Description = "Returns the identity and organization the server is connected to (WhoAmI).",
                InputSchema = new { type = "object", properties = new Dictionary<string, object>(), required = Array.Empty<string>() }
            },
            new()
            {
                Name = "create_entity",
                Description = "Creates a new table (entity) with its primary name attribute. schemaName must include the publisher prefix (e.g., 'new_project').",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["schemaName"] = StringProp("Schema name including publisher prefix, e.g. 'new_project'"),
                        ["displayName"] = StringProp("Singular display name, e.g. 'Project'"),
                        ["displayCollectionName"] = StringProp("Plural display name, e.g. 'Projects'"),
                        ["description"] = StringProp("Optional description"),
                        ["ownershipType"] = StringProp("UserOwned (default) or OrganizationOwned"),
                        ["primaryAttributeSchemaName"] = StringProp("Optional. Defaults to '<prefix>_name'"),
                        ["primaryAttributeDisplayName"] = StringProp("Display name of the primary attribute. Default 'Name'"),
                        ["primaryAttributeMaxLength"] = NumberProp("Max length of the primary attribute. Default 100"),
                        ["hasNotes"] = BoolProp("Enable notes/annotations. Default false"),
                        ["hasActivities"] = BoolProp("Enable activities. Default false")
                    },
                    required = new[] { "schemaName", "displayName", "displayCollectionName" }
                }
            },
            new()
            {
                Name = "update_entity",
                Description = "Updates display name, plural name or description of an existing table.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = StringProp("Logical name of the table"),
                        ["displayName"] = StringProp("Optional new singular display name"),
                        ["displayCollectionName"] = StringProp("Optional new plural display name"),
                        ["description"] = StringProp("Optional new description")
                    },
                    required = new[] { "entityLogicalName" }
                }
            },
            new()
            {
                Name = "delete_entity",
                Description = "Deletes a table (entity). This is destructive and removes all its data.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = StringProp("Logical name of the table to delete")
                    },
                    required = new[] { "entityLogicalName" }
                }
            },
            new()
            {
                Name = "create_attribute",
                Description = "Creates a column (attribute) of a given type on a table. attributeType: String, Memo, Integer, BigInt, Decimal, Double, Money, Boolean, DateTime, Picklist, MultiSelectPicklist. For Picklist/MultiSelectPicklist provide 'options' (array of { value?, label }) or bind to an existing global set with 'globalOptionSetName'. Lookups are created with create_one_to_many_relationship instead.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = StringProp("Logical name of the table"),
                        ["attributeType"] = StringProp("String|Memo|Integer|BigInt|Decimal|Double|Money|Boolean|DateTime|Picklist|MultiSelectPicklist"),
                        ["schemaName"] = StringProp("Schema name including prefix, e.g. 'new_amount'"),
                        ["displayName"] = StringProp("Display name of the column"),
                        ["description"] = StringProp("Optional description"),
                        ["requiredLevel"] = StringProp("None (default) | Recommended | ApplicationRequired"),
                        ["maxLength"] = NumberProp("For String/Memo"),
                        ["minValue"] = NumberProp("For numeric types"),
                        ["maxValue"] = NumberProp("For numeric types"),
                        ["precision"] = NumberProp("For Decimal/Double/Money"),
                        ["stringFormat"] = StringProp("For String: Text|Email|Url|Phone|TextArea"),
                        ["dateTimeFormat"] = StringProp("For DateTime: DateOnly|DateAndTime"),
                        ["options"] = new
                        {
                            type = "array",
                            description = "For Picklist/MultiSelectPicklist: options to create",
                            items = new
                            {
                                type = "object",
                                properties = new Dictionary<string, object>
                                {
                                    ["value"] = NumberProp("Optional explicit option value"),
                                    ["label"] = StringProp("Option label")
                                }
                            }
                        },
                        ["globalOptionSetName"] = StringProp("Bind Picklist/MultiSelect to an existing global option set"),
                        ["trueLabel"] = StringProp("For Boolean: label of the true option. Default 'Yes'"),
                        ["falseLabel"] = StringProp("For Boolean: label of the false option. Default 'No'")
                    },
                    required = new[] { "entityLogicalName", "attributeType", "schemaName", "displayName" }
                }
            },
            new()
            {
                Name = "update_attribute",
                Description = "Updates display name, description or required level of an existing column.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = StringProp("Logical name of the table"),
                        ["attributeLogicalName"] = StringProp("Logical name of the column"),
                        ["displayName"] = StringProp("Optional new display name"),
                        ["description"] = StringProp("Optional new description"),
                        ["requiredLevel"] = StringProp("Optional: None | Recommended | ApplicationRequired")
                    },
                    required = new[] { "entityLogicalName", "attributeLogicalName" }
                }
            },
            new()
            {
                Name = "delete_attribute",
                Description = "Deletes a column (attribute) from a table.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entityLogicalName"] = StringProp("Logical name of the table"),
                        ["attributeLogicalName"] = StringProp("Logical name of the column to delete")
                    },
                    required = new[] { "entityLogicalName", "attributeLogicalName" }
                }
            },
            new()
            {
                Name = "create_global_optionset",
                Description = "Creates a global option set (choice) that can be reused across attributes.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["name"] = StringProp("Schema name including prefix, e.g. 'new_status'"),
                        ["displayName"] = StringProp("Display name"),
                        ["isMultiSelect"] = BoolProp("Whether it is intended for multi-select attributes"),
                        ["options"] = new
                        {
                            type = "array",
                            description = "Options to create",
                            items = new
                            {
                                type = "object",
                                properties = new Dictionary<string, object>
                                {
                                    ["value"] = NumberProp("Optional explicit option value"),
                                    ["label"] = StringProp("Option label")
                                }
                            }
                        }
                    },
                    required = new[] { "name", "displayName", "options" }
                }
            },
            new()
            {
                Name = "insert_optionset_value",
                Description = "Adds an option to an option set. Target either a global set (globalOptionSetName) or a local one (entityLogicalName + attributeLogicalName).",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["globalOptionSetName"] = StringProp("For a global option set"),
                        ["entityLogicalName"] = StringProp("For a local option set (with attributeLogicalName)"),
                        ["attributeLogicalName"] = StringProp("For a local option set (with entityLogicalName)"),
                        ["label"] = StringProp("Label of the new option"),
                        ["value"] = NumberProp("Optional explicit value")
                    },
                    required = new[] { "label" }
                }
            },
            new()
            {
                Name = "delete_optionset_value",
                Description = "Removes an option (by value) from an option set. Target either a global or a local one.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["globalOptionSetName"] = StringProp("For a global option set"),
                        ["entityLogicalName"] = StringProp("For a local option set (with attributeLogicalName)"),
                        ["attributeLogicalName"] = StringProp("For a local option set (with entityLogicalName)"),
                        ["value"] = NumberProp("The option value to delete")
                    },
                    required = new[] { "value" }
                }
            },
            new()
            {
                Name = "create_one_to_many_relationship",
                Description = "Creates a 1:N relationship, i.e. a lookup column on the 'referencing' (many) table pointing to the 'referenced' (one) table.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["referencedEntity"] = StringProp("The 'one' side (lookup target), e.g. 'account'"),
                        ["referencingEntity"] = StringProp("The 'many' side that gets the lookup column, e.g. 'new_project'"),
                        ["lookupSchemaName"] = StringProp("Schema name of the lookup column, e.g. 'new_accountid'"),
                        ["lookupDisplayName"] = StringProp("Display name of the lookup column"),
                        ["lookupDescription"] = StringProp("Optional description"),
                        ["relationshipSchemaName"] = StringProp("Relationship schema name, e.g. 'new_account_new_project'"),
                        ["requiredLevel"] = StringProp("None (default) | Recommended | ApplicationRequired")
                    },
                    required = new[] { "referencedEntity", "referencingEntity", "lookupSchemaName", "lookupDisplayName", "relationshipSchemaName" }
                }
            },
            new()
            {
                Name = "create_many_to_many_relationship",
                Description = "Creates an N:N relationship between two tables, generating the intersect entity.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["entity1LogicalName"] = StringProp("First table logical name"),
                        ["entity2LogicalName"] = StringProp("Second table logical name"),
                        ["relationshipSchemaName"] = StringProp("Relationship schema name, e.g. 'new_project_new_tag'"),
                        ["intersectEntitySchemaName"] = StringProp("Intersect entity schema name, e.g. 'new_project_tag'")
                    },
                    required = new[] { "entity1LogicalName", "entity2LogicalName", "relationshipSchemaName", "intersectEntitySchemaName" }
                }
            },
            new()
            {
                Name = "publish_all",
                Description = "Publishes all customizations. Call after creating/updating metadata to make changes effective.",
                InputSchema = new { type = "object", properties = new Dictionary<string, object>(), required = Array.Empty<string>() }
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
        var argumentsJson = request.Params.TryGetValue("arguments", out var argsObj)
            ? (argsObj?.ToString() ?? "{}")
            : "{}";

        // Every tool needs a live connection. ConnectAsync is idempotent and cheap when
        // already connected, so this lazily (re)establishes the connection and returns a
        // clear, agent-readable error when credentials are missing.
        if (!await _schemaService.ConnectAsync())
        {
            return ToolError(request.Id.Value, "Not connected to Dataverse. Verify DATAVERSE_INSTANCE_URL, DATAVERSE_CLIENT_ID and DATAVERSE_CLIENT_SECRET.");
        }

        try
        {
            var result = toolName switch
            {
                "whoami" => await HandleWhoAmIAsync(),
                "create_entity" => await HandleCreateEntityAsync(argumentsJson),
                "update_entity" => await HandleUpdateEntityAsync(argumentsJson),
                "delete_entity" => await HandleDeleteEntityAsync(argumentsJson),
                "create_attribute" => await HandleCreateAttributeAsync(argumentsJson),
                "update_attribute" => await HandleUpdateAttributeAsync(argumentsJson),
                "delete_attribute" => await HandleDeleteAttributeAsync(argumentsJson),
                "create_global_optionset" => await HandleCreateGlobalOptionSetAsync(argumentsJson),
                "insert_optionset_value" => await HandleInsertOptionSetValueAsync(argumentsJson),
                "delete_optionset_value" => await HandleDeleteOptionSetValueAsync(argumentsJson),
                "create_one_to_many_relationship" => await HandleCreateOneToManyAsync(argumentsJson),
                "create_many_to_many_relationship" => await HandleCreateManyToManyAsync(argumentsJson),
                "publish_all" => await HandlePublishAllAsync(),
                _ => throw new Exception($"Unknown tool: {toolName}")
            };

            return new McpResponse { Id = request.Id.Value, Result = result };
        }
        catch (Exception ex)
        {
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

    private async Task<ToolResult> HandleWhoAmIAsync()
    {
        var info = await _schemaService.WhoAmIAsync();
        return TextResult(info ?? "{}");
    }

    private async Task<ToolResult> HandleCreateEntityAsync(string argumentsJson)
    {
        var dto = Deserialize<CreateEntityRequestDto>(argumentsJson);
        var logicalName = await _schemaService.CreateEntityAsync(dto);
        return JsonResult(new { entityLogicalName = logicalName, message = "Entity created successfully. Call publish_all to make it effective." });
    }

    private async Task<ToolResult> HandleUpdateEntityAsync(string argumentsJson)
    {
        var dto = Deserialize<UpdateEntityRequestDto>(argumentsJson);
        await _schemaService.UpdateEntityAsync(dto);
        return JsonResult(new { entityLogicalName = dto.EntityLogicalName, message = "Entity updated successfully" });
    }

    private async Task<ToolResult> HandleDeleteEntityAsync(string argumentsJson)
    {
        var dto = Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        var name = GetRequiredString(dto, "entityLogicalName");
        await _schemaService.DeleteEntityAsync(name);
        return JsonResult(new { entityLogicalName = name, message = "Entity deleted successfully" });
    }

    private async Task<ToolResult> HandleCreateAttributeAsync(string argumentsJson)
    {
        var dto = Deserialize<CreateAttributeRequestDto>(argumentsJson);
        var logicalName = await _schemaService.CreateAttributeAsync(dto);
        return JsonResult(new { entityLogicalName = dto.EntityLogicalName, attributeLogicalName = logicalName, message = "Attribute created successfully. Call publish_all to make it effective." });
    }

    private async Task<ToolResult> HandleUpdateAttributeAsync(string argumentsJson)
    {
        var dto = Deserialize<UpdateAttributeRequestDto>(argumentsJson);
        await _schemaService.UpdateAttributeAsync(dto);
        return JsonResult(new { dto.EntityLogicalName, dto.AttributeLogicalName, message = "Attribute updated successfully" });
    }

    private async Task<ToolResult> HandleDeleteAttributeAsync(string argumentsJson)
    {
        var dto = Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        var entity = GetRequiredString(dto, "entityLogicalName");
        var attribute = GetRequiredString(dto, "attributeLogicalName");
        await _schemaService.DeleteAttributeAsync(entity, attribute);
        return JsonResult(new { entityLogicalName = entity, attributeLogicalName = attribute, message = "Attribute deleted successfully" });
    }

    private async Task<ToolResult> HandleCreateGlobalOptionSetAsync(string argumentsJson)
    {
        var dto = Deserialize<CreateGlobalOptionSetRequestDto>(argumentsJson);
        var name = await _schemaService.CreateGlobalOptionSetAsync(dto);
        return JsonResult(new { optionSetName = name, message = "Global option set created successfully" });
    }

    private async Task<ToolResult> HandleInsertOptionSetValueAsync(string argumentsJson)
    {
        var dto = Deserialize<OptionSetValueTargetDto>(argumentsJson);
        var value = await _schemaService.InsertOptionSetValueAsync(dto);
        return JsonResult(new { value, message = "Option value inserted successfully" });
    }

    private async Task<ToolResult> HandleDeleteOptionSetValueAsync(string argumentsJson)
    {
        var dto = Deserialize<OptionSetValueTargetDto>(argumentsJson);
        await _schemaService.DeleteOptionSetValueAsync(dto);
        return JsonResult(new { dto.Value, message = "Option value deleted successfully" });
    }

    private async Task<ToolResult> HandleCreateOneToManyAsync(string argumentsJson)
    {
        var dto = Deserialize<CreateOneToManyRequestDto>(argumentsJson);
        var schema = await _schemaService.CreateOneToManyAsync(dto);
        return JsonResult(new { relationshipSchemaName = schema, message = "1:N relationship (lookup) created successfully. Call publish_all to make it effective." });
    }

    private async Task<ToolResult> HandleCreateManyToManyAsync(string argumentsJson)
    {
        var dto = Deserialize<CreateManyToManyRequestDto>(argumentsJson);
        var schema = await _schemaService.CreateManyToManyAsync(dto);
        return JsonResult(new { relationshipSchemaName = schema, message = "N:N relationship created successfully. Call publish_all to make it effective." });
    }

    private async Task<ToolResult> HandlePublishAllAsync()
    {
        await _schemaService.PublishAllAsync();
        return JsonResult(new { message = "All customizations published successfully" });
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private T Deserialize<T>(string argumentsJson)
    {
        var dto = JsonSerializer.Deserialize<T>(argumentsJson, _dtoOptions);
        if (dto == null)
            throw new ArgumentException("Invalid or empty arguments.");
        return dto;
    }

    private static string GetRequiredString(Dictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"{key} is required");
        return element.GetString() ?? throw new ArgumentException($"{key} cannot be null");
    }

    private ToolResult JsonResult(object payload)
        => TextResult(JsonSerializer.Serialize(payload, _jsonOptions));

    private static ToolResult TextResult(string text)
        => new() { Content = new List<ContentItem> { new() { Type = "text", Text = text } } };

    private static McpResponse ToolError(object id, string message)
        => new()
        {
            Id = id,
            Result = new ToolResult
            {
                IsError = true,
                Content = new List<ContentItem> { new() { Type = "text", Text = message } }
            }
        };

    private McpResponse CreateErrorResponse(object id, int code, string message)
        => new()
        {
            Id = id,
            Error = new McpError { Code = code, Message = message }
        };
}
