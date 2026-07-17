using DataverseSchema_MCP.Models;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System.Text.Json;

namespace DataverseSchema_MCP.Services;

public class DataverseSchemaService : IDataverseSchemaService
{
    private const int Lcid = 1033;

    private readonly ILogger<DataverseSchemaService> _logger;
    private readonly IConfiguration _configuration;
    private ServiceClient? _serviceClient;

    public DataverseSchemaService(ILogger<DataverseSchemaService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task<bool> ConnectAsync()
    {
        try
        {
            // Idempotent: reuse an existing live connection.
            if (_serviceClient != null && _serviceClient.IsReady)
            {
                return Task.FromResult(true);
            }

            var tenantId = _configuration["DATAVERSE_TENANT_ID"] ?? _configuration["Dataverse:TenantId"];
            var clientId = _configuration["DATAVERSE_CLIENT_ID"] ?? _configuration["Dataverse:ClientId"];
            var clientSecret = _configuration["DATAVERSE_CLIENT_SECRET"] ?? _configuration["Dataverse:ClientSecret"];
            var instanceUrl = _configuration["DATAVERSE_INSTANCE_URL"]
                ?? _configuration["Dataverse:InstanceUrl"]
                ?? _configuration["Dataverse:Environment"];

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

            _logger.LogError("Failed to connect to Dataverse: {LastError}", _serviceClient.LastError);
            return Task.FromResult(false);
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
        var response = (WhoAmIResponse)await ExecuteAsync(new WhoAmIRequest());
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

    // ---------------------------------------------------------------------
    // Entities (tables)
    // ---------------------------------------------------------------------

    public async Task<string> CreateEntityAsync(CreateEntityRequestDto request)
    {
        EnsureConnected();

        var entity = new EntityMetadata
        {
            SchemaName = request.SchemaName,
            DisplayName = BuildLabel(request.DisplayName),
            DisplayCollectionName = BuildLabel(request.DisplayCollectionName),
            OwnershipType = ParseOwnership(request.OwnershipType),
            IsActivity = false
        };

        if (!string.IsNullOrEmpty(request.Description))
            entity.Description = BuildLabel(request.Description);

        var primary = new StringAttributeMetadata
        {
            SchemaName = request.PrimaryAttributeSchemaName ?? DerivePrimarySchemaName(request.SchemaName),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            MaxLength = request.PrimaryAttributeMaxLength,
            FormatName = StringFormatName.Text,
            DisplayName = BuildLabel(request.PrimaryAttributeDisplayName)
        };

        var createRequest = new CreateEntityRequest
        {
            Entity = entity,
            PrimaryAttribute = primary,
            HasNotes = request.HasNotes,
            HasActivities = request.HasActivities
        };

        var response = (CreateEntityResponse)await ExecuteAsync(createRequest);
        _logger.LogInformation("Created entity {SchemaName} ({EntityId})", request.SchemaName, response.EntityId);
        return request.SchemaName.ToLowerInvariant();
    }

    public async Task UpdateEntityAsync(UpdateEntityRequestDto request)
    {
        EnsureConnected();

        var metadata = await RetrieveEntityMetadataAsync(request.EntityLogicalName, EntityFilters.Entity);

        if (!string.IsNullOrEmpty(request.DisplayName))
            metadata.DisplayName = BuildLabel(request.DisplayName);
        if (!string.IsNullOrEmpty(request.DisplayCollectionName))
            metadata.DisplayCollectionName = BuildLabel(request.DisplayCollectionName);
        if (!string.IsNullOrEmpty(request.Description))
            metadata.Description = BuildLabel(request.Description);

        await ExecuteAsync(new UpdateEntityRequest { Entity = metadata, MergeLabels = true });
        _logger.LogInformation("Updated entity {EntityLogicalName}", request.EntityLogicalName);
    }

    public async Task DeleteEntityAsync(string entityLogicalName)
    {
        EnsureConnected();
        await ExecuteAsync(new DeleteEntityRequest { LogicalName = entityLogicalName });
        _logger.LogInformation("Deleted entity {EntityLogicalName}", entityLogicalName);
    }

    // ---------------------------------------------------------------------
    // Attributes (columns)
    // ---------------------------------------------------------------------

    public async Task<string> CreateAttributeAsync(CreateAttributeRequestDto request)
    {
        EnsureConnected();

        var attribute = BuildAttributeMetadata(request);
        attribute.SchemaName = request.SchemaName;
        attribute.DisplayName = BuildLabel(request.DisplayName);
        attribute.RequiredLevel = new AttributeRequiredLevelManagedProperty(ParseRequiredLevel(request.RequiredLevel));
        if (!string.IsNullOrEmpty(request.Description))
            attribute.Description = BuildLabel(request.Description);

        await ExecuteAsync(new CreateAttributeRequest
        {
            EntityName = request.EntityLogicalName,
            Attribute = attribute
        });

        _logger.LogInformation("Created attribute {SchemaName} on {Entity}", request.SchemaName, request.EntityLogicalName);
        return request.SchemaName.ToLowerInvariant();
    }

    public async Task UpdateAttributeAsync(UpdateAttributeRequestDto request)
    {
        EnsureConnected();

        var response = (RetrieveAttributeResponse)await ExecuteAsync(new RetrieveAttributeRequest
        {
            EntityLogicalName = request.EntityLogicalName,
            LogicalName = request.AttributeLogicalName
        });

        var attribute = response.AttributeMetadata;

        if (!string.IsNullOrEmpty(request.DisplayName))
            attribute.DisplayName = BuildLabel(request.DisplayName);
        if (!string.IsNullOrEmpty(request.Description))
            attribute.Description = BuildLabel(request.Description);
        if (!string.IsNullOrEmpty(request.RequiredLevel))
            attribute.RequiredLevel = new AttributeRequiredLevelManagedProperty(ParseRequiredLevel(request.RequiredLevel));

        await ExecuteAsync(new UpdateAttributeRequest
        {
            EntityName = request.EntityLogicalName,
            Attribute = attribute,
            MergeLabels = true
        });

        _logger.LogInformation("Updated attribute {Attribute} on {Entity}", request.AttributeLogicalName, request.EntityLogicalName);
    }

    public async Task DeleteAttributeAsync(string entityLogicalName, string attributeLogicalName)
    {
        EnsureConnected();
        await ExecuteAsync(new DeleteAttributeRequest
        {
            EntityLogicalName = entityLogicalName,
            LogicalName = attributeLogicalName
        });
        _logger.LogInformation("Deleted attribute {Attribute} on {Entity}", attributeLogicalName, entityLogicalName);
    }

    // ---------------------------------------------------------------------
    // Option sets
    // ---------------------------------------------------------------------

    public async Task<string> CreateGlobalOptionSetAsync(CreateGlobalOptionSetRequestDto request)
    {
        EnsureConnected();

        var optionSet = new OptionSetMetadata
        {
            Name = request.Name,
            DisplayName = BuildLabel(request.DisplayName),
            IsGlobal = true,
            OptionSetType = OptionSetType.Picklist
        };

        foreach (var option in request.Options)
        {
            optionSet.Options.Add(new OptionMetadata(BuildLabel(option.Label), option.Value));
        }

        await ExecuteAsync(new CreateOptionSetRequest { OptionSet = optionSet });
        _logger.LogInformation("Created global option set {Name}", request.Name);
        return request.Name.ToLowerInvariant();
    }

    public async Task<int> InsertOptionSetValueAsync(OptionSetValueTargetDto request)
    {
        EnsureConnected();

        var insert = new InsertOptionValueRequest
        {
            Label = BuildLabel(request.Label ?? throw new ArgumentException("label is required"))
        };

        ApplyOptionSetTarget(insert, request);

        if (request.Value.HasValue)
            insert.Value = request.Value;

        var response = (InsertOptionValueResponse)await ExecuteAsync(insert);
        _logger.LogInformation("Inserted option value {Value}", response.NewOptionValue);
        return response.NewOptionValue;
    }

    public async Task DeleteOptionSetValueAsync(OptionSetValueTargetDto request)
    {
        EnsureConnected();

        var delete = new DeleteOptionValueRequest
        {
            Value = request.Value ?? throw new ArgumentException("value is required")
        };

        ApplyOptionSetTarget(delete, request);

        await ExecuteAsync(delete);
        _logger.LogInformation("Deleted option value {Value}", request.Value);
    }

    // ---------------------------------------------------------------------
    // Relationships
    // ---------------------------------------------------------------------

    public async Task<string> CreateOneToManyAsync(CreateOneToManyRequestDto request)
    {
        EnsureConnected();

        var lookup = new LookupAttributeMetadata
        {
            SchemaName = request.LookupSchemaName,
            DisplayName = BuildLabel(request.LookupDisplayName),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(ParseRequiredLevel(request.RequiredLevel))
        };
        if (!string.IsNullOrEmpty(request.LookupDescription))
            lookup.Description = BuildLabel(request.LookupDescription);

        var relationship = new OneToManyRelationshipMetadata
        {
            SchemaName = request.RelationshipSchemaName,
            ReferencedEntity = request.ReferencedEntity,
            ReferencingEntity = request.ReferencingEntity,
            AssociatedMenuConfiguration = new AssociatedMenuConfiguration
            {
                Behavior = AssociatedMenuBehavior.UseCollectionName,
                Group = AssociatedMenuGroup.Details,
                Order = 10000
            },
            CascadeConfiguration = new CascadeConfiguration
            {
                Assign = CascadeType.NoCascade,
                Delete = CascadeType.RemoveLink,
                Merge = CascadeType.NoCascade,
                Reparent = CascadeType.NoCascade,
                Share = CascadeType.NoCascade,
                Unshare = CascadeType.NoCascade
            }
        };

        await ExecuteAsync(new CreateOneToManyRequest
        {
            OneToManyRelationship = relationship,
            Lookup = lookup
        });

        _logger.LogInformation("Created 1:N relationship {Schema} ({Referenced} -> {Referencing})",
            request.RelationshipSchemaName, request.ReferencedEntity, request.ReferencingEntity);
        return request.RelationshipSchemaName.ToLowerInvariant();
    }

    public async Task<string> CreateManyToManyAsync(CreateManyToManyRequestDto request)
    {
        EnsureConnected();

        var relationship = new ManyToManyRelationshipMetadata
        {
            SchemaName = request.RelationshipSchemaName,
            Entity1LogicalName = request.Entity1LogicalName,
            Entity2LogicalName = request.Entity2LogicalName,
            Entity1AssociatedMenuConfiguration = new AssociatedMenuConfiguration
            {
                Behavior = AssociatedMenuBehavior.UseCollectionName,
                Group = AssociatedMenuGroup.Details,
                Order = 10000
            },
            Entity2AssociatedMenuConfiguration = new AssociatedMenuConfiguration
            {
                Behavior = AssociatedMenuBehavior.UseCollectionName,
                Group = AssociatedMenuGroup.Details,
                Order = 10000
            }
        };

        await ExecuteAsync(new CreateManyToManyRequest
        {
            IntersectEntitySchemaName = request.IntersectEntitySchemaName,
            ManyToManyRelationship = relationship
        });

        _logger.LogInformation("Created N:N relationship {Schema} ({E1} <-> {E2})",
            request.RelationshipSchemaName, request.Entity1LogicalName, request.Entity2LogicalName);
        return request.RelationshipSchemaName.ToLowerInvariant();
    }

    // ---------------------------------------------------------------------
    // Publishing
    // ---------------------------------------------------------------------

    public async Task PublishAllAsync()
    {
        EnsureConnected();
        await ExecuteAsync(new PublishAllXmlRequest());
        _logger.LogInformation("Published all customizations");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private AttributeMetadata BuildAttributeMetadata(CreateAttributeRequestDto request)
    {
        switch (request.AttributeType.Trim().ToLowerInvariant())
        {
            case "string":
                return new StringAttributeMetadata
                {
                    MaxLength = request.MaxLength ?? 100,
                    FormatName = ParseStringFormat(request.StringFormat)
                };
            case "memo":
                return new MemoAttributeMetadata
                {
                    MaxLength = request.MaxLength ?? 2000,
                    Format = StringFormat.TextArea
                };
            case "integer":
                return new IntegerAttributeMetadata
                {
                    MinValue = request.MinValue.HasValue ? (int)request.MinValue.Value : int.MinValue,
                    MaxValue = request.MaxValue.HasValue ? (int)request.MaxValue.Value : int.MaxValue,
                    Format = IntegerFormat.None
                };
            case "bigint":
                return new BigIntAttributeMetadata();
            case "decimal":
                return new DecimalAttributeMetadata
                {
                    MinValue = request.MinValue.HasValue ? (decimal)request.MinValue.Value : -100000000000m,
                    MaxValue = request.MaxValue.HasValue ? (decimal)request.MaxValue.Value : 100000000000m,
                    Precision = request.Precision ?? 2
                };
            case "double":
                return new DoubleAttributeMetadata
                {
                    MinValue = request.MinValue ?? -100000000000d,
                    MaxValue = request.MaxValue ?? 100000000000d,
                    Precision = request.Precision ?? 2
                };
            case "money":
                return new MoneyAttributeMetadata
                {
                    MinValue = request.MinValue ?? 0d,
                    MaxValue = request.MaxValue ?? 1000000000000d,
                    Precision = request.Precision ?? 2
                };
            case "boolean":
                return new BooleanAttributeMetadata
                {
                    OptionSet = new BooleanOptionSetMetadata(
                        new OptionMetadata(BuildLabel(request.TrueLabel), 1),
                        new OptionMetadata(BuildLabel(request.FalseLabel), 0))
                };
            case "datetime":
                return new DateTimeAttributeMetadata
                {
                    Format = ParseDateTimeFormat(request.DateTimeFormat),
                    DateTimeBehavior = DateTimeBehavior.UserLocal
                };
            case "picklist":
                return new PicklistAttributeMetadata { OptionSet = BuildOptionSet(request) };
            case "multiselectpicklist":
                return new MultiSelectPicklistAttributeMetadata { OptionSet = BuildOptionSet(request) };
            default:
                throw new ArgumentException($"Unsupported attributeType '{request.AttributeType}'. Supported: String, Memo, Integer, BigInt, Decimal, Double, Money, Boolean, DateTime, Picklist, MultiSelectPicklist.");
        }
    }

    private OptionSetMetadata BuildOptionSet(CreateAttributeRequestDto request)
    {
        // Bind to an existing global option set.
        if (!string.IsNullOrEmpty(request.GlobalOptionSetName))
        {
            return new OptionSetMetadata
            {
                IsGlobal = true,
                Name = request.GlobalOptionSetName,
                OptionSetType = OptionSetType.Picklist
            };
        }

        var optionSet = new OptionSetMetadata
        {
            IsGlobal = false,
            OptionSetType = OptionSetType.Picklist
        };

        if (request.Options != null)
        {
            foreach (var option in request.Options)
            {
                optionSet.Options.Add(new OptionMetadata(BuildLabel(option.Label), option.Value));
            }
        }

        return optionSet;
    }

    private void ApplyOptionSetTarget(OrganizationRequest request, OptionSetValueTargetDto target)
    {
        if (!string.IsNullOrEmpty(target.GlobalOptionSetName))
        {
            request["OptionSetName"] = target.GlobalOptionSetName;
        }
        else if (!string.IsNullOrEmpty(target.EntityLogicalName) && !string.IsNullOrEmpty(target.AttributeLogicalName))
        {
            request["EntityLogicalName"] = target.EntityLogicalName;
            request["AttributeLogicalName"] = target.AttributeLogicalName;
        }
        else
        {
            throw new ArgumentException("Specify either globalOptionSetName, or both entityLogicalName and attributeLogicalName.");
        }
    }

    private async Task<EntityMetadata> RetrieveEntityMetadataAsync(string entityLogicalName, EntityFilters filters)
    {
        var response = (RetrieveEntityResponse)await ExecuteAsync(new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = filters,
            RetrieveAsIfPublished = true
        });
        return response.EntityMetadata;
    }

    private Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request)
        => Task.Run(() => _serviceClient!.Execute(request));

    private static Label BuildLabel(string text) => new(text, Lcid);

    private static string DerivePrimarySchemaName(string entitySchemaName)
    {
        var prefix = entitySchemaName.Contains('_') ? entitySchemaName.Split('_')[0] : "new";
        return $"{prefix}_name";
    }

    private static OwnershipTypes ParseOwnership(string ownershipType)
        => ownershipType.Trim().ToLowerInvariant() switch
        {
            "organizationowned" or "organization" or "org" => OwnershipTypes.OrganizationOwned,
            _ => OwnershipTypes.UserOwned
        };

    private static AttributeRequiredLevel ParseRequiredLevel(string requiredLevel)
        => requiredLevel.Trim().ToLowerInvariant() switch
        {
            "applicationrequired" or "required" => AttributeRequiredLevel.ApplicationRequired,
            "recommended" => AttributeRequiredLevel.Recommended,
            _ => AttributeRequiredLevel.None
        };

    private static StringFormatName ParseStringFormat(string? format)
        => (format?.Trim().ToLowerInvariant()) switch
        {
            "email" => StringFormatName.Email,
            "url" => StringFormatName.Url,
            "phone" => StringFormatName.Phone,
            "textarea" => StringFormatName.TextArea,
            _ => StringFormatName.Text
        };

    private static DateTimeFormat ParseDateTimeFormat(string? format)
        => (format?.Trim().ToLowerInvariant()) switch
        {
            "dateonly" or "date" => DateTimeFormat.DateOnly,
            _ => DateTimeFormat.DateAndTime
        };

    private void EnsureConnected()
    {
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Not connected to Dataverse. Call ConnectAsync first.");
        }
    }
}
