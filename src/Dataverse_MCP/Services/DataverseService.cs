using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace Dataverse_MCP.Services;

public class DataverseService : IDataverseService
{
    private readonly ILogger<DataverseService> _logger;
    private readonly IConfiguration _configuration;
    private ServiceClient? _serviceClient;

    public DataverseService(ILogger<DataverseService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task<bool> ConnectAsync()
    {
        try
        {
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

    public async Task<IEnumerable<EntityMetadata>> GetAllEntitiesAsync()
    {
        EnsureConnected();

        try
        {
            var request = new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = false
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

        try
        {
            var request = new RetrieveEntityRequest
            {
                LogicalName = entityLogicalName,
                EntityFilters = EntityFilters.All,
                RetrieveAsIfPublished = false
            };

            var response = await Task.Run(() => (RetrieveEntityResponse)_serviceClient!.Execute(request));
            return response.EntityMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving entity metadata for {entityLogicalName}");
            return null;
        }
    }

    public async Task<IEnumerable<AttributeMetadata>> GetEntityAttributesAsync(string entityLogicalName)
    {
        var entityMetadata = await GetEntityMetadataAsync(entityLogicalName);
        return entityMetadata?.Attributes ?? Array.Empty<AttributeMetadata>();
    }

    public async Task<IEnumerable<OneToManyRelationshipMetadata>> GetEntityRelationshipsAsync(string entityLogicalName)
    {
        var entityMetadata = await GetEntityMetadataAsync(entityLogicalName);
        return entityMetadata?.OneToManyRelationships ?? Array.Empty<OneToManyRelationshipMetadata>();
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
                ConvertEntityAttributesToPrimitives(entity);
            }
            
            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving record {id} from {entityLogicalName}");
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

    public async Task<IEnumerable<Entity>> QueryRecordsAsync(string entityLogicalName, string? filter = null, string[]? columns = null, int? maxResults = null)
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

            if (!string.IsNullOrEmpty(filter))
            {
                // Parse simple filters in format: "attributename eq value" or "attributename ne value"
                var parts = filter.Split(new[] { " eq ", " ne ", " gt ", " lt ", " ge ", " le " }, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    var attributeName = parts[0].Trim();
                    var value = parts[1].Trim().Trim('\'', '"');
                    
                    var conditionOperator = filter.Contains(" eq ") ? ConditionOperator.Equal
                        : filter.Contains(" ne ") ? ConditionOperator.NotEqual
                        : filter.Contains(" gt ") ? ConditionOperator.GreaterThan
                        : filter.Contains(" lt ") ? ConditionOperator.LessThan
                        : filter.Contains(" ge ") ? ConditionOperator.GreaterEqual
                        : filter.Contains(" le ") ? ConditionOperator.LessEqual
                        : ConditionOperator.Equal;

                    query.Criteria.AddCondition(attributeName, conditionOperator, value);
                }
            }

            var results = await Task.Run(() => _serviceClient!.RetrieveMultiple(query));
            
            // Convert complex types to primitive values
            foreach (var entity in results.Entities)
            {
                ConvertEntityAttributesToPrimitives(entity);
            }
            
            return results.Entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error querying records from {entityLogicalName}");
            throw;
        }
    }

    private async Task<object?> ConvertAttributeValueAsync(string entityLogicalName, string attributeName, object? value)
    {
        if (value == null)
            return null;

        // Handle basic conversions for JsonElement first
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            object? convertedValue = jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
                System.Text.Json.JsonValueKind.Number => jsonElement.TryGetInt32(out var intVal) ? intVal : jsonElement.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                _ => value
            };
            value = convertedValue;
        }

        if (value == null)
            return null;

        // Get attribute metadata to determine the correct type conversion
        var attributeMetadata = await GetAttributeMetadataAsync(entityLogicalName, attributeName);
        if (attributeMetadata == null)
            return value; // If we can't get metadata, return as-is

        // Handle specific attribute types that require special conversion
        return attributeMetadata.AttributeType switch
        {
            AttributeTypeCode.Lookup => ConvertToEntityReference(value, attributeMetadata),
            AttributeTypeCode.Customer => ConvertToEntityReference(value, attributeMetadata),
            AttributeTypeCode.Owner => ConvertToEntityReference(value, attributeMetadata),
            AttributeTypeCode.DateTime => ConvertToDateTime(value),
            AttributeTypeCode.Money => ConvertToMoney(value),
            AttributeTypeCode.Picklist => ConvertToOptionSetValue(value),
            AttributeTypeCode.State => ConvertToOptionSetValue(value),
            AttributeTypeCode.Status => ConvertToOptionSetValue(value),
            AttributeTypeCode.Boolean => ConvertToBoolean(value),
            AttributeTypeCode.Integer => ConvertToInteger(value),
            AttributeTypeCode.BigInt => ConvertToLong(value),
            AttributeTypeCode.Double => ConvertToDouble(value),
            AttributeTypeCode.Decimal => ConvertToDecimal(value),
            AttributeTypeCode.Uniqueidentifier => ConvertToGuid(value),
            _ => value // For other types (String, Memo, etc.), return as-is
        };
    }

