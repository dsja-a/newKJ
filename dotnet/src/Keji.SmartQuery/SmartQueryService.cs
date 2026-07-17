using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Providers;
using Keji.Security.Auth;
using Keji.Security.Authorization;

namespace Keji.SmartQuery;

internal sealed class KejiSmartQueryService : IKejiSmartQuery
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions PlanJson = new()
    {
        PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=12,
        Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase,false)}
    };
    private readonly ICurrentUserAccessor _users;
    private readonly IKejiAuthorizationService _authorization;
    private readonly IKejiSmartQueryDataSourceCatalog _sources;
    private readonly IModelProviderRegistry _providers;
    private readonly KejiSmartQueryAuditWriter _audit;
    private readonly IReadOnlyDictionary<KejiSmartQueryDialect,IKejiSmartQueryDialectCompiler> _dialects;
    private readonly IReadOnlyDictionary<KejiSmartQueryDialect,IKejiSmartQueryExecutor> _executors;
    private readonly KejiSmartQueryOptions _options;
    private readonly TimeProvider _time;

    public KejiSmartQueryService(
        ICurrentUserAccessor users,IKejiAuthorizationService authorization,
        IKejiSmartQueryDataSourceCatalog sources,IModelProviderRegistry providers,
        IKejiAuditService audit,IEnumerable<IKejiSmartQueryDialectCompiler> dialects,
        IEnumerable<IKejiSmartQueryExecutor> executors,KejiSmartQueryOptions options,
        TimeProvider? timeProvider=null)
    {
        _users=users;_authorization=authorization;_sources=sources;_providers=providers;
        _audit=new(audit);_options=options;_time=timeProvider??TimeProvider.System;
        _dialects=dialects.ToDictionary(static d=>d.Dialect);
        _executors=executors.ToDictionary(static e=>e.Dialect);
    }

    public async IAsyncEnumerable<KejiSmartQueryEvent> RunStreamAsync(
        KejiSmartQueryRequest request,[EnumeratorCancellation]CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var channel=Channel.CreateBounded<KejiSmartQueryEvent>(new BoundedChannelOptions(16)
        {SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
        var producer=ProduceAsync(request,channel.Writer,cancellationToken);
        try
        {
            await foreach(var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            try { await producer.ConfigureAwait(false); }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    public async Task<KejiSmartQueryResult> RunAsync(
        KejiSmartQueryRequest request,CancellationToken cancellationToken=default)
    {
        KejiSmartQueryResult? result=null;
        await foreach(var item in RunStreamAsync(request,cancellationToken).ConfigureAwait(false))
            if(item.Type==KejiSmartQueryEventType.Completed&&item.Result is not null) result=item.Result;
        return result??throw new OperationCanceledException(cancellationToken);
    }

    private async Task ProduceAsync(
        KejiSmartQueryRequest request,ChannelWriter<KejiSmartQueryEvent> writer,CancellationToken callerToken)
    {
        using var runTimeout=new CancellationTokenSource(_options.RunTimeout,_time);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(callerToken,runTimeout.Token);
        var ct=linked.Token; long sequence=0;
        var runId=ValidRunId(request.RunId)?request.RunId:"";
        var started=_time.GetTimestamp();
        string safeSource="";string safeUser="";
        async ValueTask Emit(KejiSmartQueryEventType type,KejiSmartQueryStatus status=0,
            KejiSmartQueryErrorCode error=0,KejiSmartQueryStopReason stop=0,KejiSmartQueryResult? result=null)=>
            await writer.WriteAsync(new(runId,++sequence,type,_time.GetUtcNow(),status,error,stop,result),ct).ConfigureAwait(false);
        async Task Complete(KejiSmartQueryResult result)
        {
            if(result.Status!=KejiSmartQueryStatus.Completed)
                await Emit(KejiSmartQueryEventType.Error,result.Status,ErrorFor(result.Status),result.StopReason).ConfigureAwait(false);
            await Emit(KejiSmartQueryEventType.Completed,result.Status,
                result.Status==KejiSmartQueryStatus.Completed?0:ErrorFor(result.Status),result.StopReason,result).ConfigureAwait(false);
        }
        async Task Audit(string action,KejiAuditOutcome outcome,IReadOnlyDictionary<string,string>? extra=null)
        {
            var data=new Dictionary<string,string>(StringComparer.Ordinal);
            if(ValidRunId(runId))data["RunId"]=runId;
            if(KejiSmartQueryValidation.IsIdentifier(safeUser,128))data["UserId"]=safeUser;
            data["DurationMs"]=_time.GetElapsedTime(started).TotalMilliseconds.ToString("F0",CultureInfo.InvariantCulture);
            if(extra is not null)foreach(var pair in extra)data[pair.Key]=pair.Value;
            await _audit.WriteAsync(action,outcome,safeSource,data).ConfigureAwait(false);
        }
        try
        {
            await Emit(KejiSmartQueryEventType.Started).ConfigureAwait(false);
            var user=_users.CurrentUser;
            if(!TryValidateRequest(request))
            {
                var failure=Failure(runId,KejiSmartQueryStatus.InvalidRequest,"SMART_QUERY_INVALID_REQUEST",KejiSmartQueryStopReason.InvalidRequest);
                await Audit("smart_query_failed",KejiAuditOutcome.Failure,new Dictionary<string,string>{{"SafeErrorCode",failure.SafeCode}}).ConfigureAwait(false);
                await Complete(failure).ConfigureAwait(false);return;
            }
            safeSource=request.DataSourceId;
            await Audit("smart_query_started",KejiAuditOutcome.Success,new Dictionary<string,string>
                {{"RequestedLimit",request.RequestedLimit.ToString(CultureInfo.InvariantCulture)}}).ConfigureAwait(false);
            if(user is null||!KejiSmartQueryValidation.IsIdentifier(user.Id,128))
            {
                var f=Failure(runId,KejiSmartQueryStatus.Unauthenticated,"SMART_QUERY_UNAUTHENTICATED",KejiSmartQueryStopReason.Rejected);
                await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
            }
            safeUser=user.Id;
            if(!_authorization.AuthorizeAll(user,[KejiPermission.SmartQueryExecute,KejiPermission.DatabaseRead]).IsAllowed)
            {
                var f=Failure(runId,KejiSmartQueryStatus.Forbidden,"SMART_QUERY_FORBIDDEN",KejiSmartQueryStopReason.Rejected);
                await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
            }
            var source=await _sources.GetAccessibleAsync(request.DataSourceId,user.Id,user.IsAdmin,ct).ConfigureAwait(false);
            if(source is null)
            {
                var f=Failure(runId,KejiSmartQueryStatus.DataSourceNotFound,"SMART_QUERY_DATA_SOURCE_NOT_FOUND",KejiSmartQueryStopReason.Rejected);
                await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
            }
            if(!MetadataWithinLimits(source)||!KejiSmartQueryValidation.IsSafeSource(source,source.Id,source.OwnerUserId))
            {
                var f=Failure(runId,KejiSmartQueryStatus.MetadataLimitExceeded,"SMART_QUERY_METADATA_LIMIT_EXCEEDED",KejiSmartQueryStopReason.Rejected);
                await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
            }
            await Emit(KejiSmartQueryEventType.MetadataLoaded).ConfigureAwait(false);
            await Audit("smart_query_metadata_loaded",KejiAuditOutcome.Success,new Dictionary<string,string>
            {
                {"Dialect",source.Dialect.ToString()},{"TableCount",source.Tables.Length.ToString(CultureInfo.InvariantCulture)},
                {"ColumnCount",source.Tables.Sum(static t=>t.Columns.Length).ToString(CultureInfo.InvariantCulture)}
            }).ConfigureAwait(false);
            var schema=BuildSchemaPrompt(source,request.RequestedLimit);
            if(ByteCount(schema)>_options.MaxSchemaBytes)
            {
                var f=Failure(runId,KejiSmartQueryStatus.MetadataLimitExceeded,"SMART_QUERY_METADATA_LIMIT_EXCEEDED",KejiSmartQueryStopReason.Rejected);
                await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
            }
            var provider=_providers.GetProvider(_options.ProviderName);
            if(provider is null)
            {
                var f=Failure(runId,KejiSmartQueryStatus.ProviderNotFound,"SMART_QUERY_PROVIDER_NOT_FOUND",KejiSmartQueryStopReason.Failed);
                await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
            }
            if(!_dialects.TryGetValue(source.Dialect,out var dialect))
            {
                var f=Failure(runId,KejiSmartQueryStatus.InvalidPlan,"SMART_QUERY_INVALID_PLAN",KejiSmartQueryStopReason.Failed);
                await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
            }
            await Emit(KejiSmartQueryEventType.PlanningStarted).ConfigureAwait(false);
            await Audit("smart_query_planning_started",KejiAuditOutcome.Success).ConfigureAwait(false);
            KejiSmartQueryPlan? plan;
            try
            {
                plan=await PlanAsync(provider,request.Question,schema,
                    candidate=>candidate.Limit<=request.RequestedLimit&&
                        dialect.TryCompile(candidate,source,_options,out _),ct).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested)
            {
                var f=Failure(runId,KejiSmartQueryStatus.PlanningFailed,"SMART_QUERY_PLANNING_FAILED",KejiSmartQueryStopReason.TimedOut);
                await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
            }
            if(plan is null||!dialect.TryCompile(plan,source,_options,out var compiled))
            {
                var f=Failure(runId,KejiSmartQueryStatus.InvalidPlan,"SMART_QUERY_INVALID_PLAN",KejiSmartQueryStopReason.Failed);
                await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
            }
            await Emit(KejiSmartQueryEventType.PlanValidated).ConfigureAwait(false);
            await Audit("smart_query_plan_validated",KejiAuditOutcome.Success,new Dictionary<string,string>
            {
                {"JoinCount",plan.Joins.Length.ToString(CultureInfo.InvariantCulture)},
                {"FilterCount",CountFilters(plan.Where).ToString(CultureInfo.InvariantCulture)},
                {"QueryFingerprint",compiled!.Fingerprint}
            }).ConfigureAwait(false);
            if(!_executors.TryGetValue(source.Dialect,out var executor))throw new InvalidOperationException();
            await Emit(KejiSmartQueryEventType.ExecutionStarted).ConfigureAwait(false);
            await Audit("smart_query_execution_started",KejiAuditOutcome.Success).ConfigureAwait(false);
            KejiSmartQueryResult result;
            using(var queryTimeout=new CancellationTokenSource(_options.QueryTimeout,_time))
            using(var queryLinked=CancellationTokenSource.CreateLinkedTokenSource(ct,queryTimeout.Token))
            {
                try { result=await executor.ExecuteAsync(runId,source,compiled,_options,queryLinked.Token).ConfigureAwait(false); }
                catch(OperationCanceledException) when(queryTimeout.IsCancellationRequested&&!ct.IsCancellationRequested)
                {
                    var f=Failure(runId,KejiSmartQueryStatus.QueryTimedOut,"SMART_QUERY_QUERY_TIMED_OUT",KejiSmartQueryStopReason.TimedOut);
                    await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);return;
                }
            }
            await Emit(KejiSmartQueryEventType.ExecutionCompleted).ConfigureAwait(false);
            await Audit("smart_query_execution_completed",KejiAuditOutcome.Success,ResultAudit(result,compiled.Fingerprint)).ConfigureAwait(false);
            if(_options.IncludeSummary)
            {
                await Emit(KejiSmartQueryEventType.SummaryStarted).ConfigureAwait(false);
                var summary=await SummarizeAsync(provider,result,ct).ConfigureAwait(false);
                result=result with{Summary=summary.Text,SummaryGenerated=summary.Generated};
                await Emit(KejiSmartQueryEventType.SummaryCompleted).ConfigureAwait(false);
                await Audit("smart_query_summary_completed",KejiAuditOutcome.Success,
                    new Dictionary<string,string>{{"Success",summary.Generated?"true":"false"}}).ConfigureAwait(false);
            }
            await Audit("smart_query_completed",KejiAuditOutcome.Success,ResultAudit(result,compiled.Fingerprint)).ConfigureAwait(false);
            await Complete(result).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(callerToken.IsCancellationRequested)
        {
            await Audit("smart_query_cancelled",KejiAuditOutcome.Failure).ConfigureAwait(false);
            writer.TryComplete(new OperationCanceledException(callerToken));return;
        }
        catch(OperationCanceledException) when(runTimeout.IsCancellationRequested)
        {
            var f=Failure(runId,KejiSmartQueryStatus.QueryTimedOut,"SMART_QUERY_QUERY_TIMED_OUT",KejiSmartQueryStopReason.TimedOut);
            await Failed(f).ConfigureAwait(false);
            using var terminal=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await writer.WriteAsync(new(runId,++sequence,KejiSmartQueryEventType.Error,_time.GetUtcNow(),f.Status,KejiSmartQueryErrorCode.QueryTimedOut,f.StopReason),terminal.Token).ConfigureAwait(false);
            await writer.WriteAsync(new(runId,++sequence,KejiSmartQueryEventType.Completed,_time.GetUtcNow(),f.Status,KejiSmartQueryErrorCode.QueryTimedOut,f.StopReason,f),terminal.Token).ConfigureAwait(false);
        }
        catch(KejiSmartQuerySecretUnavailableException)
        {
            var f=Failure(runId,KejiSmartQueryStatus.SecretUnavailable,"SMART_QUERY_SECRET_UNAVAILABLE",KejiSmartQueryStopReason.Failed);
            await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);
        }
        catch(KejiSmartQueryDatabaseTimeoutException)
        {
            var f=Failure(runId,KejiSmartQueryStatus.QueryTimedOut,"SMART_QUERY_QUERY_TIMED_OUT",KejiSmartQueryStopReason.TimedOut);
            await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);
        }
        catch
        {
            var f=Failure(runId,KejiSmartQueryStatus.ExecutionFailed,"SMART_QUERY_EXECUTION_FAILED",KejiSmartQueryStopReason.Failed);
            await Failed(f).ConfigureAwait(false);await Complete(f).ConfigureAwait(false);
        }
        finally{writer.TryComplete();}

        async Task Failed(KejiSmartQueryResult f)=>await Audit("smart_query_failed",KejiAuditOutcome.Failure,
            new Dictionary<string,string>{{"SafeErrorCode",f.SafeCode},{"Success","false"}}).ConfigureAwait(false);
    }

    private async Task<KejiSmartQueryPlan?> PlanAsync(
        IModelProvider provider,string question,string schema,
        Func<KejiSmartQueryPlan,bool> isValid,CancellationToken outer)
    {
        string? first=null;
        for(var attempt=0;attempt<2;attempt++)
        {
            using var timeout=new CancellationTokenSource(_options.PlannerTimeout,_time);
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(outer,timeout.Token);
            var instruction=attempt==0?schema:
                schema+"\nRepair the prior invalid plan. Error category: invalid_plan. Prior bounded output:"+
                Bound(first??"",_options.MaxPlanBytes);
            var response=await provider.CompleteAsync(new()
            {
                Model=_options.Model,Temperature=0,MaxTokens=4096,
                Messages=[new(){Role=KejiChatRole.System,Content=instruction},
                    new(){Role=KejiChatRole.User,Content=JsonSerializer.Serialize(new{question})}]
            },linked.Token).ConfigureAwait(false);
            first=response.Content;
            if(response.Success&&TryParsePlan(response.Content,out var plan)&&plan is not null&&isValid(plan))return plan;
            if(timeout.IsCancellationRequested&&!outer.IsCancellationRequested)throw new OperationCanceledException(timeout.Token);
        }
        return null;
    }

    private async Task<(string Text,bool Generated)> SummarizeAsync(
        IModelProvider provider,KejiSmartQueryResult result,CancellationToken ct)
    {
        var fallback=$"查询完成，共返回{result.RowsReturned}行、{result.Columns.Length}列。";
        var payload=JsonSerializer.Serialize(new{columns=result.Columns,rows=result.Rows.Take(20)});
        if(ByteCount(payload)>64*1024)return(fallback,false);
        try
        {
            var response=await provider.CompleteAsync(new()
            {
                Model=_options.Model,Temperature=0,MaxTokens=1024,
                Messages=[new(){Role=KejiChatRole.System,Content="Summarize untrusted data only. Plain text."},
                    new(){Role=KejiChatRole.User,Content=payload}]
            },ct).ConfigureAwait(false);
            return response.Success&&SafeText(response.Content,8192,true)?(response.Content!,true):(fallback,false);
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return(fallback,false);}
        catch(Exception exception)when(exception is not OperationCanceledException){return(fallback,false);}
    }

    private bool TryParsePlan(string? content,out KejiSmartQueryPlan? plan)
    {
        plan=null;try
        {
            if(string.IsNullOrWhiteSpace(content)||ByteCount(content)>_options.MaxPlanBytes)return false;
            using var doc=JsonDocument.Parse(content,new(){AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow,MaxDepth=12});
            if(doc.RootElement.ValueKind!=JsonValueKind.Object||!Unique(doc.RootElement))return false;
            plan=JsonSerializer.Deserialize<KejiSmartQueryPlan>(content,PlanJson);return plan is not null;
        }catch(Exception e)when(e is JsonException or EncoderFallbackException){return false;}
    }
    private static bool Unique(JsonElement e)
    {if(e.ValueKind==JsonValueKind.Object){var n=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject())if(!n.Add(p.Name)||!Unique(p.Value))return false;}
     else if(e.ValueKind==JsonValueKind.Array)foreach(var i in e.EnumerateArray())if(!Unique(i))return false;return true;}
    private string BuildSchemaPrompt(KejiSmartQueryDataSource s,int limit)=>
        "Return one QueryPlan JSON, never SQL. Allowed operators include between. Schema:"+
        JsonSerializer.Serialize(new{limit,tables=s.Tables.Where(t=>t.QaEnabled&&t.QueryEnabled&&s.AllowedSchemas.Contains(t.SchemaName))
            .Select(t=>new{schema=t.SchemaName,name=t.Name,t.DisplayName,t.Description,t.BusinessContext,
                columns=t.Columns.Where(c=>c.QueryEnabled&&!c.Sensitive).Select(c=>new{c.Name,c.Type,c.Nullable,c.Description})}),
            foreignKeys=s.ForeignKeys.Where(f=>ForeignKeyIsQueryable(s,f))});
    private static bool ForeignKeyIsQueryable(KejiSmartQueryDataSource s,KejiSmartQueryForeignKey f)=>
        f.QueryEnabled&&s.Tables.Any(t=>t.QaEnabled&&t.QueryEnabled&&t.Name==f.PrincipalTable&&t.Columns.Any(c=>c.Name==f.PrincipalColumn&&c.QueryEnabled&&!c.Sensitive))&&
        s.Tables.Any(t=>t.QaEnabled&&t.QueryEnabled&&t.Name==f.DependentTable&&t.Columns.Any(c=>c.Name==f.DependentColumn&&c.QueryEnabled&&!c.Sensitive));
    private bool MetadataWithinLimits(KejiSmartQueryDataSource s)=>s.Tables.Length<=_options.MaxTables&&
        s.Tables.Sum(static t=>t.Columns.Length)<=_options.MaxTotalColumns&&
        s.Tables.All(t=>t.Columns.Length<=_options.MaxColumnsPerTable)&&s.ForeignKeys.Length<=_options.MaxForeignKeys;
    private bool TryValidateRequest(KejiSmartQueryRequest r)=>ValidRunId(r.RunId)&&
        KejiSmartQueryValidation.IsIdentifier(r.DataSourceId,128)&&SafeText(r.Question,_options.MaxQuestionBytes,true)&&
        r.RequestedLimit is>=1 and<=KejiSmartQueryOptions.SystemMaxRows;
    private static bool ValidRunId(string v)=>v.Length==32&&v.All(static c=>c is>='0'and<='9'or>='a'and<='f');
    private static bool SafeText(string? v,int max,bool bytes)
    {if(string.IsNullOrWhiteSpace(v)||v.Any(char.IsControl))return false;try{return bytes?ByteCount(v)<=max:v.Length<=max&&ByteCount(v)>0;}catch(EncoderFallbackException){return false;}}
    private static int ByteCount(string v)=>StrictUtf8.GetByteCount(v);
    private static string Bound(string v,int bytes){while(v.Length>0&&ByteCount(v)>bytes)v=v[..^1];return v;}
    private static int CountFilters(KejiSmartQueryFilterNode? n)=>n is null?0:n.Filter is null?n.Group!.Children.Sum(CountFilters):1;
    private static KejiSmartQueryResult Failure(string runId,KejiSmartQueryStatus status,string code,KejiSmartQueryStopReason reason)=>
        new(runId,status,[],[],0,false,0,0,0,false,"",code,reason);
    private static KejiSmartQueryErrorCode ErrorFor(KejiSmartQueryStatus s)=>s switch
    {
        KejiSmartQueryStatus.InvalidRequest=>KejiSmartQueryErrorCode.InvalidRequest,
        KejiSmartQueryStatus.Unauthenticated=>KejiSmartQueryErrorCode.Unauthenticated,
        KejiSmartQueryStatus.Forbidden=>KejiSmartQueryErrorCode.Forbidden,
        KejiSmartQueryStatus.DataSourceNotFound=>KejiSmartQueryErrorCode.DataSourceNotFound,
        KejiSmartQueryStatus.MetadataLimitExceeded=>KejiSmartQueryErrorCode.MetadataLimitExceeded,
        KejiSmartQueryStatus.ProviderNotFound=>KejiSmartQueryErrorCode.ProviderNotFound,
        KejiSmartQueryStatus.SecretUnavailable=>KejiSmartQueryErrorCode.SecretUnavailable,
        KejiSmartQueryStatus.InvalidPlan=>KejiSmartQueryErrorCode.InvalidPlan,
        KejiSmartQueryStatus.PlanningFailed=>KejiSmartQueryErrorCode.PlanningFailed,
        KejiSmartQueryStatus.QueryTimedOut=>KejiSmartQueryErrorCode.QueryTimedOut,
        _=>KejiSmartQueryErrorCode.ExecutionFailed
    };
    private static IReadOnlyDictionary<string,string> ResultAudit(KejiSmartQueryResult r,string fp)=>new Dictionary<string,string>
    {
        {"RowsReturned",r.RowsReturned.ToString(CultureInfo.InvariantCulture)},
        {"RowsTruncated",r.RowsTruncated?"true":"false"},{"CellsTruncated",r.CellsTruncated.ToString(CultureInfo.InvariantCulture)},
        {"ResultBytes",r.ResultBytes.ToString(CultureInfo.InvariantCulture)},
        {"QueryFingerprint",fp},{"Success","true"}
    };
}
