using Keji.Api.HostedServices;
using Keji.Configuration.Loading;
using Keji.Configuration.Models;
using Keji.Configuration.Secrets;
using Keji.Persistence;
using Keji.Security.Exceptions;
using Keji.Security.Middleware;
using Keji.Security.Options;

var builder = WebApplication.CreateBuilder(args);

var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

var loadOptions = new KejiConfigurationLoadOptions
{
    ProjectRoot = projectRoot,
    RequireConfigFile = false,
    FailOnMissingEnvironmentVariable = false,
};

var yamlLoader = new SafeYamlConfigurationLoader();
var dotEnvStore = new DotEnvStore(Path.Combine(projectRoot, ".env"));
var envSource = new CompositeEnvironmentValueSource(
    new ProcessEnvironmentValueSource(),
    new DotEnvEnvironmentValueSource(dotEnvStore));
var configLoader = new KejiConfigurationLoader(yamlLoader);

var configResult = configLoader.Load(loadOptions);
var config = configResult.Document;

var persistenceOptions = KejiPersistenceOptions.FromConfiguration(config, projectRoot);
builder.Services.AddKejiPersistenceFoundation(o =>
{
    o.ProjectRoot = persistenceOptions.ProjectRoot;
    o.DatabasePath = persistenceOptions.DatabasePath;
});

KejiSecurityOptions securityOptions;
try
{
    securityOptions = KejiSecurityOptions.FromConfiguration(config);
}
catch (KejiSecurityConfigurationException ex)
{
    Console.Error.WriteLine($"[WARNING] Security configuration error: {ex.Message}. Security will be disabled.");
    securityOptions = new KejiSecurityOptions { Enabled = false };
}
builder.Services.AddKejiSecurityFoundation(securityOptions);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddHostedService<KejiStartupInitializer>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<KejiAuthenticationMiddleware>();
app.MapControllers();

app.Run();

public partial class Program { }
