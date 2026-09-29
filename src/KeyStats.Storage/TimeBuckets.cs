namespace KeyStats.Storage;

public static class TimeBuckets
{
    public const int MinuteSeconds = 60;
    public const int TenMinuteSeconds = 600;

    public static long ToMinuteStart(DateTimeOffset utcTime) =>
        AlignDown(utcTime.ToUnixTimeSeconds(), MinuteSeconds);

    public static long ToTenMinuteStart(long unixSeconds) =>
        AlignDown(unixSeconds, TenMinuteSeconds);

    public static long AlignDown(long value, int interval)
    {
        if (interval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        var remainder = value % interval;
        return remainder < 0 ? value - remainder - interval : value - remainder;
    }

    public static long AlignUp(long value, int interval)
    {
        var aligned = AlignDown(value, interval);
        return aligned == value ? value : checked(aligned + interval);
    }
}

