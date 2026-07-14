using Keji.Tools.Names;
using Keji.Tools.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Tools.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddKejiToolRegistry_RegistersIKejiToolRegistry()
    {
        var services = new ServiceCollection();
        services.AddKejiToolRegistry();
        var provider = services.BuildServiceProvider();
        var registry = provider.GetService<IKejiToolRegistry>();
        Assert.NotNull(registry);
    }

    [Fact]
    public void AddKejiToolRegistry_RegistryIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddKejiToolRegistry();
        var provider = services.BuildServiceProvider();
        var r1 = provider.GetService<IKejiToolRegistry>();
        var r2 = provider.GetService<IKejiToolRegistry>();
        Assert.Same(r1, r2);
    }

    [Fact]
    public void AddKejiToolRegistry_RegistryContainsBuiltinTools()
    {
        var services = new ServiceCollection();
        services.AddKejiToolRegistry();
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKejiToolRegistry>();
        Assert.True(registry.Contains(KejiToolName.Create("read_file")));
        Assert.True(registry.Contains(KejiToolName.Create("write_file")));
        Assert.True(registry.Contains(KejiToolName.Create("web_search")));
    }
}
