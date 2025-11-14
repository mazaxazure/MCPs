using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using ProjectOpsTestData_MCP.Models;
using Bogus;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Globalization;

namespace ProjectOpsTestData_MCP.Services;

public static class StringExtensions
{
    public static string ToTitleCase(this string input)
    {
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
    }
}

public class ProjectOpsTestDataService : IProjectOpsTestDataService
{
    private readonly ILogger<ProjectOpsTestDataService> _logger;
    private readonly IConfiguration _configuration;
    private ServiceClient? _serviceClient;
    private readonly List<Guid> _createdProjects = new();
    private readonly List<Guid> _createdTasks = new();

    private static readonly string[] Industries = {
        "Technology", "Manufacturing", "Financial Services", "Healthcare",
        "Construction", "Retail", "Energy", "Transportation", "Education",
        "Professional Services", "Real Estate", "Media", "Agriculture",
        "Pharmaceuticals", "Aerospace", "Automotive", "Telecommunications"
    };

    private static readonly string[] Countries = {
        "España", "México", "Argentina", "Colombia", "Chile", "Perú",
        "Venezuela", "Ecuador", "Bolivia", "Uruguay", "Paraguay",
        "Estados Unidos", "Canadá", "Reino Unido", "Francia", "Alemania", "Italia"
    };

    public ProjectOpsTestDataService(
        ILogger<ProjectOpsTestDataService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task<bool> ConnectAsync()
    {
        try
        {
            // First try environment variables (for MCP server configuration)
            var tenantId = Environment.GetEnvironmentVariable("DATAVERSE_TENANT_ID");
            var clientId = Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_SECRET");
            var instanceUrl = Environment.GetEnvironmentVariable("DATAVERSE_INSTANCE_URL");
            
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

    private void EnsureConnected()
    {
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Not connected to Dataverse. Call ConnectAsync first.");
        }
    }

    // Metadata operations (copied from working Dataverse_MCP)
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

    // CRUD Operations (copied from working Dataverse_MCP)
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

        // Special handling for currency fields
        if (attributeMetadata.LogicalName == "transactioncurrencyid" && value is string currencyCode)
        {
            return GetCurrencyEntityReference(currencyCode);
        }

        throw new ArgumentException($"Cannot convert value '{value}' to EntityReference for attribute '{attributeMetadata.LogicalName}'");
    }

    private string GetTargetEntityFromLookupMetadata(AttributeMetadata attributeMetadata)
    {
        try
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
                "contactid" => "contact",
                "accountid" => "account",
                "userid" => "systemuser",
                _ => "account" // Default fallback
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting target entity for attribute {attributeMetadata?.LogicalName}");
            return "account"; // Safe fallback
        }
    }

