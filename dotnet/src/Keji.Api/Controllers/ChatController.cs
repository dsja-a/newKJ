using Keji.Agent;
using Keji.Security.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Keji.Api.Controllers;

public sealed record KejiChatApiRequest(string RunId,string ConversationId,string Message);

[ApiController]
public sealed class ChatController : ControllerBase
{
    private const string Provider="openai";
    private const string Model="server-default";
    private readonly IKejiAgentLoop _agent;
    private readonly KejiAgentSseAdapter _sse;
    public ChatController(IKejiAgentLoop agent,KejiAgentSseAdapter sse)
    {
        _agent=agent;_sse=sse;
    }

    [HttpPost("/chat")]
    [KejiRequirePermission(KejiPermission.ChatUse)]
    public async Task<IActionResult> Run(
        [FromBody]KejiChatApiRequest request,CancellationToken cancellationToken)
    {
        KejiAgentEvent? terminal=null;
        await foreach(var item in _agent.RunStreamAsync(Map(request),cancellationToken).ConfigureAwait(false))
            if(item.Type is KejiAgentEventType.Error or KejiAgentEventType.RunCompleted)terminal=item;
        return Ok(new
        {
            runId=terminal?.RunId,status=terminal?.StopReason.ToString(),
            errorCode=terminal?.ErrorCode.ToString(),
            answer=terminal?.Transcript?.FinalContent
        });
    }

    [HttpPost("/chat/stream")]
    [KejiRequirePermission(KejiPermission.ChatUse)]
    public async Task Stream(
        [FromBody]KejiChatApiRequest request,CancellationToken cancellationToken)
    {
        Response.StatusCode=StatusCodes.Status200OK;
        Response.ContentType="text/event-stream";
        Response.Headers["X-Accel-Buffering"]="no";
        var events=_agent.RunStreamAsync(Map(request),cancellationToken);
        await foreach(var wire in _sse.AdaptAsync(events,cancellationToken).ConfigureAwait(false))
        {
            await Response.WriteAsync(wire,cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static KejiAgentRunRequest Map(KejiChatApiRequest request)=>new()
    {
        RunId=request.RunId,ConversationId=request.ConversationId,UserMessage=request.Message,
        ProviderName=Provider,Model=Model
    };
}
