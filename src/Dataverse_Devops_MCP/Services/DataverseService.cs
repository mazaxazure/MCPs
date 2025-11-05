using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse_Devops_MCP.Services;

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
                entity[attr.Key] = ConvertAttributeValue(attr.Value);
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
                entity[attr.Key] = ConvertAttributeValue(attr.Value);
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

    private object? ConvertAttributeValue(object value)
    {
        // Handle basic conversions
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
                System.Text.Json.JsonValueKind.Number => jsonElement.TryGetInt32(out var intVal) ? intVal : jsonElement.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                _ => value
            };
        }

        return value;
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
            
            // EntityReference - return a dictionary with id, name, and logical name
            EntityReference entityRef => new Dictionary<string, object?>
            {
                ["id"] = entityRef.Id.ToString(),
                ["name"] = entityRef.Name,
                ["logicalName"] = entityRef.LogicalName
            },
            
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
}