    private EntityReference GetCurrencyEntityReference(string currencyCode)
    {
        try
        {
            // Query for currency by ISO code
            var query = new FetchExpression($@"
                <fetch top='1'>
                    <entity name='transactioncurrency'>
                        <attribute name='transactioncurrencyid'/>
                        <filter type='and'>
                            <condition attribute='isocurrencycode' operator='eq' value='{currencyCode}'/>
                        </filter>
                    </entity>
                </fetch>");

            var result = _serviceClient?.RetrieveMultiple(query);
            if (result?.Entities?.Count > 0)
            {
                return result.Entities[0].ToEntityReference();
            }
            
            // If not found, try with USD as fallback
            if (currencyCode != "USD")
            {
                return GetCurrencyEntityReference("USD");
            }
            
            // If USD also not found, return a default reference (should not happen in normal circumstances)
            return new EntityReference("transactioncurrency", Guid.NewGuid());
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Failed to retrieve currency {currencyCode}: {ex.Message}");
            // Fallback to USD or default
            if (currencyCode != "USD")
            {
                try
                {
                    return GetCurrencyEntityReference("USD");
                }
                catch
                {
                    // Ultimate fallback
                    return new EntityReference("transactioncurrency", Guid.NewGuid());
                }
            }
            return new EntityReference("transactioncurrency", Guid.NewGuid());
        }
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

    // Test data generation methods (existing functionality)
    public Task<List<AccountTestData>> GenerateAccountTestDataAsync(int count = 20)
    {
        var faker = new Faker<AccountTestData>()
            .RuleFor(a => a.Name, f => f.Company.CompanyName())
            .RuleFor(a => a.PrimaryContact, f => f.Person.FullName)
            .RuleFor(a => a.EmailAddress, f => f.Internet.Email())
            .RuleFor(a => a.PhoneNumber, f => f.Phone.PhoneNumber())
            .RuleFor(a => a.WebsiteUrl, f => f.Internet.Url())
            .RuleFor(a => a.Industry, f => f.PickRandom(Industries))
            .RuleFor(a => a.City, f => f.Address.City())
            .RuleFor(a => a.Country, f => f.PickRandom(Countries))
            .RuleFor(a => a.Revenue, f => f.Random.Decimal(1000000, 50000000))
            .RuleFor(a => a.NumberOfEmployees, f => f.Random.Int(50, 5000));

        return Task.FromResult(faker.Generate(count));
    }

    public Task<List<ResourceTestData>> GenerateResourceTestDataAsync(int count = 15)
    {
        var faker = new Faker<ResourceTestData>()
            .RuleFor(r => r.Name, f => f.Person.FullName)
            .RuleFor(r => r.ResourceType, f => f.PickRandom("User", "Equipment", "Facility"))
            .RuleFor(r => r.CostPrice, f => f.Random.Decimal(50, 200))
            .RuleFor(r => r.SalesPrice, f => f.Random.Decimal(60, 250));

        return Task.FromResult(faker.Generate(count));
    }

    public Task<List<ProjectTestData>> GenerateProjectTestDataAsync(TestDataGenerationOptions options)
    {
        var faker = new Faker<ProjectTestData>()
            .RuleFor(p => p.Name, f => $"{f.Hacker.Adjective().ToTitleCase()} {f.Commerce.ProductName()} Project")
            .RuleFor(p => p.Description, f => f.Lorem.Sentences(2))
            .RuleFor(p => p.ScheduledStart, f => f.Date.Between(DateTime.Now.AddDays(-90), DateTime.Now.AddDays(30)))
            .RuleFor(p => p.ScheduledEnd, (f, p) => p.ScheduledStart?.AddDays(f.Random.Int(30, 365)))
            .RuleFor(p => p.Currency, f => f.PickRandom("EUR", "USD", "GBP"))
            .RuleFor(p => p.Tasks, (f, p) => GenerateTaskTestData(options.TasksPerProject, p.ScheduledStart ?? DateTime.Now, p.ScheduledEnd ?? DateTime.Now.AddDays(365)));

        return Task.FromResult(faker.Generate(options.ProjectCount));
    }

    private List<TaskTestData> GenerateTaskTestData(int count, DateTime projectStart, DateTime projectEnd)
    {
        var faker = new Faker<TaskTestData>()
            .RuleFor(t => t.Subject, f => $"{f.Hacker.Verb().ToTitleCase()} {f.Commerce.ProductMaterial()}")
            .RuleFor(t => t.Description, f => f.Lorem.Sentence())
            .RuleFor(t => t.ScheduledStart, f => f.Date.Between(projectStart, projectEnd.AddDays(-7)))
            .RuleFor(t => t.ScheduledEnd, (f, t) => t.ScheduledStart?.AddDays(f.Random.Int(1, 14)))
            .RuleFor(t => t.Effort, f => f.Random.Double(8, 80))
            .RuleFor(t => t.ProgressPercent, f => f.Random.Int(0, 100));

        return faker.Generate(count);
    }

    // Test data creation methods with metadata validation
    public async Task<string> CreateAccountsAsync(List<AccountTestData> accounts)
    {
        var created = 0;
        var failed = 0;

        // First, verify the account entity metadata
        var entityMetadata = await GetEntityMetadataAsync("account");
        if (entityMetadata == null)
        {
            return $"Failed to retrieve metadata for account entity. Created: {created}, Failed: {accounts.Count}";
        }

        foreach (var account in accounts)
        {
            try
            {
                var attributes = new Dictionary<string, object>
                {
                    ["name"] = account.Name,
                    ["emailaddress1"] = account.EmailAddress,
                    ["telephone1"] = account.PhoneNumber,
                    ["websiteurl"] = account.WebsiteUrl,
                    ["industrycode"] = GetIndustryCode(account.Industry),
                    ["address1_city"] = account.City,
                    ["address1_country"] = account.Country,
                    ["revenue"] = account.Revenue,
                    ["numberofemployees"] = account.NumberOfEmployees
                };

                await CreateRecordAsync("account", attributes);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create account: {account.Name}");
                failed++;
            }
        }

        return $"Account creation completed. Created: {created}, Failed: {failed}";
    }

    public async Task<string> CreateResourcesAsync(List<ResourceTestData> resources)
    {
        var created = 0;
        var failed = 0;

        // First, verify the bookableresource entity metadata
        var entityMetadata = await GetEntityMetadataAsync("bookableresource");
        if (entityMetadata == null)
        {
            return $"Failed to retrieve metadata for bookableresource entity. Created: {created}, Failed: {resources.Count}";
        }

        foreach (var resource in resources)
        {
            try
            {
                var attributes = new Dictionary<string, object>
                {
                    ["name"] = resource.Name,
                    ["msdyn_resourcetype"] = GetResourceTypeCode(resource.ResourceType),
                    ["msdyn_costprice"] = resource.CostPrice ?? 0,
                    ["msdyn_salesprice"] = resource.SalesPrice ?? 0
                };

                await CreateRecordAsync("bookableresource", attributes);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create resource: {resource.Name}");
                failed++;
            }
        }

        return $"Resource creation completed. Created: {created}, Failed: {failed}";
    }

    public async Task<string> CreateProjectsAsync(List<ProjectTestData> projects)
    {
        var created = 0;
        var failed = 0;

        // First, verify the msdyn_project entity metadata
        var entityMetadata = await GetEntityMetadataAsync("msdyn_project");
        if (entityMetadata == null)
        {
            return $"Failed to retrieve metadata for msdyn_project entity. Created: {created}, Failed: {projects.Count}";
        }

        foreach (var project in projects)
        {
            try
            {
                var attributes = new Dictionary<string, object>
                {
                    ["msdyn_subject"] = project.Name,
                    ["msdyn_description"] = project.Description ?? string.Empty,
                    ["msdyn_scheduledstart"] = project.ScheduledStart ?? DateTime.Now,
                    ["msdyn_finish"] = project.ScheduledEnd ?? DateTime.Now.AddDays(365),
                    ["transactioncurrencyid"] = project.Currency
                };

                var projectId = await CreateRecordAsync("msdyn_project", attributes);
                _createdProjects.Add(projectId);
                created++;
                
                // Create associated tasks using the Dynamics 365 action
                await CreateProjectTasksUsingDynamics365ActionAsync(projectId, project.Tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create project: {project.Name}");
                failed++;
            }
        }

        return $"Project creation completed. Created: {created}, Failed: {failed}";
    }

    private Entity GetDefaultProjectBucket(EntityReference projectReference)
    {
        try
        {
            var columnsToFetch = new ColumnSet("msdyn_project", "msdyn_name");
            var getDefaultBucket = new QueryExpression("msdyn_projectbucket")
            {
                ColumnSet = columnsToFetch,
                Criteria = 
                {
                    Conditions = 
                    {
                        new ConditionExpression("msdyn_project", ConditionOperator.Equal, projectReference.Id),
                        new ConditionExpression("msdyn_name", ConditionOperator.Equal, "Bucket 1")
                    }
                }
            };

            var bucketCollection = _serviceClient!.RetrieveMultiple(getDefaultBucket);
            if (bucketCollection.Entities.Count > 0)
            {
                return bucketCollection[0].ToEntity<Entity>();
            }
            
            // If no default bucket found, try to get any bucket for this project
            var getAnyBucket = new QueryExpression("msdyn_projectbucket")
            {
                ColumnSet = columnsToFetch,
                Criteria = 
                {
                    Conditions = 
                    {
                        new ConditionExpression("msdyn_project", ConditionOperator.Equal, projectReference.Id)
                    }
                }
            };

            var anyBucketCollection = _serviceClient!.RetrieveMultiple(getAnyBucket);
            if (anyBucketCollection.Entities.Count > 0)
            {
                return anyBucketCollection[0].ToEntity<Entity>();
            }
            
            // If no bucket exists, create one manually
            var bucket = new Entity("msdyn_projectbucket", Guid.NewGuid());
            bucket.Attributes.Add("msdyn_project", projectReference);
            bucket.Attributes.Add("msdyn_name", "Bucket 1");
            
            var bucketId = _serviceClient!.Create(bucket);
            bucket.Id = bucketId;
            
            return bucket;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting default bucket for project {projectReference.Id}");
            throw;
        }
    }

    public async Task<(int SuccessCount, int FailedCount)> CreateProjectTasksUsingDynamics365ActionAsync(Guid projectId, List<TaskTestData> tasks)
    {
        var successCount = 0;
        var failedCount = 0;

        try
        {
            EnsureConnected();

            // Step 1: Create an OperationSet
            var operationSetRequest = new OrganizationRequest("msdyn_CreateOperationSetV1");
            operationSetRequest.Parameters.Add("ProjectId", projectId.ToString());
            operationSetRequest.Parameters.Add("Description", $"Create tasks for project {projectId} - {DateTime.Now}");

            var operationSetResponse = await Task.Run(() => _serviceClient!.Execute(operationSetRequest));
            var operationSetId = operationSetResponse.Results["OperationSetId"].ToString();

            _logger.LogInformation($"Created OperationSet with ID: {operationSetId}");

            // Step 2: Get project bucket for tasks
            var projectReference = new EntityReference("msdyn_project", projectId);
            var bucket = GetDefaultProjectBucket(projectReference);

            // Step 3: Create tasks within the OperationSet
            foreach (var taskData in tasks)
            {
                try
                {
                    // Create task entity according to Microsoft documentation
                    var taskEntity = new Entity("msdyn_projecttask", Guid.NewGuid());
                    taskEntity.Attributes.Add("msdyn_project", projectReference);
                    taskEntity.Attributes.Add("msdyn_subject", taskData.Subject);
                    taskEntity.Attributes.Add("msdyn_description", taskData.Description ?? string.Empty);
                    taskEntity.Attributes.Add("msdyn_effort", Math.Round(taskData.Effort ?? 40.0, 2)); // Max 2 decimal precision
                    taskEntity.Attributes.Add("msdyn_scheduledstart", taskData.ScheduledStart ?? DateTime.Now);
                    taskEntity.Attributes.Add("msdyn_finish", taskData.ScheduledEnd ?? DateTime.Now.AddDays(7));
                    taskEntity.Attributes.Add("msdyn_start", taskData.ScheduledStart ?? DateTime.Now);
                    taskEntity.Attributes.Add("msdyn_projectbucket", bucket.ToEntityReference());
                    taskEntity.Attributes.Add("msdyn_outlinelevel", 1);
                    taskEntity.Attributes.Add("msdyn_LinkStatus", new OptionSetValue(192350000)); // Required field

                    // Create task using msdyn_PssCreateV1 within OperationSet
                    var createTaskRequest = new OrganizationRequest("msdyn_PssCreateV1");
                    createTaskRequest.Parameters.Add("Entity", taskEntity);
                    createTaskRequest.Parameters.Add("OperationSetId", operationSetId);

                    var taskResponse = await Task.Run(() => _serviceClient!.Execute(createTaskRequest));
                    _logger.LogInformation($"Added task to OperationSet: {taskData.Subject}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to add task to OperationSet: {taskData.Subject}");
                    failedCount++;
                }
            }

            // Step 4: Execute the OperationSet
            var executeRequest = new OrganizationRequest("msdyn_ExecuteOperationSetV1");
            executeRequest.Parameters.Add("OperationSetId", operationSetId);

            var executeResponse = await Task.Run(() => _serviceClient!.Execute(executeRequest));
            successCount = tasks.Count - failedCount;

            _logger.LogInformation($"Executed OperationSet {operationSetId}. Success: {successCount}, Failed: {failedCount}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateProjectTasksUsingDynamics365ActionAsync");
            failedCount = tasks.Count;
        }

        return (successCount, failedCount);
    }

    public async Task<string> CreateCompleteTestDataSetAsync(TestDataGenerationOptions options)
    {
        var results = new List<string>();

        // Generate and create accounts
        var accounts = await GenerateAccountTestDataAsync(options.AccountCount);
        var accountResult = await CreateAccountsAsync(accounts);
        results.Add($"Accounts: {accountResult}");

        // Generate and create resources
        var resources = await GenerateResourceTestDataAsync(options.ResourceCount);
        var resourceResult = await CreateResourcesAsync(resources);
        results.Add($"Resources: {resourceResult}");

        // Generate and create projects with tasks
        var projects = await GenerateProjectTestDataAsync(options);
        var projectResult = await CreateProjectsAsync(projects);
        results.Add($"Projects: {projectResult}");

        return string.Join("; ", results);
    }

    public async Task<string> DeleteAllTestDataAsync()
    {
        var deletedProjects = 0;
        var deletedTasks = 0;

        try
        {
            // Delete tasks first
            foreach (var taskId in _createdTasks)
            {
                try
                {
                    await DeleteRecordAsync("msdyn_projecttask", taskId);
                    deletedTasks++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to delete task {taskId}");
                }
            }

            // Delete projects
            foreach (var projectId in _createdProjects)
            {
                try
                {
                    await DeleteRecordAsync("msdyn_project", projectId);
                    deletedProjects++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to delete project {projectId}");
                }
            }

            _createdProjects.Clear();
            _createdTasks.Clear();

            return $"Deleted {deletedProjects} projects and {deletedTasks} tasks";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting test data");
            return "Error occurred during deletion";
        }
    }

    public async Task<TestDataStatistics> GetTestDataStatisticsAsync()
    {
        try
        {
            EnsureConnected();

            var accounts = await QueryRecordsAsync("account", columns: new[] { "accountid" });
            var resources = await QueryRecordsAsync("bookableresource", columns: new[] { "bookableresourceid" });
            var projects = await QueryRecordsAsync("msdyn_project", columns: new[] { "msdyn_projectid" });
            var tasks = await QueryRecordsAsync("msdyn_projecttask", columns: new[] { "msdyn_projecttaskid" });

            return new TestDataStatistics
            {
                Accounts = accounts.Count(),
                Resources = resources.Count(),
                Projects = projects.Count(),
                ProjectTasks = tasks.Count(),
                LastUpdated = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting statistics");
            return new TestDataStatistics
            {
                Accounts = 0,
                Resources = 0,
                Projects = 0,
                ProjectTasks = 0,
                LastUpdated = DateTime.Now
            };
        }
    }

    // Helper methods for option set values
    private int GetIndustryCode(string industry)
    {
        return industry switch
        {
            "Technology" => 11,
            "Manufacturing" => 6,
            "Financial Services" => 1,
            "Healthcare" => 3,
            "Construction" => 21,
            "Retail" => 16,
            "Energy" => 10,
            "Transportation" => 9,
            "Education" => 13,
            _ => 1 // Default to Financial Services
        };
    }

    private int GetResourceTypeCode(string resourceType)
    {
        return resourceType switch
        {
            "User" => 690970000,
            "Equipment" => 690970001,
            "Facility" => 690970002,
            _ => 690970000 // Default to User
        };
    }

    private int GetProjectStatusCode(string status)
    {
        return status switch
        {
            "Active" => 192350000,
            "Planning" => 192350001,
            "On Hold" => 192350002,
            _ => 192350001 // Default to Planning
        };
    }
}