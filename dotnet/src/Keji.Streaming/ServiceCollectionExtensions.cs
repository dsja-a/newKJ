namespace Microsoft.Extensions.DependencyInjection;

public static class KejiStreamingServiceCollectionExtensions
{
    public static IServiceCollection AddKejiStreaming(this IServiceCollection services)
    {
        // Keji.Streaming is stateless - all public API is static methods
        // Registration reserved for future DI needs
        return services;
    }
}
