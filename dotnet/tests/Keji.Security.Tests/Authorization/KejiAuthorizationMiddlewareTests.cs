using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.Security.Exceptions;
using Keji.Security.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keji.Security.Tests.Authorization;

public sealed class KejiAuthorizationMiddlewareTests
{
    [Fact] public async Task Disabled_CallsNextOnce() { var c=Context(); var n=0; await Execute(c,null,new KejiSecurityOptions { Enabled=false },_=>{n++;return Task.CompletedTask;}); Assert.Equal(1,n); }
    [Fact] public async Task NullEndpoint_CallsNextOnce() { var c=Context(); var n=0; await Execute(c,null,new KejiSecurityOptions(),_=>{n++;return Task.CompletedTask;}); Assert.Null(c.GetEndpoint()); Assert.Equal(1,n); }
    [Fact] public async Task CustomAnonymous_CallsNext() { var n=0; await Execute(Context(new object[]{new KejiAllowAnonymousAttribute()}),null,new KejiSecurityOptions(),_=>{n++;return Task.CompletedTask;}); Assert.Equal(1,n); }
    [Fact] public async Task StandardAnonymous_CallsNext() { var n=0; await Execute(Context(new object[]{new AllowAnonymousAttribute()}),null,new KejiSecurityOptions(),_=>{n++;return Task.CompletedTask;}); Assert.Equal(1,n); }
    [Fact] public async Task MissingMetadata_Is403() => await AssertDenied(Array.Empty<object>(), null, 403, "权限不足");
    [Fact] public async Task PermissionWithoutUser_Is401() => await AssertDenied(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AccountSelfRead)}, null, 401, "未登录，请先登录");
    [Theory] [InlineData("admin")] [InlineData("member")] [InlineData("readonly")]
    public async Task AccountRead_AllRolesAllow(string role) => await AssertAllow(KejiPermission.AccountSelfRead, User(role));
    [Theory] [InlineData("member")] [InlineData("readonly")]
    public async Task AdminOnly_NonAdminDenied(string role) => await AssertDenied(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AdminUsers)}, User(role), 403, "需要管理员权限");
    [Fact] public async Task ReadonlyWrite_Denied() => await AssertDenied(new object[]{new KejiRequirePermissionAttribute(KejiPermission.FileWrite)}, User("readonly"), 403, "当前账号无写入权限");
    [Fact] public async Task Multiple_AllSatisfied() => await AssertAllow(new[]{KejiPermission.FileRead,KejiPermission.FileWrite}, User("admin"));
    [Theory] [InlineData("member")] [InlineData("readonly")]
    public async Task MultipleAdminOnly_Denied(string role) => await AssertDenied(new object[]{new KejiRequirePermissionAttribute(KejiPermission.FileRead),new KejiRequirePermissionAttribute(KejiPermission.AdminUsers)}, User(role), 403, "需要管理员权限");
    [Fact] public async Task OrdinaryWriteCombination_ReadonlyDenied() => await AssertDenied(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AccountSelfRead),new KejiRequirePermissionAttribute(KejiPermission.FileWrite)}, User("readonly"), 403, "当前账号无写入权限");
    [Fact] public async Task Conflict_Throws() => await Assert.ThrowsAsync<KejiSecurityConfigurationException>(() => Execute(new[]{(object)new KejiAllowAnonymousAttribute(),new KejiRequirePermissionAttribute(KejiPermission.AccountSelfRead)}, User("admin")));
    [Fact] public async Task InvalidPermission_Secure403() => await AssertDenied(new object[]{new KejiRequirePermissionAttribute((KejiPermission)999)}, User("admin"), 403, "权限不足");
    [Fact] public async Task GetDoesNotInferRead() => await AssertDenied(Array.Empty<object>(), User("admin"), 403, "权限不足");
    [Fact] public async Task PostDoesNotInferWrite() => await AssertDenied(Array.Empty<object>(), User("admin"), 403, "权限不足", HttpMethods.Post);
    [Fact] public async Task Denial_DoesNotCallNext() => await AssertDenied(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AdminUsers)}, User("member"), 403, "需要管理员权限");
    [Fact] public async Task StartedResponse_IsUntouched() { var c=Context(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AdminUsers)}); c.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedFeature()); c.Response.StatusCode=200; c.Response.ContentType="text/plain"; c.Response.Headers["X-Test"]="before"; Assert.True(c.Response.HasStarted); await Execute(c,User("member")); Assert.Equal(200,c.Response.StatusCode); Assert.Equal("text/plain",c.Response.ContentType); Assert.Equal("before",c.Response.Headers["X-Test"]); Assert.Equal(0,c.Response.Body.Length); }
    [Fact] public async Task UserPrincipal_IsUntouched() { var c=Context(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AccountSelfRead)}); c.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("x")); await Execute(c,User("admin")); Assert.Equal("x",c.User.Identity!.AuthenticationType); }
    [Fact] public async Task Cancellation_Propagates() { var c=Context(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AccountSelfRead)}); c.RequestAborted=new CancellationToken(true); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Execute(c,null)); }
    [Fact] public async Task NextOnlyOnce() { var n=0; var c=Context(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AccountSelfRead)}); await Execute(c,User("admin"),new KejiSecurityOptions(),_=>{n++;return Task.CompletedTask;}); Assert.Equal(1,n); }
    [Fact] public async Task AllowAnonymous_NextOnlyOnce() { var n=0; var c=Context(new object[]{new KejiAllowAnonymousAttribute()}); await Execute(c,null,new KejiSecurityOptions(),_=>{n++;return Task.CompletedTask;}); Assert.Equal(1,n); }
    [Fact] public async Task Denied_NextZero() { var n=0; var c=Context(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AdminUsers)}); await Execute(c,User("member"),new KejiSecurityOptions(),_=>{n++;return Task.CompletedTask;}); Assert.Equal(0,n); }
    [Fact] public async Task ParallelRequests_AreIsolated() { var results=await Task.WhenAll(ExecuteResult(User("admin"),KejiPermission.FileRead),ExecuteResult(User("member"),KejiPermission.AdminUsers),ExecuteResult(User("readonly"),KejiPermission.FileWrite),ExecuteResult(null,KejiPermission.AccountSelfRead)); Assert.Equal(200,results[0].Status); Assert.Equal(403,results[1].Status); Assert.Equal(403,results[2].Status); Assert.Equal(401,results[3].Status); Assert.Contains("需要管理员权限",results[1].Body); Assert.Contains("当前账号无写入权限",results[2].Body); Assert.Contains("未登录",results[3].Body); }

    private static CurrentUser User(string role)=>new("id","user",role,"User",KejiAuthenticationKind.Jwt);
    private static DefaultHttpContext Context(IEnumerable<object>? metadata=null,string method="GET") { var c=new DefaultHttpContext(); c.Request.Method=method; c.Response.Body=new MemoryStream(); if(metadata!=null)c.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(metadata),"test")); return c; }
    private static async Task AssertAllow(KejiPermission p,CurrentUser u)=>await AssertAllow(new[]{p},u);
    private static async Task AssertAllow(IEnumerable<KejiPermission> ps,CurrentUser u){var c=Context(ps.Select(p=>(object)new KejiRequirePermissionAttribute(p)));var called=0;await Execute(c,u,new KejiSecurityOptions(),_=>{called++;return Task.CompletedTask;});Assert.Equal(1,called);}
    private static async Task AssertDenied(IEnumerable<object> m,CurrentUser? u,int status,string body,string method="GET"){var c=Context(m,method);await Execute(c,u);Assert.Equal(status,c.Response.StatusCode);Assert.Equal("application/json",c.Response.ContentType);c.Response.Body.Position=0;Assert.Equal($"{{\"detail\":\"{body}\"}}",await new StreamReader(c.Response.Body).ReadToEndAsync());}
    private static Task Execute(DefaultHttpContext c,CurrentUser? u,KejiSecurityOptions? options=null,RequestDelegate? next=null)=>new KejiAuthorizationMiddleware(next??(_=>Task.CompletedTask)).InvokeAsync(c,options??new KejiSecurityOptions(),new Accessor(u),new KejiAuthorizationService(new KejiRolePermissionMatrix()),NullLogger<KejiAuthorizationMiddleware>.Instance);
    private static Task Execute(IEnumerable<object> m,CurrentUser? u)=>Execute(Context(m),u);
    private static async Task<(int Status,string Body)> ExecuteResult(CurrentUser? u,KejiPermission p){var c=Context(new object[]{new KejiRequirePermissionAttribute(p)}); await Execute(c,u); c.Response.Body.Position=0; return (c.Response.StatusCode,await new StreamReader(c.Response.Body).ReadToEndAsync());}
    private sealed class Accessor(CurrentUser? u):ICurrentUserAccessor { public CurrentUser? CurrentUser=>u; }
    private sealed class StartedFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature { public int StatusCode{get;set;}=200; public string? ReasonPhrase{get;set;} public IHeaderDictionary Headers{get;set;}=new HeaderDictionary(); public Stream Body{get;set;}=new MemoryStream(); public bool HasStarted=>true; public void OnStarting(Func<object,Task> callback,object state){} public void OnCompleted(Func<object,Task> callback,object state){} }
}
