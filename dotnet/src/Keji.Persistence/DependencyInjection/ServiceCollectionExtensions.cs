using Keji.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddKejiPersistenceFoundation(
        this IServiceCollection services)
    {
        return AddKejiPersistenceFoundation(services, _ => { });
    }

    public static IServiceCollection AddKejiPersistenceFoundation(
        this IServiceCollection services,
        Action<KejiPersistenceOptions> configureOptions)
    {
        var options = new KejiPersistenceOptions();
        configureOptions(options);
        services.AddSingleton(options);

        services.AddSingleton<IUnixTimeProvider, UnixTimeProvider>();
        services.AddSingleton<ISqliteConnectionFactory>(sp =>
        {
            var opts = sp.GetRequiredService<KejiPersistenceOptions>();
            return new SqliteConnectionFactory(opts);
        });
        services.AddSingleton<IKejiDatabaseInitializer, KejiDatabaseInitializer>();
        services.AddSingleton<IUserRepository, SqliteUserRepository>();
        services.AddSingleton<IConversationRepository, SqliteConversationRepository>();
        services.AddSingleton<IMessageRepository, SqliteMessageRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();

        return services;
    }
}
