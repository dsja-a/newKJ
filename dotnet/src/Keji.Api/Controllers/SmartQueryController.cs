using Keji.Providers;
using Keji.Security.Authorization;
using Keji.SmartQuery;
using Keji.Streaming;
using Microsoft.AspNetCore.Mvc;

namespace Keji.Api.Controllers;

[ApiController]
[Route("api/smart-query")]
public sealed class SmartQueryController : ControllerBase
{
    private readonly IKejiSmartQuery _smartQuery;
    public SmartQueryController(IKejiSmartQuery smartQuery)=>_smartQuery=smartQuery;

    [HttpPost]
    [KejiRequirePermission(KejiPermission.SmartQueryExecute)]
    public async Task<IActionResult> Run([FromBody]KejiSmartQueryRequest request,CancellationToken cancellationToken)=>
        Ok(await _smartQuery.RunAsync(request,cancellationToken).ConfigureAwait(false));

    [HttpPost("with-steps")]
    [KejiRequirePermission(KejiPermission.SmartQueryExecute)]
    public async Task<IActionResult> RunWithSteps(
        [FromBody]KejiSmartQueryRequest request,CancellationToken cancellationToken)
    {
        var events=new List<KejiSmartQueryEvent>(16);
        await foreach(var item in _smartQuery.RunStreamAsync(request,cancellationToken).ConfigureAwait(false))
            events.Add(item);
        return Ok(new{events,result=events.LastOrDefault(static item=>item.Result is not null)?.Result});
    }

    [HttpPost("stream")]
    [KejiRequirePermission(KejiPermission.SmartQueryExecute)]
    public async Task Stream([FromBody]KejiSmartQueryRequest request,CancellationToken cancellationToken)
    {
        Response.StatusCode=StatusCodes.Status200OK;
        Response.ContentType="text/event-stream";
        Response.Headers["X-Accel-Buffering"]="no";
        await foreach(var item in _smartQuery.RunStreamAsync(request,cancellationToken).ConfigureAwait(false))
        {
            var wire=KejiSseFormatter.FormatEvent(Map(item));
            await Response.WriteAsync(wire,cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static KejiSseEvent Map(KejiSmartQueryEvent item)=>new()
    {
        EventType=item.Type switch
        {
            KejiSmartQueryEventType.Error=>KejiSseEventType.Error,
            KejiSmartQueryEventType.Completed=>KejiSseEventType.Done,
            _=>KejiSseEventType.SystemNotice
        },
        Phase=item.Type switch
        {
            KejiSmartQueryEventType.Error=>KejiSsePhase.Error,
            KejiSmartQueryEventType.Completed=>KejiSsePhase.Done,
            _=>KejiSsePhase.Answering
        },
        Delta=item.Type is KejiSmartQueryEventType.Error or KejiSmartQueryEventType.Completed
            ? null:item.Type.ToString(),
        ErrorCode=item.Type==KejiSmartQueryEventType.Error
            ?KejiProviderErrorCode.ProviderError:KejiProviderErrorCode.Invalid,
        Sequence=item.Sequence,EventId=Guid.NewGuid().ToString("N"),TimestampUtc=item.Timestamp.UtcDateTime
    };
}
