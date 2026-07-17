using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Dataverse_MCP.Services;

public class DataverseService : IDataverseService
{
    private readonly ILogger<DataverseService> _logger;
    private readonly IConfiguration _configuration;
    private ServiceClient? _serviceClient;

    // Metadata is expensive to retrieve, so cache it per entity for the lifetime
    // of the process. Creating a record with N attributes used to trigger N full
    // entity-metadata retrievals; with the cache it triggers at most one.
    private readonly ConcurrentDictionary<string, EntityMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);

    public DataverseService(ILogger<DataverseService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task<bool> ConnectAsync()
    {
        try
        {
            // Idempotent: if we already have a live connection, reuse it.
            if (_serviceClient != null && _serviceClient.IsReady)
            {
                return Task.FromResult(true);
            }

            // First try environment variables (for MCP server configuration)
            var tenantId = _configuration["DATAVERSE_TENANT_ID"];
            var clientId = _configuration["DATAVERSE_CLIENT_ID"];
            var clientSecret = _configuration["DATAVERSE_CLIENT_SECRET"];
            var instanceUrl = _configuration["DATAVERSE_INSTANCE_URL"];
            
            // Fallback to configuration section (for appsettings.json)
            if (string.IsNullOrEmpty(tenantId))
                tenantId = _configuration["Dataverse:TenantId"];
            if (string.IsNullOrEmpty(clientId))
                clientId = _configuration["Dataverse:ClientId"];
            if (string.IsNullOrEmpty(clientSecret))
                clientSecret = _configuration["Dataverse:ClientSecret"];
            if (string.IsNullOrEmpty(instanceUrl))
                instanceUrl = _configuration["Dataverse:InstanceUrl"] ?? _configuration["Dataverse:Environment"];
            
            if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogError("Dataverse connection configuration is missing. Required: DATAVERSE_INSTANCE_URL, DATAVERSE_CLIENT_ID, DATAVERSE_CLIENT_SECRET");
                return Task.FromResult(false);
            }
            
            var connectionString = $"AuthType=ClientSecret;Url={instanceUrl};ClientId={clientId};ClientSecret={clientSecret}";

            _serviceClient = new ServiceClient(connectionString);
            
            if (_serviceClient.IsReady)
            {
                _logger.LogInformation("Successfully connected to Dataverse at {InstanceUrl}", instanceUrl);
                return Task.FromResult(true);
            }
            else
            {
                _logger.LogError($"Failed to connect to Dataverse: {_serviceClient.LastError}");
                return Task.FromResult(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Dataverse");
            return Task.FromResult(false);
        }
    }

    public async Task<string?> WhoAmIAsync()
    {
        EnsureConnected();

        try
        {
            var response = await Task.Run(() => (WhoAmIResponse)_serviceClient!.Execute(new WhoAmIRequest()));
            var info = new
            {
                userId = response.UserId,
                businessUnitId = response.BusinessUnitId,
                organizationId = response.OrganizationId,
                connectedOrgUriActual = _serviceClient!.ConnectedOrgUriActual?.ToString(),
                connectedOrgFriendlyName = _serviceClient!.ConnectedOrgFriendlyName
            };
            return JsonSerializer.Serialize(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing WhoAmI");
            throw;
        }
    }

    public async Task<IEnumerable<EntityMetadata>> GetAllEntitiesAsync()
    {
        EnsureConnected();

        try
        {
            var request = new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };

            var response = await Task.Run(() => (RetrieveAllEntitiesResponse)_serviceClient!.Execute(request));
            return response.EntityMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all entities");
            throw;
        }
    }

    public async Task<EntityMetadata?> GetEntityMetadataAsync(string entityLogicalName)
    {
        EnsureConnected();

        if (_metadataCache.TryGetValue(entityLogicalName, out var cached))
        {
            return cached;
        }

        try
        {
            var request = new RetrieveEntityRequest
            {
                LogicalName = entityLogicalName,
                EntityFilters = EntityFilters.All,
                RetrieveAsIfPublished = true
            };

            var response = await Task.Run(() => (RetrieveEntityResponse)_serviceClient!.Execute(request));
            if (response.EntityMetadata != null)
            {
                _metadataCache[entityLogicalName] = response.EntityMetadata;
            }
            return response.EntityMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving entity metadata for {EntityLogicalName}", entityLogicalName);
            return null;
        }
    }

    public async Task<IEnumerable<AttributeMetadata>> GetEntityAttributesAsync(string entityLogicalName)
    {
        var entityMetadata = await GetEntityMetadataAsync(entityLogicalName);
        return entityMetadata?.Attributes ?? Array.Empty<AttributeMetadata>();
    }

    public async Task<OptionMetadata[]?> GetGlobalOptionSetAsync(string optionSetName)
    {
        EnsureConnected();

        try
        {
            var request = new RetrieveOptionSetRequest { Name = optionSetName };
            var response = await Task.Run(() => (RetrieveOptionSetResponse)_serviceClient!.Execute(request));
            return (response.OptionSetMetadata as OptionSetMetadata)?.Options.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving global option set {OptionSetName}", optionSetName);
            throw;
        }
    }

    public async Task<AttributeMetadata?> GetAttributeMetadataAsync(string entityLogicalName, string attributeLogicalName)
    {
        var entityMetadata = await GetEntityMetadataAsync(entityLogicalName);
        return entityMetadata?.Attributes?.FirstOrDefault(a => a.LogicalName == attributeLogicalName);
    }

    private void EnsureConnected()
    {
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Not connected to Dataverse. Call ConnectAsync first.");
        }
    }

    // CRUD Operations
    public async Task<Guid> CreateRecordAsync(string entityLogicalName, Dictionary<string, object> attributes)
    {
        EnsureConnected();

        try
        {
            var entity = new Entity(entityLogicalName);
            foreach (var attr in attributes)
            {
                entity[attr.Key] = await ConvertAttributeValueAsync(entityLogicalName, attr.Key, attr.Value);
            }

            var id = await Task.Run(() => _serviceClient!.Create(entity));
            _logger.LogInformation($"Created record in {entityLogicalName} with ID: {id}");
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating record in {entityLogicalName}");
            throw;
        }
    }

    public async Task<Entity?> GetRecordAsync(string entityLogicalName, Guid id, string[]? columns = null)
    {
        EnsureConnected();

        try
        {
            var columnSet = columns != null && columns.Length > 0
                ? new ColumnSet(columns)
                : new ColumnSet(true);

            var entity = await Task.Run(() => _serviceClient!.Retrieve(entityLogicalName, id, columnSet));
            
            if (entity != null)
            {
                // Raw entity is returned so callers can access EntityReference,
                // OptionSetValue and FormattedValues. Serialization happens in the
                // MCP layer.
                return entity;
            }

            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving record {Id} from {EntityLogicalName}", id, entityLogicalName);
            return null;
        }
    }

    public async Task UpdateRecordAsync(string entityLogicalName, Guid id, Dictionary<string, object> attributes)
    {
        EnsureConnected();

        try
        {
            var entity = new Entity(entityLogicalName)
            {
                Id = id
            };

            foreach (var attr in attributes)
            {
                entity[attr.Key] = await ConvertAttributeValueAsync(entityLogicalName, attr.Key, attr.Value);
            }

            await Task.Run(() => _serviceClient!.Update(entity));
            _logger.LogInformation($"Updated record {id} in {entityLogicalName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating record {id} in {entityLogicalName}");
            throw;
        }
    }

    public async Task DeleteRecordAsync(string entityLogicalName, Guid id)
    {
        EnsureConnected();

        try
        {
            await Task.Run(() => _serviceClient!.Delete(entityLogicalName, id));
            _logger.LogInformation($"Deleted record {id} from {entityLogicalName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting record {id} from {entityLogicalName}");
            throw;
        }
    }

    public async Task<IEnumerable<Entity>> QueryRecordsAsync(
        string entityLogicalName,
        string? filter = null,
        string[]? columns = null,
        int? maxResults = null,
        string? orderBy = null,
        bool orderDescending = false)
    {
        EnsureConnected();

        try
        {
            var query = new QueryExpression(entityLogicalName)
            {
                ColumnSet = columns != null && columns.Length > 0
                    ? new ColumnSet(columns)
                    : new ColumnSet(true)
            };

            if (maxResults.HasValue)
            {
                query.TopCount = maxResults.Value;
            }

            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                query.AddOrder(orderBy, orderDescending ? OrderType.Descending : OrderType.Ascending);
            }

            if (!string.IsNullOrWhiteSpace(filter))
            {
                await ApplyFilterAsync(query, entityLogicalName, filter!);
            }

            var results = await Task.Run(() => _serviceClient!.RetrieveMultiple(query));
            return results.Entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying records from {EntityLogicalName}", entityLogicalName);
            throw;
        }
    }

    public async Task AssociateAsync(string entityLogicalName, Guid id, string relationshipName, string relatedEntity, Guid[] relatedIds)
    {
        EnsureConnected();

        try
        {
            var relatedReferences = new EntityReferenceCollection(
                relatedIds.Select(r => new EntityReference(relatedEntity, r)).ToList());

            await Task.Run(() => _serviceClient!.Associate(
                entityLogicalName, id, new Relationship(relationshipName), relatedReferences));

            _logger.LogInformation("Associated {Count} {RelatedEntity} record(s) to {EntityLogicalName} {Id} via {Relationship}",
                relatedIds.Length, relatedEntity, entityLogicalName, id, relationshipName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error associating records via {Relationship}", relationshipName);
            throw;
        }
    }

    public async Task DisassociateAsync(string entityLogicalName, Guid id, string relationshipName, string relatedEntity, Guid[] relatedIds)
    {
        EnsureConnected();

        try
        {
            var relatedReferences = new EntityReferenceCollection(
                relatedIds.Select(r => new EntityReference(relatedEntity, r)).ToList());

            await Task.Run(() => _serviceClient!.Disassociate(
                entityLogicalName, id, new Relationship(relationshipName), relatedReferences));

            _logger.LogInformation("Disassociated {Count} {RelatedEntity} record(s) from {EntityLogicalName} {Id} via {Relationship}",
                relatedIds.Length, relatedEntity, entityLogicalName, id, relationshipName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disassociating records via {Relationship}", relationshipName);
            throw;
        }
    }

    /// <summary>
    /// Parses a textual filter into a <see cref="FilterExpression"/>. Supports multiple
    /// conditions joined by " and " or " or " (not mixed), the operators
    /// eq/ne/gt/lt/ge/le/like/contains and the unary "null"/"notnull" checks. Values are
    /// coerced to the attribute's real type using metadata so that option sets, numbers,
    /// booleans, dates and lookups filter correctly.
    /// </summary>
    private async Task ApplyFilterAsync(QueryExpression query, string entityLogicalName, string filter)
    {
        var hasOr = filter.Contains(" or ", StringComparison.OrdinalIgnoreCase);
        var hasAnd = filter.Contains(" and ", StringComparison.OrdinalIgnoreCase);

        string[] conditionStrings;
        if (hasOr && !hasAnd)
        {
            query.Criteria.FilterOperator = LogicalOperator.Or;
            conditionStrings = filter.Split(new[] { " or ", " OR " }, StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            query.Criteria.FilterOperator = LogicalOperator.And;
            conditionStrings = filter.Split(new[] { " and ", " AND " }, StringSplitOptions.RemoveEmptyEntries);
        }

        foreach (var raw in conditionStrings)
        {
            var condition = await ParseConditionAsync(entityLogicalName, raw.Trim());
            if (condition != null)
            {
                query.Criteria.AddCondition(condition);
            }
        }
    }

    private async Task<ConditionExpression?> ParseConditionAsync(string entityLogicalName, string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return null;

        // Unary operators: "attr null" / "attr notnull" / "attr is null"
        var trimmed = condition.Trim();
        if (trimmed.EndsWith(" notnull", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" not null", StringComparison.OrdinalIgnoreCase))
        {
            var attr = trimmed.Split(' ')[0];
            return new ConditionExpression(attr, ConditionOperator.NotNull);
        }
        if (trimmed.EndsWith(" null", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(" is null", StringComparison.OrdinalIgnoreCase))
        {
            var attr = trimmed.Split(' ')[0];
            return new ConditionExpression(attr, ConditionOperator.Null);
        }

        var operators = new (string token, ConditionOperator op)[]
        {
            (" eq ", ConditionOperator.Equal),
            (" ne ", ConditionOperator.NotEqual),
            (" ge ", ConditionOperator.GreaterEqual),
            (" le ", ConditionOperator.LessEqual),
            (" gt ", ConditionOperator.GreaterThan),
            (" lt ", ConditionOperator.LessThan),
            (" like ", ConditionOperator.Like),
            (" contains ", ConditionOperator.Like)
        };

        foreach (var (token, op) in operators)
        {
            var idx = trimmed.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            var attributeName = trimmed.Substring(0, idx).Trim();
            var rawValue = trimmed.Substring(idx + token.Length).Trim().Trim('\'', '"');

            if (token == " contains ")
            {
                rawValue = $"%{rawValue}%";
            }
            else if (op == ConditionOperator.Like && !rawValue.Contains('%'))
            {
                rawValue = $"%{rawValue}%";
            }

            object convertedValue = op == ConditionOperator.Like
                ? rawValue
                : await ConvertFilterValueAsync(entityLogicalName, attributeName, rawValue);

            return new ConditionExpression(attributeName, op, convertedValue);
        }

        return null;
    }

    private async Task<object> ConvertFilterValueAsync(string entityLogicalName, string attributeName, string rawValue)
    {
        var metadata = await GetAttributeMetadataAsync(entityLogicalName, attributeName);
        if (metadata == null)
            return rawValue;

        return metadata.AttributeType switch
        {
            AttributeTypeCode.Picklist or AttributeTypeCode.State or AttributeTypeCode.Status
                => ResolveOptionValue(rawValue, metadata) ?? (object)rawValue,
            AttributeTypeCode.Boolean => bool.TryParse(rawValue, out var b) ? b : rawValue,
            AttributeTypeCode.Integer => int.TryParse(rawValue, out var i) ? i : rawValue,
            AttributeTypeCode.BigInt => long.TryParse(rawValue, out var l) ? l : rawValue,
            AttributeTypeCode.Double => double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : rawValue,
            AttributeTypeCode.Decimal or AttributeTypeCode.Money => decimal.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var m) ? m : rawValue,
            AttributeTypeCode.DateTime => DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt) ? dt : rawValue,
            AttributeTypeCode.Lookup or AttributeTypeCode.Customer or AttributeTypeCode.Owner or AttributeTypeCode.Uniqueidentifier
                => Guid.TryParse(rawValue, out var g) ? g : rawValue,
            _ => rawValue
        };
    }

    private async Task<object?> ConvertAttributeValueAsync(string entityLogicalName, string attributeName, object? value)
    {
        if (value == null)
            return null;

        // Normalize JsonElement scalars up front. Objects/arrays are intentionally kept
        // as JsonElement so that lookups ({entity,id}/{entity,name}) and multi-select
        // option sets ([1,2,3]) can be interpreted using metadata below.
        if (value is JsonElement jsonElement)
        {
            switch (jsonElement.ValueKind)
            {
                case JsonValueKind.String:
                    value = jsonElement.GetString() ?? string.Empty;
                    break;
                case JsonValueKind.Number:
                    value = jsonElement.TryGetInt32(out var intVal) ? intVal : jsonElement.GetDouble();
                    break;
                case JsonValueKind.True:
                    value = true;
                    break;
                case JsonValueKind.False:
                    value = false;
                    break;
                case JsonValueKind.Null:
                    return null;
                    // Object / Array: keep the JsonElement as-is.
            }
        }

        if (value == null)
            return null;

        var attributeMetadata = await GetAttributeMetadataAsync(entityLogicalName, attributeName);
        if (attributeMetadata == null)
            return value; // If we can't get metadata, return as-is

        switch (attributeMetadata.AttributeType)
        {
            case AttributeTypeCode.Lookup:
            case AttributeTypeCode.Customer:
            case AttributeTypeCode.Owner:
                return await ConvertToEntityReferenceAsync(value, attributeMetadata);
            case AttributeTypeCode.DateTime:
                return ConvertToDateTime(value);
            case AttributeTypeCode.Money:
                return ConvertToMoney(value);
            case AttributeTypeCode.Picklist:
            case AttributeTypeCode.State:
            case AttributeTypeCode.Status:
                var option = ResolveOptionValue(value, attributeMetadata);
                return option.HasValue ? new OptionSetValue(option.Value) : null;
            case AttributeTypeCode.Virtual:
                // Multi-select option sets are exposed as Virtual attributes.
                if (attributeMetadata is MultiSelectPicklistAttributeMetadata)
                    return ConvertToOptionSetValueCollection(value, attributeMetadata);
                return value;
            case AttributeTypeCode.Boolean:
                return ConvertToBoolean(value, attributeMetadata);
            case AttributeTypeCode.Integer:
                return ConvertToInteger(value);
            case AttributeTypeCode.BigInt:
                return ConvertToLong(value);
            case AttributeTypeCode.Double:
                return ConvertToDouble(value);
            case AttributeTypeCode.Decimal:
                return ConvertToDecimal(value);
            case AttributeTypeCode.Uniqueidentifier:
                return ConvertToGuid(value);
            default:
                return value; // String, Memo, etc.
        }
    }

    private async Task<EntityReference?> ConvertToEntityReferenceAsync(object value, AttributeMetadata attributeMetadata)
    {
        var targets = (attributeMetadata as LookupAttributeMetadata)?.Targets ?? Array.Empty<string>();

        // Structured input: { "entity": "contact", "id": "guid" } or { "entity": "contact", "name": "John" }
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            string? targetEntity = null;
            if (je.TryGetProperty("entity", out var entProp)
                || je.TryGetProperty("logicalName", out entProp)
                || je.TryGetProperty("entityLogicalName", out entProp))
            {
                targetEntity = entProp.GetString();
            }
            targetEntity ??= targets.Length == 1 ? targets[0] : null;

            if ((je.TryGetProperty("id", out var idProp) || je.TryGetProperty("value", out idProp))
                && Guid.TryParse(idProp.GetString(), out var gid))
            {
                if (string.IsNullOrEmpty(targetEntity))
                    throw new ArgumentException($"Lookup '{attributeMetadata.LogicalName}' is polymorphic; specify 'entity'. Targets: {string.Join(", ", targets)}");
                return new EntityReference(targetEntity, gid);
            }

            if (je.TryGetProperty("name", out var nameProp))
            {
                var name = nameProp.GetString();
                if (!string.IsNullOrEmpty(name))
                {
                    if (string.IsNullOrEmpty(targetEntity))
                        throw new ArgumentException($"Lookup '{attributeMetadata.LogicalName}' is polymorphic; specify 'entity' when resolving by name. Targets: {string.Join(", ", targets)}");
                    return await ResolveEntityReferenceByNameAsync(targetEntity!, name!);
                }
            }

            throw new ArgumentException($"Lookup object for '{attributeMetadata.LogicalName}' must include 'id' or 'name'.");
        }

        if (value is string s)
        {
            if (Guid.TryParse(s, out var guid))
            {
                if (targets.Length == 0)
                    throw new ArgumentException($"Cannot resolve target entity for lookup '{attributeMetadata.LogicalName}'.");
                if (targets.Length > 1)
                    _logger.LogWarning("Lookup {Attr} is polymorphic ({Targets}); defaulting to {Target}. Pass an object {{ entity, id }} to be explicit.",
                        attributeMetadata.LogicalName, string.Join(",", targets), targets[0]);
                return new EntityReference(targets[0], guid);
            }

            if (targets.Length == 1)
                return await ResolveEntityReferenceByNameAsync(targets[0], s);

            throw new ArgumentException($"Value '{s}' is not a GUID and lookup '{attributeMetadata.LogicalName}' has multiple targets; pass {{ entity, name }}.");
        }

        if (value is Guid g)
        {
            if (targets.Length == 0)
                throw new ArgumentException($"Cannot resolve target entity for lookup '{attributeMetadata.LogicalName}'.");
            return new EntityReference(targets[0], g);
        }

        throw new ArgumentException($"Cannot convert value '{value}' to EntityReference for attribute '{attributeMetadata.LogicalName}'.");
    }

    private async Task<EntityReference> ResolveEntityReferenceByNameAsync(string entityLogicalName, string name)
    {
        var meta = await GetEntityMetadataAsync(entityLogicalName);
        var primaryName = meta?.PrimaryNameAttribute ?? "name";

        var query = new QueryExpression(entityLogicalName)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 2
        };
        query.Criteria.AddCondition(primaryName, ConditionOperator.Equal, name);

        var result = await Task.Run(() => _serviceClient!.RetrieveMultiple(query));
        if (result.Entities.Count == 0)
            throw new ArgumentException($"No '{entityLogicalName}' record found with {primaryName} = '{name}'.");
        if (result.Entities.Count > 1)
            throw new ArgumentException($"Multiple '{entityLogicalName}' records match {primaryName} = '{name}'. Use an id instead.");

        return new EntityReference(entityLogicalName, result.Entities[0].Id);
    }

    private static IEnumerable<OptionMetadata> GetOptions(AttributeMetadata metadata)
    {
        return metadata switch
        {
            MultiSelectPicklistAttributeMetadata ms => ms.OptionSet?.Options ?? Enumerable.Empty<OptionMetadata>(),
            EnumAttributeMetadata en => en.OptionSet?.Options ?? Enumerable.Empty<OptionMetadata>(),
            _ => Enumerable.Empty<OptionMetadata>()
        };
    }

    private int? ResolveOptionValue(object value, AttributeMetadata metadata)
    {
        if (value is int i) return i;
        if (value is long l) return (int)l;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var ji)) return ji;
            if (je.ValueKind == JsonValueKind.String) value = je.GetString() ?? string.Empty;
        }

        if (value is string s)
        {
            if (int.TryParse(s, out var parsed)) return parsed;

            var match = GetOptions(metadata)
                .FirstOrDefault(o => string.Equals(o.Label?.UserLocalizedLabel?.Label, s, StringComparison.OrdinalIgnoreCase));
            if (match?.Value != null) return match.Value;

            throw new ArgumentException($"Option label '{s}' not found for attribute '{metadata.LogicalName}'.");
        }

        return null;
    }

    private OptionSetValueCollection? ConvertToOptionSetValueCollection(object value, AttributeMetadata metadata)
    {
        var values = new List<int>();

        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in je.EnumerateArray())
            {
                var v = ResolveOptionValue(element, metadata);
                if (v.HasValue) values.Add(v.Value);
            }
        }
        else if (value is string s)
        {
            foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var v = ResolveOptionValue(part, metadata);
                if (v.HasValue) values.Add(v.Value);
            }
        }
        else
        {
            var single = ResolveOptionValue(value, metadata);
            if (single.HasValue) values.Add(single.Value);
        }

        return values.Count == 0
            ? null
            : new OptionSetValueCollection(values.Select(v => new OptionSetValue(v)).ToList());
    }

    private DateTime? ConvertToDateTime(object value)
    {
        if (value is DateTime dateTime)
            return dateTime;

        if (value is string dateString)
        {
            if (DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedDate))
                return parsedDate;

            if (DateTimeOffset.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDateOffset))
                return parsedDateOffset.UtcDateTime;
        }

        return null;
    }

    private Money? ConvertToMoney(object value)
    {
        if (value is decimal decimalValue) return new Money(decimalValue);
        if (value is double doubleValue) return new Money((decimal)doubleValue);
        if (value is int intValue) return new Money(intValue);
        if (value is long longValue) return new Money(longValue);
        if (value is string stringValue && decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDecimal))
            return new Money(parsedDecimal);
        return null;
    }

    private bool? ConvertToBoolean(object value, AttributeMetadata metadata)
    {
        if (value is bool boolValue) return boolValue;
        if (value is int intValue) return intValue != 0;
        if (value is long longValue) return longValue != 0;

        if (value is string stringValue)
        {
            if (bool.TryParse(stringValue, out var parsedBool)) return parsedBool;
            if (int.TryParse(stringValue, out var parsedInt)) return parsedInt != 0;

            if (metadata is BooleanAttributeMetadata boolMeta)
            {
                if (string.Equals(boolMeta.OptionSet?.TrueOption?.Label?.UserLocalizedLabel?.Label, stringValue, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(boolMeta.OptionSet?.FalseOption?.Label?.UserLocalizedLabel?.Label, stringValue, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return null;
    }

    private int? ConvertToInteger(object value)
    {
        if (value is int intValue) return intValue;
        if (value is long longValue) return (int)longValue;
        if (value is double doubleValue) return (int)doubleValue;
        if (value is string stringValue && int.TryParse(stringValue, out var parsedInt)) return parsedInt;
        return null;
    }

    private long? ConvertToLong(object value)
    {
        if (value is long longValue) return longValue;
        if (value is int intValue) return intValue;
        if (value is double doubleValue) return (long)doubleValue;
        if (value is string stringValue && long.TryParse(stringValue, out var parsedLong)) return parsedLong;
        return null;
    }

    private double? ConvertToDouble(object value)
    {
        if (value is double doubleValue) return doubleValue;
        if (value is decimal decimalValue) return (double)decimalValue;
        if (value is int intValue) return intValue;
        if (value is long longValue) return longValue;
        if (value is string stringValue && double.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDouble))
            return parsedDouble;
        return null;
    }

    private decimal? ConvertToDecimal(object value)
    {
        if (value is decimal decimalValue) return decimalValue;
        if (value is double doubleValue) return (decimal)doubleValue;
        if (value is int intValue) return intValue;
        if (value is long longValue) return longValue;
        if (value is string stringValue && decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDecimal))
            return parsedDecimal;
        return null;
    }

    private Guid? ConvertToGuid(object value)
    {
        if (value is Guid guidValue) return guidValue;
        if (value is string stringValue && Guid.TryParse(stringValue, out var parsedGuid)) return parsedGuid;
        return null;
    }
}
