namespace Keji.Persistence;

public interface IKejiDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
