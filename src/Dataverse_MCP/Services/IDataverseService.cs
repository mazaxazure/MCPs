using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace Dataverse_MCP.Services;

public interface IDataverseService
{
    Task<bool> ConnectAsync();
    Task<string?> WhoAmIAsync();

    // Metadata
    Task<IEnumerable<EntityMetadata>> GetAllEntitiesAsync();
    Task<EntityMetadata?> GetEntityMetadataAsync(string entityLogicalName);
    Task<IEnumerable<AttributeMetadata>> GetEntityAttributesAsync(string entityLogicalName);
    Task<AttributeMetadata?> GetAttributeMetadataAsync(string entityLogicalName, string attributeLogicalName);
    Task<OptionMetadata[]?> GetGlobalOptionSetAsync(string optionSetName);

    // CRUD Operations
    Task<Guid> CreateRecordAsync(string entityLogicalName, Dictionary<string, object> attributes);
    Task<Entity?> GetRecordAsync(string entityLogicalName, Guid id, string[]? columns = null);
    Task UpdateRecordAsync(string entityLogicalName, Guid id, Dictionary<string, object> attributes);
    Task DeleteRecordAsync(string entityLogicalName, Guid id);
    Task<IEnumerable<Entity>> QueryRecordsAsync(
        string entityLogicalName,
        string? filter = null,
        string[]? columns = null,
        int? maxResults = null,
        string? orderBy = null,
        bool orderDescending = false);

    // N:N relationships
    Task AssociateAsync(string entityLogicalName, Guid id, string relationshipName, string relatedEntity, Guid[] relatedIds);
    Task DisassociateAsync(string entityLogicalName, Guid id, string relationshipName, string relatedEntity, Guid[] relatedIds);
}
