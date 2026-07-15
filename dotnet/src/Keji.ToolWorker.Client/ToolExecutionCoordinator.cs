using System.Text.Json;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.ToolWorker.Protocol;
using Keji.Tools.Definitions;
using Keji.Tools.Execution;
using Keji.Tools.Names;
using Keji.Tools.Registry;
using Keji.Tools.Validation;

namespace Keji.ToolWorker.Client;

public sealed class WorkerExecutionConfig
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(10);

    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromSeconds(60))
                throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be between 1ms and 60s");
            _timeout = value;
        }
    }

    public WorkerExecutionConfig()
    {
    }

    public WorkerExecutionConfig(TimeSpan timeout)
    {
        Timeout = timeout;
    }
}

public sealed class ToolExecutionCoordinator : IToolExecutionCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IToolWorkerClient _workerClient;
    private readonly ICurrentUserAccessor _userAccessor;
    private readonly IKejiToolRegistry _registry;
    private readonly IKejiAuthorizationService _authService;
    private readonly IKejiAuditService _auditService;
    private readonly TimeSpan _workerTimeout;

    public ToolExecutionCoordinator(
        IToolWorkerClient workerClient,
        ICurrentUserAccessor userAccessor,
        IKejiToolRegistry registry,
        IKejiAuthorizationService authService,
        IKejiAuditService auditService,
        WorkerExecutionConfig? config = null)
    {
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
        _userAccessor = userAccessor ?? throw new ArgumentNullException(nameof(userAccessor));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _workerTimeout = config?.Timeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? inputs,
        CancellationToken ct = default)
    {
        var user = _userAccessor.CurrentUser;

        if (user is null)
            return ToolExecutionResult.Failed("Authentication required", "AUTH_REQUIRED");

        if (!KejiToolName.TryCreate(toolName, out var name))
            return ToolExecutionResult.Failed("Invalid tool name", "INVALID_TOOL_NAME");

        var resolution = _registry.Resolve(name);
        if (resolution.Status != KejiToolResolutionStatus.Found || resolution.Definition is null)
            return ToolExecutionResult.Failed("Tool not registered", "UNKNOWN_TOOL");

        var def = resolution.Definition;

        if (def.Availability != KejiToolAvailability.Executable)
            return ToolExecutionResult.Failed("Tool is not executable", "NOT_EXECUTABLE");

        if (def.ExecutionTarget != KejiToolExecutionTarget.ToolWorker)
            return ToolExecutionResult.Failed("Tool target mismatch", "TARGET_MISMATCH");

        var validation = KejiToolInputValidator.Validate(_registry, toolName, inputs);
        if (!validation.IsValid)
            return ToolExecutionResult.Failed("Invalid inputs", "INVALID_INPUT");

        var auth = _authService.Authorize(user, def.RequiredPermission);
        if (!auth.IsAllowed)
        {
            await AuditAsync(def, user, KejiAuditOutcome.Denied);
            return ToolExecutionResult.Failed("Permission denied", "PERMISSION_DENIED");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var deadline = DateTimeOffset.UtcNow.Add(_workerTimeout);
        var inputJson = inputs is not null ? JsonSerializer.Serialize(inputs, JsonOptions) : "{}";

        var request = new ToolWorkerRequest
        {
            ProtocolVersion = Protocol.ProtocolVersion.String,
            RequestId = requestId,
            DeadlineUtc = deadline.ToString("O"),
            ToolName = toolName,
            ContractVersion = def.ContractVersion.ToString(),
            InputJson = inputJson
        };

        ToolWorkerResponse workerResponse;
        try
        {
            workerResponse = await _workerClient.ExecuteAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await AuditAsync(def, user, KejiAuditOutcome.Error);
            return ToolExecutionResult.Failed("Worker execution failed", "WORKER_UNAVAILABLE");
        }

        if (workerResponse.ProtocolVersion != Protocol.ProtocolVersion.String)
        {
            await AuditAsync(def, user, KejiAuditOutcome.Error);
            return ToolExecutionResult.Failed("Protocol version mismatch", "PROTOCOL_ERROR");
        }

        if (workerResponse.RequestId != requestId)
        {
            await AuditAsync(def, user, KejiAuditOutcome.Error);
            return ToolExecutionResult.Failed("Request ID mismatch", "PROTOCOL_ERROR");
        }

        if (workerResponse.ToolName != toolName)
        {
            await AuditAsync(def, user, KejiAuditOutcome.Error);
            return ToolExecutionResult.Failed("Tool name mismatch", "PROTOCOL_ERROR");
        }

        if (workerResponse.ContractVersion != def.ContractVersion.ToString())
        {
            await AuditAsync(def, user, KejiAuditOutcome.Error);
            return ToolExecutionResult.Failed("Contract version mismatch", "PROTOCOL_ERROR");
        }

        if (!Enum.IsDefined(typeof(ToolWorkerErrorCode), workerResponse.ErrorCode))
        {
            await AuditAsync(def, user, KejiAuditOutcome.Error);
            return ToolExecutionResult.Failed("Unknown error code", "PROTOCOL_ERROR");
        }

        var errorCode = (ToolWorkerErrorCode)workerResponse.ErrorCode;
        var outcome = errorCode == ToolWorkerErrorCode.None ? KejiAuditOutcome.Success : KejiAuditOutcome.Failure;
        await AuditAsync(def, user, outcome);

        if (errorCode == ToolWorkerErrorCode.None)
            return ToolExecutionResult.Successful(workerResponse.ResultJson, TimeSpan.Zero);

        return errorCode switch
        {
            ToolWorkerErrorCode.Timeout => ToolExecutionResult.Failed("Worker timed out", "TIMEOUT"),
            ToolWorkerErrorCode.Cancelled => ToolExecutionResult.Failed("Worker cancelled", "CANCELLED"),
            ToolWorkerErrorCode.InvalidRequest => ToolExecutionResult.Failed("Invalid request", "INVALID_REQUEST"),
            ToolWorkerErrorCode.UnknownTool => ToolExecutionResult.Failed("Unknown tool", "UNKNOWN_TOOL"),
            ToolWorkerErrorCode.ContractMismatch => ToolExecutionResult.Failed("Contract version mismatch", "CONTRACT_MISMATCH"),
            ToolWorkerErrorCode.InvalidInput => ToolExecutionResult.Failed("Invalid input", "INVALID_INPUT"),
            ToolWorkerErrorCode.ExecutionFailed => ToolExecutionResult.Failed("Execution failed", "EXECUTION_FAILED"),
            ToolWorkerErrorCode.ProtocolError => ToolExecutionResult.Failed("Protocol error", "PROTOCOL_ERROR"),
            ToolWorkerErrorCode.WorkerUnavailable => ToolExecutionResult.Failed("Worker unavailable", "WORKER_UNAVAILABLE"),
            ToolWorkerErrorCode.OutputTooLarge => ToolExecutionResult.Failed("Output too large", "OUTPUT_TOO_LARGE"),
            _ => ToolExecutionResult.Failed("Protocol error", "PROTOCOL_ERROR")
        };
    }

    private async Task AuditAsync(KejiToolDefinition def, CurrentUser user, KejiAuditOutcome outcome)
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
            System.Diagnostics.Trace.TraceWarning("KEJI_TOOL_AUDIT_WRITE_FAILED");
        }
    }
}
