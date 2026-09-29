namespace KeyStats.Core;

public sealed record KeyboardSnapshot(
    long TotalCount,
    IReadOnlyList<long> Counts,
    IReadOnlySet<KeyId> PressedKeys,
    bool IsPaused);

