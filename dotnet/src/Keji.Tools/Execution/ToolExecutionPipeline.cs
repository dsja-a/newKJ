using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.Tools.Definitions;
using Keji.Tools.Names;
using Keji.Tools.Registry;
using Keji.Tools.Validation;

namespace Keji.Tools.Execution;

public sealed class ToolExecutionPipeline : IToolExecutionPipeline
{
    private readonly ICurrentUserAccessor _userAccessor;
    private readonly IKejiToolRegistry _registry;
    private readonly IKejiAuthorizationService _authService;
    private readonly IKejiAuditService _auditService;
    private readonly IKejiAuditCorrelationAccessor _correlationAccessor;
    private readonly ToolWorkerLauncher _launcher;

    public ToolExecutionPipeline(
        ICurrentUserAccessor userAccessor,
        IKejiToolRegistry registry,
        IKejiAuthorizationService authService,
        IKejiAuditService auditService,
        IKejiAuditCorrelationAccessor correlationAccessor,
        ToolWorkerLauncher launcher)
    {
        _userAccessor = userAccessor;
        _registry = registry;
        _authService = authService;
        _auditService = auditService;
        _correlationAccessor = correlationAccessor;
        _launcher = launcher;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? inputs,
        CancellationToken cancellationToken = default)
    {
        if (!KejiToolName.TryCreate(toolName, out var name))
            return await FailAsync(null, null, $"Invalid tool name: '{toolName}'.", "INVALID_TOOL_NAME");

        var resolution = _registry.Resolve(name);
        if (resolution.Status != KejiToolResolutionStatus.Found || resolution.Definition is null)
            return await FailAsync(null, null, "Tool is not registered.", "NOT_FOUND");

        var user = _userAccessor.CurrentUser;
        if (user is null)
            return await FailAsync(null, null, "Authentication required.", "AUTH_REQUIRED");

        var def = resolution.Definition;

        if (def.Availability != KejiToolAvailability.Executable)
            return await FailAsync(def, user, "Tool is not executable.", "NOT_EXECUTABLE");

        if (def.ExecutionTarget != KejiToolExecutionTarget.ToolWorker)
            return await FailAsync(def, user, "Tool execution target mismatch.", "TARGET_MISMATCH");

        var validation = KejiToolInputValidator.Validate(_registry, toolName, inputs);
        if (!validation.IsValid)
            return await FailAsync(def, user, validation.ErrorMessage ?? "Invalid inputs.", "VALIDATION_ERROR");

        var auth = _authService.Authorize(user, def.RequiredPermission);
        if (!auth.IsAllowed)
        {
            await AuditAsync(def, user, KejiAuditOutcome.Denied, "Permission denied.");
            return ToolExecutionResult.Failed("Permission denied.", "PERMISSION_DENIED");
        }

        var result = await _launcher.ExecuteAsync(def, inputs ?? new Dictionary<string, object?>(), cancellationToken);

        await AuditAsync(def, user, result.Success ? KejiAuditOutcome.Success : KejiAuditOutcome.Failure,
            result.ErrorMessage ?? "Executed successfully.");

        return result;
    }

    private async Task<ToolExecutionResult> FailAsync(
        KejiToolDefinition? def, CurrentUser? user, string error, string errorCode)
    {
        if (def is not null)
            await AuditAsync(def, user, KejiAuditOutcome.Error, error);
        return ToolExecutionResult.Failed(error, errorCode);
    }

    private async Task AuditAsync(
        KejiToolDefinition def, CurrentUser? user, KejiAuditOutcome outcome, string detail)
    {
        try
        {
            var metadata = new Dictionary<string, string>
            {
                ["tool"] = def.Name.Value,
                ["detail"] = detail
            };

            if (_correlationAccessor.CorrelationId is not null)
                metadata["correlationId"] = _correlationAccessor.CorrelationId;

            await _auditService.WriteAsync(
                KejiAuditCategory.ToolExecution,
                "ToolExecution",
                outcome,
                outcome == KejiAuditOutcome.Denied ? KejiAuditSeverity.Warning : KejiAuditSeverity.Information,
                "KejiToolDefinition",
                targetId: def.Name.Value,
                metadata: metadata);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Audit write failed: {ex.Message}");
        }
    }
}
