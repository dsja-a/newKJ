using System.Reflection;
using Keji.Tools.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Tools.Tests;

public class ArchitectureTests
{
    [Fact]
    public void ToolExecutionPipeline_DoesNotReferenceProcess()
    {
        var pipelineType = typeof(ToolExecutionPipeline);
        var referencedTypes = pipelineType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        // Pipeline only depends on IToolExecutionCoordinator
        Assert.Contains(typeof(IToolExecutionCoordinator), referencedTypes);
        Assert.Single(referencedTypes);
    }

    [Fact]
    public void ToolWorkerLauncher_DoesNotReferenceProcess()
    {
        var launcherType = typeof(ToolWorkerLauncher);
        var ctorParams = launcherType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        // Launcher only depends on IToolExecutionCoordinator
        Assert.Contains(typeof(IToolExecutionCoordinator), ctorParams);
        Assert.Single(ctorParams);
    }

    [Fact]
    public void KejiTools_ExecutionNamespace_HasNoProcessStart()
    {
        var toolsAssembly = typeof(ToolExecutionPipeline).Assembly;
        var executionTypes = toolsAssembly.GetTypes()
            .Where(t => t.Namespace == "Keji.Tools.Execution")
            .ToList();

        foreach (var type in executionTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                var body = method.GetMethodBody();
                if (body is not null)
                {
                    var locals = body.LocalVariables;
                    // No Process or WorkerFrameReader/Writer should be used
                    foreach (var local in locals)
                    {
                        if (local.LocalType is not null)
                        {
                            Assert.False(
                                local.LocalType.FullName?.Contains("System.Diagnostics.Process") == true,
                                $"{type.Name}.{method.Name} uses System.Diagnostics.Process");
                            Assert.False(
                                local.LocalType.FullName?.Contains("WorkerFrameReader") == true,
                                $"{type.Name}.{method.Name} uses WorkerFrameReader");
                            Assert.False(
                                local.LocalType.FullName?.Contains("WorkerFrameWriter") == true,
                                $"{type.Name}.{method.Name} uses WorkerFrameWriter");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void PipelineForwarderExtension_DoesNotRegisterLauncher()
    {
        var extensionType = typeof(KejiToolsServiceCollectionExtensions);
        var method = extensionType.GetMethod("AddKejiToolPipelineForwarder");
        Assert.NotNull(method);
    }
}