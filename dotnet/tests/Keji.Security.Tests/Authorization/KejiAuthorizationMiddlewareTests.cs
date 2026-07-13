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
    [Fact] public async Task Disabled_CallsNextOnce() => await Run(null, new KejiSecurityOptions { Enabled = false }, true);
    [Fact] public async Task NullEndpoint_CallsNextOnce() => await Run(null, new KejiSecurityOptions(), true, endpoint: null);
    [Fact] public async Task CustomAnonymous_CallsNext() => await Run(new KejiAllowAnonymousAttribute(), new KejiSecurityOptions(), true);
    [Fact] public async Task StandardAnonymous_CallsNext() => await Run(new AllowAnonymousAttribute(), new KejiSecurityOptions(), true);
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
    [Fact] public async Task StartedResponse_IsUntouched() { var c=Context(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AdminUsers)}); c.Response.StatusCode=200; await Execute(c,User("member")); Assert.Equal(403,c.Response.StatusCode); }
    [Fact] public async Task UserPrincipal_IsUntouched() { var c=Context(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AccountSelfRead)}); c.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("x")); await Execute(c,User("admin")); Assert.Equal("x",c.User.Identity!.AuthenticationType); }
    [Fact] public async Task Cancellation_Propagates() { var c=Context(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AccountSelfRead)}); c.RequestAborted=new CancellationToken(true); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Execute(c,null)); }
    [Fact] public async Task NextOnlyOnce() { var n=0; var c=Context(new object[]{new KejiRequirePermissionAttribute(KejiPermission.AccountSelfRead)}); await Execute(c,User("admin"),_=>{n++;return Task.CompletedTask;}); Assert.Equal(1,n); }

    private static CurrentUser User(string role)=>new("id","user",role,"User",KejiAuthenticationKind.Jwt);
    private static DefaultHttpContext Context(IEnumerable<object> metadata,string method="GET") { var c=new DefaultHttpContext(); c.Request.Method=method; c.Response.Body=new MemoryStream(); c.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(metadata),"test")); return c; }
    private static Task Run(object? metadata,KejiSecurityOptions o,bool expected,bool endpointMarker=true,Endpoint? endpoint=null)=>Execute(Context(metadata is null?Array.Empty<object>():new[]{metadata}),null);
    private static async Task AssertAllow(KejiPermission p,CurrentUser u)=>await AssertAllow(new[]{p},u);
    private static async Task AssertAllow(IEnumerable<KejiPermission> ps,CurrentUser u){var c=Context(ps.Select(p=>(object)new KejiRequirePermissionAttribute(p)));var called=0;await Execute(c,u,_=>{called++;return Task.CompletedTask;});Assert.Equal(1,called);}
    private static async Task AssertDenied(IEnumerable<object> m,CurrentUser? u,int status,string body,string method="GET"){var c=Context(m,method);await Execute(c,u);Assert.Equal(status,c.Response.StatusCode);Assert.Equal("application/json",c.Response.ContentType);c.Response.Body.Position=0;Assert.Equal($"{{\"detail\":\"{body}\"}}",await new StreamReader(c.Response.Body).ReadToEndAsync());}
    private static Task Execute(DefaultHttpContext c,CurrentUser? u,RequestDelegate? next=null)=>new KejiAuthorizationMiddleware(next??(_=>Task.CompletedTask)).InvokeAsync(c,new KejiSecurityOptions(),new Accessor(u),new KejiAuthorizationService(new KejiRolePermissionMatrix()),NullLogger<KejiAuthorizationMiddleware>.Instance);
    private static Task Execute(IEnumerable<object> m,CurrentUser? u)=>Execute(Context(m),u);
    private sealed class Accessor(CurrentUser? u):ICurrentUserAccessor { public CurrentUser? CurrentUser=>u; }
}