    private EntityReference ConvertToEntityReference(object value, AttributeMetadata attributeMetadata)
    {
        if (value is string guidString && Guid.TryParse(guidString, out var guid))
        {
            // For lookup fields, we need to determine the target entity type
            var targetEntity = GetTargetEntityFromLookupMetadata(attributeMetadata);
            return new EntityReference(targetEntity, guid);
        }
        
        if (value is Guid guidValue)
        {
            var targetEntity = GetTargetEntityFromLookupMetadata(attributeMetadata);
            return new EntityReference(targetEntity, guidValue);
        }

        throw new ArgumentException($"Cannot convert value '{value}' to EntityReference for attribute '{attributeMetadata.LogicalName}'");
    }

    private string GetTargetEntityFromLookupMetadata(AttributeMetadata attributeMetadata)
    {
        // Try to get target entity from lookup metadata
        if (attributeMetadata is LookupAttributeMetadata lookupMetadata && lookupMetadata.Targets?.Length > 0)
        {
            return lookupMetadata.Targets[0]; // Take the first target entity
        }

        // Fallback mapping for common lookup fields
        return attributeMetadata.LogicalName switch
        {
            "msdyn_customer" => "account", // Could also be "contact", but "account" is most common
            "ownerid" => "systemuser",
            "createdby" => "systemuser",
            "modifiedby" => "systemuser",
            "transactioncurrencyid" => "transactioncurrency",
            _ => "account" // Default fallback
        };
    }

    private DateTime? ConvertToDateTime(object value)
    {
        if (value is DateTime dateTime)
            return dateTime;
            
        if (value is string dateString)
        {
            if (DateTime.TryParse(dateString, out var parsedDate))
                return parsedDate;
                
            if (DateTimeOffset.TryParse(dateString, out var parsedDateOffset))
                return parsedDateOffset.DateTime;
        }
        
        return null;
    }

    private Money? ConvertToMoney(object value)
    {
        if (value is decimal decimalValue)
            return new Money(decimalValue);
            
        if (value is double doubleValue)
            return new Money((decimal)doubleValue);
            
        if (value is string stringValue && decimal.TryParse(stringValue, out var parsedDecimal))
            return new Money(parsedDecimal);
            
        return null;
    }

    private OptionSetValue? ConvertToOptionSetValue(object value)
    {
        if (value is int intValue)
            return new OptionSetValue(intValue);
            
        if (value is string stringValue && int.TryParse(stringValue, out var parsedInt))
            return new OptionSetValue(parsedInt);
            
        return null;
    }

    private bool? ConvertToBoolean(object value)
    {
        if (value is bool boolValue)
            return boolValue;
            
        if (value is string stringValue && bool.TryParse(stringValue, out var parsedBool))
            return parsedBool;
            
        return null;
    }

    private int? ConvertToInteger(object value)
    {
        if (value is int intValue)
            return intValue;
            
        if (value is string stringValue && int.TryParse(stringValue, out var parsedInt))
            return parsedInt;
            
        return null;
    }

    private long? ConvertToLong(object value)
    {
        if (value is long longValue)
            return longValue;
            
        if (value is int intValue)
            return intValue;
            
        if (value is string stringValue && long.TryParse(stringValue, out var parsedLong))
            return parsedLong;
            
        return null;
    }

    private double? ConvertToDouble(object value)
    {
        if (value is double doubleValue)
            return doubleValue;
            
        if (value is decimal decimalValue)
            return (double)decimalValue;
            
        if (value is string stringValue && double.TryParse(stringValue, out var parsedDouble))
            return parsedDouble;
            
        return null;
    }

    private decimal? ConvertToDecimal(object value)
    {
        if (value is decimal decimalValue)
            return decimalValue;
            
        if (value is double doubleValue)
            return (decimal)doubleValue;
            
        if (value is string stringValue && decimal.TryParse(stringValue, out var parsedDecimal))
            return parsedDecimal;
            
        return null;
    }

    private Guid? ConvertToGuid(object value)
    {
        if (value is Guid guidValue)
            return guidValue;
            
        if (value is string stringValue && Guid.TryParse(stringValue, out var parsedGuid))
            return parsedGuid;
            
        return null;
    }

    /// <summary>
    /// Converts complex Dataverse attribute types to primitive values that can be serialized
    /// </summary>
    private void ConvertEntityAttributesToPrimitives(Entity entity)
    {
        var attributesToConvert = entity.Attributes.Keys.ToList();
        
        foreach (var attributeName in attributesToConvert)
        {
            var value = entity[attributeName];
            var convertedValue = ConvertDataverseValue(value);
            
            if (convertedValue != value)
            {
                entity[attributeName] = convertedValue;
            }
        }
    }

    /// <summary>
    /// Converts Dataverse complex types to primitive values
    /// </summary>
    private object? ConvertDataverseValue(object? value)
    {
        if (value == null)
            return null;

