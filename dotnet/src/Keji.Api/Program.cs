using Keji.Api.HostedServices;
using Keji.Api.Middleware;
using Keji.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Keji.Configuration.Loading;
using Keji.Configuration.Models;
using Keji.Configuration.Secrets;
using Keji.Persistence;
using Keji.Security.Middleware;
using Keji.Security.Authorization;
using Keji.Security.Options;
using Keji.Api.Takeover;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Keji.SmartQuery;
using Keji.Tools.Execution;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize=KejiApiBoundaryMiddleware.MaximumRequestBodyBytes;
    options.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout=TimeSpan.FromSeconds(120);
});

var projectRoot = builder.Configuration["Keji:ProjectRoot"]
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

builder.Services.AddKejiConfigurationFoundation(o =>
{
    o.ProjectRoot = projectRoot;
    o.RequireConfigFile = false;
    o.FailOnMissingEnvironmentVariable = false;
});

var yamlLoader = new SafeYamlConfigurationLoader();
var configLoader = new KejiConfigurationLoader(yamlLoader);
var loadOptions = new KejiConfigurationLoadOptions
{
    ProjectRoot = projectRoot,
    RequireConfigFile = false,
    FailOnMissingEnvironmentVariable = false,
};
var configResult = configLoader.Load(loadOptions);
var config = configResult.Document;

var persistenceOptions = KejiPersistenceOptions.FromConfiguration(config, projectRoot);
builder.Services.AddKejiPersistenceFoundation(o =>
{
    o.ProjectRoot = persistenceOptions.ProjectRoot;
    o.DatabasePath = persistenceOptions.DatabasePath;
});

var securityOptions = KejiSecurityOptions.FromConfiguration(config);
builder.Services.AddKejiSecurityFoundation(securityOptions);
builder.Services.AddKejiAuditingFoundation();
builder.Services.AddKejiProviders(static _ => { });
builder.Services.AddKejiSmartQuery();
builder.Services.AddKejiToolRegistry();
builder.Services.AddSingleton<IToolExecutionPipeline,KejiApiToolExecutionPipeline>();
builder.Services.AddKejiAgentLoop();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<Keji.Auditing.Abstractions.IKejiAuditCorrelationAccessor, Keji.Api.Middleware.KejiCorrelationAccessor>();
builder.Services.AddScoped<Keji.Security.Auth.IKejiAuditBridge, Keji.Api.Middleware.KejiAuditBridgeImpl>();

builder.Services.AddScoped<IKejiConversationService, KejiConversationService>();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.MaxDepth=32;
});
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.MaxDepth = 32;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = KejiApiBoundaryMiddleware.MaximumRequestBodyBytes;
    options.ValueLengthLimit = 64 * 1024;
    options.MultipartHeadersLengthLimit = 16 * 1024;
});
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext,string>(static _ =>
        RateLimitPartition.GetConcurrencyLimiter("global",static _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 128,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = static async (context, cancellationToken) =>
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type="about:blank",title="Too many requests",status=429,
            code="RATE_LIMITED",correlationId=context.HttpContext.TraceIdentifier
        },cancellationToken).ConfigureAwait(false);
});

builder.Services.AddHostedService<KejiStartupInitializer>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi()
        .WithMetadata(new KejiRequirePermissionAttribute(KejiPermission.SystemRead));
}

app.UseMiddleware<KejiApiExceptionMiddleware>();
app.UseMiddleware<KejiApiBoundaryMiddleware>();
app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<KejiAuthenticationMiddleware>();
app.UseMiddleware<KejiAuthorizationMiddleware>();
app.UseMiddleware<KejiApiAuditMiddleware>();
app.MapControllers();
app.MapKejiApiTakeover();

app.Run();

public partial class Program { }
