using Keji.Api.HostedServices;
using Keji.Api.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Keji.Configuration.Loading;
using Keji.Configuration.Models;
using Keji.Configuration.Secrets;
using Keji.Persistence;
using Keji.Security.Middleware;
using Keji.Security.Options;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddHostedService<KejiStartupInitializer>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<KejiApiExceptionMiddleware>();
app.UseMiddleware<KejiAuthenticationMiddleware>();
app.MapControllers();

app.Run();

public partial class Program { }
