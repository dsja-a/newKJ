using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Persistence;
using Keji.Persistence.Models;
using Keji.Persistence.Repositories;
using Keji.Persistence.Validation;
using Keji.Security.Auth;
using Microsoft.Extensions.Logging;

namespace Keji.Api.Services;

public sealed class KejiConversationService : IKejiConversationService
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IKejiAuditService _auditService;
    private readonly ILogger<KejiConversationService> _logger;

    public KejiConversationService(
        IConversationRepository conversationRepository,
        IMessageRepository messageRepository,
        ICurrentUserAccessor currentUserAccessor,
        IKejiAuditService auditService,
        ILogger<KejiConversationService> logger)
    {
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _currentUserAccessor = currentUserAccessor;
        _auditService = auditService;
        _logger = logger;
    }

    private string RequireUserId()
    {
        var userId = _currentUserAccessor.CurrentUser?.Id;
        if (userId is null)
            throw new KejiPersistenceException("User is not authenticated.");
        return UserIdValidator.RequireValid(userId);
    }

    public async Task<ConversationRecord> CreateConversationAsync(string convId, string title = "新对话", CancellationToken cancellationToken = default)
    {
        var ownerUserId = RequireUserId();
        var record = await _conversationRepository.CreateOwnedAsync(convId, ownerUserId, title, cancellationToken).ConfigureAwait(false);
        await AuditDataAccessAsync("conversation_create", KejiAuditOutcome.Success, "conversation", convId, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<(ConversationRecord Record, ConversationOwnershipResult Result)> EnsureConversationAsync(string convId, string title = "新对话", CancellationToken cancellationToken = default)
    {
        var ownerUserId = RequireUserId();
        var result = await _conversationRepository.EnsureOwnedAsync(convId, ownerUserId, title, cancellationToken).ConfigureAwait(false);
        if (result.Result == ConversationOwnershipResult.AlreadyOwned)
        {
            await AuditDataAccessAsync("conversation_ensure", KejiAuditOutcome.Success, "conversation", null, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    public async Task<ConversationRecord?> GetConversationAsync(string convId, CancellationToken cancellationToken = default)
    {
        var ownerUserId = RequireUserId();
        var record = await _conversationRepository.GetOwnedAsync(convId, ownerUserId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await AuditDataAccessAsync("conversation_access", KejiAuditOutcome.Denied, "conversation", null, cancellationToken).ConfigureAwait(false);
        }
        return record;
    }

    public async Task<List<ConversationRecord>> ListConversationsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var ownerUserId = RequireUserId();
        return await _conversationRepository.ListOwnedAsync(ownerUserId, limit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RenameConversationAsync(string convId, string title, CancellationToken cancellationToken = default)
    {
        var ownerUserId = RequireUserId();
        var ok = await _conversationRepository.RenameOwnedAsync(convId, ownerUserId, title, cancellationToken).ConfigureAwait(false);
        if (!ok)
        {
            await AuditDataAccessAsync("conversation_access", KejiAuditOutcome.Denied, "conversation", null, cancellationToken).ConfigureAwait(false);
        }
        return ok;
    }

    public async Task<bool> DeleteConversationAsync(string convId, CancellationToken cancellationToken = default)
    {
        var ownerUserId = RequireUserId();
        var deleted = await _conversationRepository.DeleteOwnedAsync(convId, ownerUserId, cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            await AuditDataAccessAsync("conversation_delete", KejiAuditOutcome.Success, "conversation", convId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await AuditDataAccessAsync("conversation_access", KejiAuditOutcome.Denied, "conversation", null, cancellationToken).ConfigureAwait(false);
        }
        return deleted;
    }

    public async Task<int> CountConversationsAsync(CancellationToken cancellationToken = default)
    {
        var ownerUserId = RequireUserId();
        return await _conversationRepository.CountByOwnerAsync(ownerUserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> AddMessageAsync(string conversationId, string role, string content, CancellationToken cancellationToken = default)
    {
        var ownerUserId = RequireUserId();
        return await _messageRepository.AddOwnedAsync(conversationId, ownerUserId, role, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<MessageRecord>> ListMessagesAsync(string conversationId, int limit = 100, CancellationToken cancellationToken = default)
    {
        var ownerUserId = RequireUserId();
        return await _messageRepository.ListOwnedMessagesAsync(conversationId, ownerUserId, limit, cancellationToken).ConfigureAwait(false);
    }

    private async Task AuditDataAccessAsync(string action, KejiAuditOutcome outcome, string targetType, string? targetId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _auditService.WriteAsync(
                KejiAuditCategory.DataAccess,
                action: action,
                outcome: outcome,
                severity: outcome == KejiAuditOutcome.Denied ? KejiAuditSeverity.Warning : KejiAuditSeverity.Information,
                targetType: targetType,
                targetId: targetId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            switch (result)
            {
                case KejiAuditResult.PartialFailure:
                    _logger.LogWarning("ConversationAuditFailure code=AUDIT_PARTIAL_SINK_FAILURE action={Action}", action);
                    break;
                case KejiAuditResult.SinkError:
                    _logger.LogError("ConversationAuditFailure code=AUDIT_SINK_FAILED action={Action}", action);
                    break;
                case KejiAuditResult.ValidationError:
                    _logger.LogWarning("ConversationAuditFailure code=AUDIT_VALIDATION_FAILED action={Action}", action);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
}
