using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using ProjectOpsTestData_MCP.Models;

namespace ProjectOpsTestData_MCP.Services;

public interface IProjectOpsTestDataService
{
    // Connection management
    Task<bool> ConnectAsync();
    
    // Metadata operations (copied from working Dataverse_MCP)
    Task<IEnumerable<EntityMetadata>> GetAllEntitiesAsync();
    Task<EntityMetadata?> GetEntityMetadataAsync(string entityLogicalName);
    Task<IEnumerable<AttributeMetadata>> GetEntityAttributesAsync(string entityLogicalName);
    Task<IEnumerable<OneToManyRelationshipMetadata>> GetEntityRelationshipsAsync(string entityLogicalName);
    Task<AttributeMetadata?> GetAttributeMetadataAsync(string entityLogicalName, string attributeLogicalName);
    
    // CRUD Operations (copied from working Dataverse_MCP)
    Task<Guid> CreateRecordAsync(string entityLogicalName, Dictionary<string, object> attributes);
    Task<Entity?> GetRecordAsync(string entityLogicalName, Guid id, string[]? columns = null);
    Task UpdateRecordAsync(string entityLogicalName, Guid id, Dictionary<string, object> attributes);
    Task DeleteRecordAsync(string entityLogicalName, Guid id);
    Task<IEnumerable<Entity>> QueryRecordsAsync(string entityLogicalName, string? filter = null, string[]? columns = null, int? maxResults = null);

    // Test data generation (existing functionality)
    Task<List<AccountTestData>> GenerateAccountTestDataAsync(int count = 20);
    Task<List<ResourceTestData>> GenerateResourceTestDataAsync(int count = 15);
    Task<List<ProjectTestData>> GenerateProjectTestDataAsync(TestDataGenerationOptions options);
    
    // Test data creation with metadata validation (Project Operations specific)
    Task<string> CreateAccountsAsync(List<AccountTestData> accounts);
    Task<string> CreateResourcesAsync(List<ResourceTestData> resources);
    Task<string> CreateProjectsAsync(List<ProjectTestData> projects);
    
    // Project Operations specific methods
    Task<(int SuccessCount, int FailedCount)> CreateProjectTasksUsingDynamics365ActionAsync(Guid projectId, List<TaskTestData> tasks);
    Task<string> CreateCompleteTestDataSetAsync(TestDataGenerationOptions options);
    Task<string> DeleteAllTestDataAsync();
    Task<TestDataStatistics> GetTestDataStatisticsAsync();
}