namespace KeyStats.Storage;

public sealed record MinuteBucketSnapshot(long BucketStartUtc, uint[] Counts)
{
    public long TotalCount => Counts.Aggregate<uint, long>(0, (total, count) => total + count);

    public MinuteBucketSnapshot Copy() => new(BucketStartUtc, (uint[])Counts.Clone());
}
