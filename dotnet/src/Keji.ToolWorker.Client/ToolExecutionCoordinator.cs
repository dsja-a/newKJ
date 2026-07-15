using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.ToolWorker.Protocol;
using Keji.Tools.Definitions;
using Keji.Tools.Execution;
using Keji.Tools.Names;
using Keji.Tools.Registry;

namespace Keji.ToolWorker.Client;

public interface IToolExecutionCoordinator
{
    Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? inputs,
        string? rawInputJson,
        CancellationToken ct = default);
}

public sealed class ToolExecutionCoordinator : IToolExecutionCoordinator
{
    private readonly IToolWorkerClient _workerClient;
    private readonly ICurrentUserAccessor _userAccessor;
    private readonly IKejiToolRegistry _registry;
    private readonly IKejiAuthorizationService _authService;
    private readonly IKejiAuditService _auditService;

    public ToolExecutionCoordinator(
        IToolWorkerClient workerClient,
        ICurrentUserAccessor userAccessor,
        IKejiToolRegistry registry,
        IKejiAuthorizationService authService,
        IKejiAuditService auditService)
    {
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
        _userAccessor = userAccessor ?? throw new ArgumentNullException(nameof(userAccessor));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? inputs,
        string? rawInputJson,
        CancellationToken ct = default)
    {
        var user = _userAccessor.CurrentUser;

        if (!KejiToolName.TryCreate(toolName, out var name))
            return await FailAsync(null, user, $"Invalid tool name: '{toolName}'");

        var resolution = _registry.Resolve(name);
        if (resolution.Status != KejiToolResolutionStatus.Found || resolution.Definition is null)
            return await FailAsync(null, user, $"Tool not registered: '{toolName}'");

        var def = resolution.Definition;

        if (def.Availability != KejiToolAvailability.Executable)
            return await FailAsync(def, user, "Tool is not executable");

        if (def.ExecutionTarget != KejiToolExecutionTarget.ToolWorker)
            return await FailAsync(def, user, "Tool target mismatch");

        var auth = _authService.Authorize(user, def.RequiredPermission);
        if (!auth.IsAllowed)
        {
            await AuditAsync(def, user, KejiAuditOutcome.Denied);
            return ToolExecutionResult.Failed("Permission denied", "PERMISSION_DENIED");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        var request = new ToolWorkerRequest
        {
            ProtocolVersion = Protocol.ProtocolVersion.String,
            RequestId = requestId,
            DeadlineUtc = deadline.ToString("O"),
            ToolName = toolName,
            ContractVersion = def.ContractVersion.ToString(),
            InputJson = rawInputJson ?? "{}"
        };

        ToolWorkerResponse workerResponse;
        try
        {
            workerResponse = await _workerClient.ExecuteAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await AuditAsync(def, user, KejiAuditOutcome.Error);
            return ToolExecutionResult.Failed($"Worker execution failed: {ex.Message}", "WORKER_ERROR");
        }

        var success = workerResponse.ErrorCode == 0;
        await AuditAsync(def, user, success ? KejiAuditOutcome.Success : KejiAuditOutcome.Failure);

        return success
            ? ToolExecutionResult.Successful(workerResponse.ResultJson, TimeSpan.Zero)
            : ToolExecutionResult.Failed(workerResponse.ErrorMessage ?? "Worker execution failed", $"WORKER_ERROR_{workerResponse.ErrorCode}");
    }

    private async Task<ToolExecutionResult> FailAsync(KejiToolDefinition? def, CurrentUser? user, string error)
    {
        if (def is not null)
            await AuditAsync(def, user, KejiAuditOutcome.Error);
        return ToolExecutionResult.Failed(error);
    }

    private async Task AuditAsync(KejiToolDefinition def, CurrentUser? user, KejiAuditOutcome outcome)
    {
        try
        {
            await _auditService.WriteAsync(
                KejiAuditCategory.ToolExecution,
                "tool_execute",
                outcome,
                outcome == KejiAuditOutcome.Denied ? KejiAuditSeverity.Warning : KejiAuditSeverity.Information,
                "KejiToolDefinition",
                targetId: def.Name.Value,
                cancellationToken: default);
        }
        catch
        {
        }
    }
}
