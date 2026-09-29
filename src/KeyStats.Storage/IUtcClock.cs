namespace KeyStats.Storage;

public interface IUtcClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemUtcClock : IUtcClock
{
    public static SystemUtcClock Instance { get; } = new();

    private SystemUtcClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
