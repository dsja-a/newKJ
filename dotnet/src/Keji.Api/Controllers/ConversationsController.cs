using Keji.Api.Services;
using Keji.Security.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Keji.Api.Controllers;

[ApiController]
[Route("api/conversations")]
public sealed class ConversationsController : ControllerBase
{
    private readonly IKejiConversationService _conversations;
    public ConversationsController(IKejiConversationService conversations)=>_conversations=conversations;

    [HttpGet]
    [KejiRequirePermission(KejiPermission.ConversationRead)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)=>
        Ok(await _conversations.ListConversationsAsync(50,cancellationToken).ConfigureAwait(false));

    [HttpGet("{conv_id}")]
    [KejiRequirePermission(KejiPermission.ConversationRead)]
    public async Task<IActionResult> Get(string conv_id,CancellationToken cancellationToken)
    {
        if(!SafeId(conv_id))return NotFound();
        var conversation=await _conversations.GetConversationAsync(conv_id,cancellationToken).ConfigureAwait(false);
        if(conversation is null)return NotFound();
        var messages=await _conversations.ListMessagesAsync(conv_id,100,cancellationToken).ConfigureAwait(false);
        return Ok(new{conversation,messages});
    }

    [HttpDelete("{conv_id}")]
    [KejiRequirePermission(KejiPermission.ConversationWrite)]
    public async Task<IActionResult> Delete(string conv_id,CancellationToken cancellationToken)
    {
        if(!SafeId(conv_id))return NotFound();
        return await _conversations.DeleteConversationAsync(conv_id,cancellationToken).ConfigureAwait(false)
            ? Ok(new{deleted=true}) : NotFound();
    }

    private static bool SafeId(string value)=>value.Length is >0 and <=128&&
        value.All(static c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
}
