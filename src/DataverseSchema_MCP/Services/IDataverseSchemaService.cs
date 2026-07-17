using DataverseSchema_MCP.Models;

namespace DataverseSchema_MCP.Services;

public interface IDataverseSchemaService
{
    Task<bool> ConnectAsync();
    Task<string?> WhoAmIAsync();

    // Entities (tables)
    Task<string> CreateEntityAsync(CreateEntityRequestDto request);
    Task UpdateEntityAsync(UpdateEntityRequestDto request);
    Task DeleteEntityAsync(string entityLogicalName);

    // Attributes (columns)
    Task<string> CreateAttributeAsync(CreateAttributeRequestDto request);
    Task UpdateAttributeAsync(UpdateAttributeRequestDto request);
    Task DeleteAttributeAsync(string entityLogicalName, string attributeLogicalName);

    // Option sets
    Task<string> CreateGlobalOptionSetAsync(CreateGlobalOptionSetRequestDto request);
    Task<int> InsertOptionSetValueAsync(OptionSetValueTargetDto request);
    Task DeleteOptionSetValueAsync(OptionSetValueTargetDto request);

    // Relationships
    Task<string> CreateOneToManyAsync(CreateOneToManyRequestDto request);
    Task<string> CreateManyToManyAsync(CreateManyToManyRequestDto request);

    // Publishing
    Task PublishAllAsync();
}
