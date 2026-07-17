namespace DataverseSchema_MCP.Models;

/// <summary>Represents a single option (value + label) of an option set.</summary>
public class OptionDto
{
    public int? Value { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class CreateEntityRequestDto
{
    public string SchemaName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DisplayCollectionName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string OwnershipType { get; set; } = "UserOwned"; // UserOwned | OrganizationOwned
    public string? PrimaryAttributeSchemaName { get; set; }
    public string PrimaryAttributeDisplayName { get; set; } = "Name";
    public int PrimaryAttributeMaxLength { get; set; } = 100;
    public bool HasNotes { get; set; }
    public bool HasActivities { get; set; }
}

public class UpdateEntityRequestDto
{
    public string EntityLogicalName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? DisplayCollectionName { get; set; }
    public string? Description { get; set; }
}

public class CreateAttributeRequestDto
{
    public string EntityLogicalName { get; set; } = string.Empty;
    public string AttributeType { get; set; } = string.Empty; // String|Memo|Integer|BigInt|Decimal|Double|Money|Boolean|DateTime|Picklist|MultiSelectPicklist
    public string SchemaName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RequiredLevel { get; set; } = "None"; // None|Recommended|ApplicationRequired

    public int? MaxLength { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public int? Precision { get; set; }
    public string? StringFormat { get; set; }   // Text|Email|Url|Phone|TextArea
    public string? DateTimeFormat { get; set; }  // DateOnly|DateAndTime

    public List<OptionDto>? Options { get; set; } // for local Picklist/MultiSelectPicklist
    public string? GlobalOptionSetName { get; set; } // bind Picklist/MultiSelect to a global option set

    public string TrueLabel { get; set; } = "Yes";
    public string FalseLabel { get; set; } = "No";
}

public class UpdateAttributeRequestDto
{
    public string EntityLogicalName { get; set; } = string.Empty;
    public string AttributeLogicalName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? RequiredLevel { get; set; } // None|Recommended|ApplicationRequired
}

public class CreateGlobalOptionSetRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsMultiSelect { get; set; }
    public List<OptionDto> Options { get; set; } = new();
}

public class OptionSetValueTargetDto
{
    public string? GlobalOptionSetName { get; set; }
    public string? EntityLogicalName { get; set; }
    public string? AttributeLogicalName { get; set; }
    public string? Label { get; set; }
    public int? Value { get; set; }
}

public class CreateOneToManyRequestDto
{
    public string ReferencedEntity { get; set; } = string.Empty;   // "one" side (lookup target)
    public string ReferencingEntity { get; set; } = string.Empty;  // "many" side (holds the lookup column)
    public string LookupSchemaName { get; set; } = string.Empty;
    public string LookupDisplayName { get; set; } = string.Empty;
    public string? LookupDescription { get; set; }
    public string RelationshipSchemaName { get; set; } = string.Empty;
    public string RequiredLevel { get; set; } = "None";
}

public class CreateManyToManyRequestDto
{
    public string Entity1LogicalName { get; set; } = string.Empty;
    public string Entity2LogicalName { get; set; } = string.Empty;
    public string RelationshipSchemaName { get; set; } = string.Empty;
    public string IntersectEntitySchemaName { get; set; } = string.Empty;
}
