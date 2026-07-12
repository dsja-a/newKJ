namespace Keji.Persistence;

public interface IUnixTimeProvider
{
    double Now { get; }
}

public class UnixTimeProvider : IUnixTimeProvider
{
    public double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
}