        return value switch
        {
            // Money type - return the decimal value
            Money money => money.Value,
            
            // OptionSetValue - return the integer value
            OptionSetValue optionSet => optionSet.Value,
            
            // EntityReference - return a more readable format
            EntityReference entityRef => $"{entityRef.Id}|{entityRef.LogicalName}" + (string.IsNullOrEmpty(entityRef.Name) ? "" : $"|{entityRef.Name}"),
            
            // Boolean
            bool boolValue => boolValue,
            
            // DateTime
            DateTime dateTime => dateTime,
            
            // Guid
            Guid guid => guid.ToString(),
            
            // Numeric types
            int intValue => intValue,
            long longValue => longValue,
            decimal decimalValue => decimalValue,
            double doubleValue => doubleValue,
            
            // String
            string stringValue => stringValue,
            
            // AliasedValue - extract the actual value
            AliasedValue aliasedValue => ConvertDataverseValue(aliasedValue.Value),
            
            // For any other type, return as is
            _ => value
        };
    }

    // WebResource Operations
    public async Task<IEnumerable<Entity>> ListWebResourcesAsync(int? webResourceType = null, string? nameFilter = null, int? maxResults = null)
    {
        EnsureConnected();

        try
        {
            var query = new QueryExpression("webresource")
            {
                ColumnSet = new ColumnSet("webresourceid", "name", "displayname", "webresourcetype", "modifiedon", "description")
            };

            // Filter by webresource type if specified
            // Types: 1=HTML, 2=CSS, 3=JavaScript, 4=XML, 5=PNG, 6=JPG, 7=GIF, 8=XAP, 9=XSL, 10=ICO, 11=SVG, 12=RESX
            if (webResourceType.HasValue)
            {
                query.Criteria.AddCondition("webresourcetype", ConditionOperator.Equal, webResourceType.Value);
            }

            // Filter by name pattern if specified
            if (!string.IsNullOrEmpty(nameFilter))
            {
                query.Criteria.AddCondition("name", ConditionOperator.Like, $"%{nameFilter}%");
            }

            if (maxResults.HasValue)
            {
                query.TopCount = maxResults.Value;
            }

            // Order by name
            query.AddOrder("name", OrderType.Ascending);

            var results = await Task.Run(() => _serviceClient!.RetrieveMultiple(query));
            
            // Convert complex types to primitive values
            foreach (var entity in results.Entities)
            {
                ConvertEntityAttributesToPrimitives(entity);
            }
            
            _logger.LogInformation($"Retrieved {results.Entities.Count} webresources");
            return results.Entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving webresources");
            throw;
        }
    }

    public async Task<string?> GetWebResourceContentAsync(Guid webResourceId)
    {
        EnsureConnected();

        try
        {
            var entity = await Task.Run(() => _serviceClient!.Retrieve("webresource", webResourceId, new ColumnSet("content", "name", "webresourcetype")));
            
            if (entity == null)
            {
                _logger.LogWarning($"WebResource with ID {webResourceId} not found");
                return null;
            }

            if (!entity.Contains("content") || entity["content"] == null)
            {
                _logger.LogWarning($"WebResource {webResourceId} has no content");
                return null;
            }

            var base64Content = entity["content"].ToString();
            if (string.IsNullOrEmpty(base64Content))
            {
                return string.Empty;
            }

            // Decode Base64 content
            var bytes = Convert.FromBase64String(base64Content);
            var decodedContent = System.Text.Encoding.UTF8.GetString(bytes);
            
            _logger.LogInformation($"Retrieved content for webresource {webResourceId} ({entity.GetAttributeValue<string>("name")})");
            return decodedContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving webresource content for ID {webResourceId}");
            throw;
        }
    }

    public async Task<string?> GetWebResourceContentByNameAsync(string name)
    {
        EnsureConnected();

        try
        {
            var query = new QueryExpression("webresource")
            {
                ColumnSet = new ColumnSet("webresourceid", "content", "name", "webresourcetype"),
                Criteria = new FilterExpression()
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
            query.TopCount = 1;

            var results = await Task.Run(() => _serviceClient!.RetrieveMultiple(query));
            
            if (results.Entities.Count == 0)
            {
                _logger.LogWarning($"WebResource with name '{name}' not found");
                return null;
            }

            var entity = results.Entities[0];
            var webResourceId = entity.Id;
            
            if (!entity.Contains("content") || entity["content"] == null)
            {
                _logger.LogWarning($"WebResource '{name}' has no content");
                return null;
            }

            var base64Content = entity["content"].ToString();
            if (string.IsNullOrEmpty(base64Content))
            {
                return string.Empty;
            }

            // Decode Base64 content
            var bytes = Convert.FromBase64String(base64Content);
            var decodedContent = System.Text.Encoding.UTF8.GetString(bytes);
            
            _logger.LogInformation($"Retrieved content for webresource '{name}' (ID: {webResourceId})");
            return decodedContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving webresource content for name '{name}'");
            throw;
        }
    }
}
