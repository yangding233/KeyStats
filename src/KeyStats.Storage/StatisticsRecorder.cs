using System.Threading.Channels;
using KeyStats.Core;

namespace KeyStats.Storage;

public sealed class StatisticsRecorder : IAsyncDisposable
{
    private readonly KeyStatsRepository _repository;
    private readonly IUtcClock _clock;
    private readonly TimeSpan _flushInterval;
    private readonly object _bucketGate = new();
    private readonly Channel<SaveCommand> _saveQueue;
    private readonly CancellationTokenSource _timerCancellation = new();
    private readonly Task _saveWorker;
    private readonly Task _timerWorker;
    private long _currentMinuteStart;
    private uint[] _currentCounts;
    private long _lastSavedUnixSeconds;
    private int _disposed;
    private string? _lastError;

    private StatisticsRecorder(
        KeyStatsRepository repository,
        IUtcClock clock,
        TimeSpan flushInterval,
        MinuteBucketSnapshot initialBucket)
    {
        _repository = repository;
        _clock = clock;
        _flushInterval = flushInterval;
        _currentMinuteStart = initialBucket.BucketStartUtc;
        _currentCounts = (uint[])initialBucket.Counts.Clone();
        _saveQueue = Channel.CreateUnbounded<SaveCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _saveWorker = Task.Run(ProcessSaveQueueAsync);
        _timerWorker = Task.Run(PeriodicFlushAsync);
    }

    public DateTimeOffset? LastSavedUtc
    {
        get
        {
            var value = Interlocked.Read(ref _lastSavedUnixSeconds);
            return value == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(value);
        }
    }

    public string? LastError => Volatile.Read(ref _lastError);

    public static async Task<StatisticsRecorder> CreateAsync(
        KeyStatsRepository repository,
        TimeSpan flushInterval,
        IUtcClock? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (flushInterval < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(flushInterval));
        }

        clock ??= SystemUtcClock.Instance;
        var currentMinuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
        var initialBucket = await repository.GetMinuteBucketAsync(currentMinuteStart, cancellationToken)
            ?? new MinuteBucketSnapshot(currentMinuteStart, new uint[KeyCatalog.Count]);
        return new StatisticsRecorder(repository, clock, flushInterval, initialBucket);
    }

    public void RecordKeyPress(KeyId keyId)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        MinuteBucketSnapshot? completedBucket = null;
        lock (_bucketGate)
        {
            var minuteStart = TimeBuckets.ToMinuteStart(_clock.UtcNow);
            if (minuteStart != _currentMinuteStart)
            {
                completedBucket = SnapshotCurrentBucketLocked();
                _currentMinuteStart = minuteStart;
                _currentCounts = new uint[KeyCatalog.Count];
            }

            var keyIndex = (int)keyId;
            if (_currentCounts[keyIndex] < uint.MaxValue)
            {
                _currentCounts[keyIndex]++;
            }
        }

        if (completedBucket is { TotalCount: > 0 })
        {
            _saveQueue.Writer.TryWrite(new SaveCommand(completedBucket, null));
        }
    }

    public MinuteBucketSnapshot GetCurrentSnapshot()
    {
        lock (_bucketGate)
        {
            return SnapshotCurrentBucketLocked();
        }
    }

    public Task FlushNowAsync(CancellationToken cancellationToken = default) =>
        QueueCurrentSnapshotAndWaitAsync(allowDisposed: false, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timerCancellation.Cancel();
        try
        {
            await _timerWorker;
        }
        catch (OperationCanceledException)
        {
        }

        await QueueCurrentSnapshotAndWaitAsync(allowDisposed: true, CancellationToken.None);
        _saveQueue.Writer.TryComplete();
        await _saveWorker;
        _timerCancellation.Dispose();
    }

    private async Task PeriodicFlushAsync()
    {
        using var timer = new PeriodicTimer(_flushInterval);
        while (await timer.WaitForNextTickAsync(_timerCancellation.Token))
        {
            MinuteBucketSnapshot snapshot;
            lock (_bucketGate)
            {
                snapshot = SnapshotCurrentBucketLocked();
            }

            if (snapshot.TotalCount > 0)
            {
                _saveQueue.Writer.TryWrite(new SaveCommand(snapshot, null));
            }
        }
    }

    private async Task QueueCurrentSnapshotAndWaitAsync(bool allowDisposed, CancellationToken cancellationToken)
    {
        if (!allowDisposed && Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(StatisticsRecorder));
        }

        MinuteBucketSnapshot snapshot;
        lock (_bucketGate)
        {
            snapshot = SnapshotCurrentBucketLocked();
        }

        if (snapshot.TotalCount == 0)
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_saveQueue.Writer.TryWrite(new SaveCommand(snapshot, completion)))
        {
            throw new InvalidOperationException("保存队列已关闭。");
        }

        await completion.Task.WaitAsync(cancellationToken);
    }

    private async Task ProcessSaveQueueAsync()
    {
        await foreach (var command in _saveQueue.Reader.ReadAllAsync())
        {
            try
            {
                await _repository.UpsertMinuteBucketAsync(command.Snapshot);
                Interlocked.Exchange(ref _lastSavedUnixSeconds, _clock.UtcNow.ToUnixTimeSeconds());
                Volatile.Write(ref _lastError, null);
                command.Completion?.SetResult();
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _lastError, exception.Message);
                command.Completion?.SetException(exception);
            }
        }
    }

    private MinuteBucketSnapshot SnapshotCurrentBucketLocked() =>
        new(_currentMinuteStart, (uint[])_currentCounts.Clone());

    private sealed record SaveCommand(MinuteBucketSnapshot Snapshot, TaskCompletionSource? Completion);
}
