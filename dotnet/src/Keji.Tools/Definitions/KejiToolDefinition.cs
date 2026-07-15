using System.Collections.Frozen;
using Keji.Security.Authorization;
using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Names;

namespace Keji.Tools.Definitions;

public sealed class KejiToolDefinition
{
    public KejiToolName Name { get; }
    public int ContractVersion { get; }
    public string Description { get; }
    public KejiToolCategory Category { get; }
    public KejiToolRiskLevel RiskLevel { get; }
    public KejiToolExecutionTarget ExecutionTarget { get; }
    public KejiToolAvailability Availability { get; }
    public KejiPermission RequiredPermission { get; }
    public KejiToolInputSchema InputSchema { get; }
    public bool IsDeterministic { get; }
    public bool SupportsCancellation { get; }
    public IReadOnlySet<string> Tags { get; }

    private static readonly int MaxDescriptionLength = 2000;
    private static readonly int MaxTagLength = 32;
    private static readonly int MaxTagCount = 16;

    public KejiToolDefinition(
        KejiToolName? name,
        int contractVersion,
        string description,
        KejiToolCategory category,
        KejiToolRiskLevel riskLevel,
        KejiToolExecutionTarget executionTarget,
        KejiPermission requiredPermission,
        KejiToolInputSchema? inputSchema = null,
        KejiToolAvailability availability = KejiToolAvailability.ContractOnly,
        bool isDeterministic = false,
        bool supportsCancellation = false,
        IReadOnlySet<string>? tags = null)
    {
        if (name is null)
            throw new KejiToolContractException("Name must not be null.");
        if (description is null)
            throw new KejiToolContractException("Description must not be null.");
        if (description.Length == 0)
            throw new KejiToolContractException("Description must not be empty.");
        if (description.Length > MaxDescriptionLength)
            throw new KejiToolContractException($"Description must not exceed {MaxDescriptionLength} characters.");
        if (contractVersion < 1)
            throw new KejiToolContractException("ContractVersion must be >= 1.");

        if (!Enum.IsDefined(category))
            throw new KejiToolContractException("Category must be a defined enum value.");
        if (!Enum.IsDefined(riskLevel))
            throw new KejiToolContractException("RiskLevel must be a defined enum value.");
        if (!Enum.IsDefined(executionTarget))
            throw new KejiToolContractException("ExecutionTarget must be a defined enum value.");
        if (!Enum.IsDefined(availability))
            throw new KejiToolContractException("Availability must be a defined enum value.");
        if (!KejiPermissionCatalog.IsDefined(requiredPermission))
            throw new KejiToolContractException("RequiredPermission must be a defined permission.");

        if (tags is not null)
        {
            if (tags.Count > MaxTagCount)
                throw new KejiToolContractException($"Tags must not exceed {MaxTagCount} entries.");
            foreach (var tag in tags)
            {
                if (tag is null)
                    throw new KejiToolContractException("Tag must not be null.");
                if (tag.Length == 0)
                    throw new KejiToolContractException("Tag must not be empty.");
                if (tag.Length > MaxTagLength)
                    throw new KejiToolContractException($"Each tag must be 1-{MaxTagLength} characters.");
                if (tag.Any(c => char.IsControl(c)))
                    throw new KejiToolContractException("Tag must not contain control characters.");
            }
        }

        if (inputSchema is not null)
        {
            foreach (var p in inputSchema.Parameters)
            {
                if (p is null)
                    throw new KejiToolContractException("Schema parameter must not be null.");
            }
        }

        Name = name;
        ContractVersion = contractVersion;
        Description = description;
        Category = category;
        RiskLevel = riskLevel;
        ExecutionTarget = executionTarget;
        Availability = availability;
        RequiredPermission = requiredPermission;
        InputSchema = inputSchema ?? new KejiToolInputSchema(null);
        IsDeterministic = isDeterministic;
        SupportsCancellation = supportsCancellation;
        Tags = tags is not null
            ? tags.ToFrozenSet(StringComparer.Ordinal)
            : FrozenSet<string>.Empty;
    }
}
