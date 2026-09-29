namespace KeyStats.Storage;

public sealed record TenMinuteBucketTotal(long BucketStartUtc, ulong TotalCount);
