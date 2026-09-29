namespace KeyStats.Storage;

public sealed record StatisticsAggregate(long StartUtc, long EndUtc, ulong[] Counts)
{
    public ulong TotalCount => Counts.Aggregate<ulong, ulong>(0, (total, count) => total + count);
}

