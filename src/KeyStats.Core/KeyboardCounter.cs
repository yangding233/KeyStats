namespace KeyStats.Core;

/// <summary>
/// 按下/计数状态机。支持两个互补的事件来源：
/// <list type="bullet">
///   <item>Raw Input（按设备 + 扫描码，主来源）；</item>
///   <item>GetAsyncKeyState 轮询（兜底来源，负责 Raw Input 没有投递的按键，例如 PrtSc 只有“松开”没有“按下”，或被其它程序全局热键吃掉的组合键）。</item>
/// </list>
/// 两个来源共享同一套去重状态，保证一次物理按下只计一次；同一物理键的“别名”
/// （例如 NumLock 关闭时的小键盘键与导航键）也共享去重状态。
/// </summary>
public sealed class KeyboardCounter
{
    // NumLock 关闭时，小键盘键在虚拟键层面与对应导航键等价：
    // Raw Input 按扫描码 + E0 区分两者，而轮询按虚拟键采样，会把同一次物理按下报成另一个 KeyId。
    // 这些“同一物理键”的别名必须共享去重状态，否则会重复计数。
    private static readonly KeyId[][] PhysicalAliasGroups =
    [
        [KeyId.Numpad0, KeyId.Insert],
        [KeyId.Numpad1, KeyId.End],
        [KeyId.Numpad2, KeyId.ArrowDown],
        [KeyId.Numpad3, KeyId.PageDown],
        [KeyId.Numpad4, KeyId.ArrowLeft],
        [KeyId.Numpad6, KeyId.ArrowRight],
        [KeyId.Numpad7, KeyId.Home],
        [KeyId.Numpad8, KeyId.ArrowUp],
        [KeyId.Numpad9, KeyId.PageUp],
        [KeyId.NumpadDecimal, KeyId.Delete],
    ];

    private static readonly Dictionary<KeyId, KeyId[]> AliasLookup = BuildAliasLookup();

    private readonly long[] _counts = new long[KeyCatalog.Count];
    private readonly HashSet<PressedKey> _rawPressed = [];
    private readonly Dictionary<KeyId, int> _rawHoldCounts = [];
    private readonly HashSet<KeyId> _polledHeld = [];
    private readonly object _pressedGate = new();
    private long _totalCount;
    private long _pollOnlyCountedPresses;
    private long _reconciledReleaseCount;
    private int _isPaused;

    public bool IsPaused => Volatile.Read(ref _isPaused) != 0;

    /// <summary>仅由轮询兜底源发现并计数的按下次数（Raw Input 未投递这些按键）。</summary>
    public long PollOnlyCountedPresses => Interlocked.Read(ref _pollOnlyCountedPresses);

    /// <summary>由物理状态复核纠正的“丢失松开”次数。</summary>
    public long ReconciledReleaseCount => Interlocked.Read(ref _reconciledReleaseCount);

    public bool Process(nint deviceHandle, RawKeyEvent keyEvent)
        => ProcessDetailed(deviceHandle, keyEvent).Recognized;

    public KeyProcessResult ProcessDetailed(nint deviceHandle, RawKeyEvent keyEvent)
    {
        var normalized = KeyNormalizer.Normalize(keyEvent);
        if (normalized is not { } keyId)
        {
            return KeyProcessResult.Unrecognized;
        }

        return ProcessButton(deviceHandle, keyId, keyEvent.IsBreak);
    }

    /// <summary>Raw Input 来源的按下/松开。</summary>
    public KeyProcessResult ProcessButton(nint deviceHandle, KeyId keyId, bool isBreak)
    {
        if ((uint)keyId >= (uint)KeyCatalog.Count)
        {
            return KeyProcessResult.Unrecognized;
        }

        // Tab 在部分 Windows 10 环境的 Alt+Tab 系统切换中不会产生 Raw Input，
        // 因而需要与补偿采样共用一个全局身份，避免正常消息和补偿消息重复计数。
        var trackingDeviceHandle = keyId == KeyId.Tab ? 0 : deviceHandle;
        var pressedKey = new PressedKey(trackingDeviceHandle, keyId);

        lock (_pressedGate)
        {
            if (isBreak)
            {
                if (_rawPressed.Remove(pressedKey))
                {
                    DecrementRawHoldLocked(keyId);
                }

                return KeyProcessResult.RecognizedOnly(keyId);
            }

            if (!_rawPressed.Add(pressedKey))
            {
                return KeyProcessResult.RecognizedOnly(keyId);
            }

            var heldBeforeByRaw = RawHoldersInGroupLocked(keyId);
            IncrementRawHoldLocked(keyId);

            // 若这次按下完全由轮询源解释（本组此前没有任何 Raw 按下，但轮询已按住），则不再重复计数。
            var alreadyCountedByPolling = heldBeforeByRaw == 0 && AnyPolledHeldInGroupLocked(keyId);
            if (alreadyCountedByPolling || IsPaused)
            {
                return KeyProcessResult.RecognizedOnly(keyId);
            }

            CountPressLocked(keyId);
            return KeyProcessResult.CountedPress(keyId);
        }
    }

    /// <summary>轮询兜底来源的按下/松开。</summary>
    public KeyProcessResult ProcessPolledButton(KeyId keyId, bool isDown)
    {
        if ((uint)keyId >= (uint)KeyCatalog.Count)
        {
            return KeyProcessResult.Unrecognized;
        }

        lock (_pressedGate)
        {
            if (!isDown)
            {
                _polledHeld.Remove(keyId);
                return KeyProcessResult.RecognizedOnly(keyId);
            }

            // 必须先判断“是否已被其它来源/别名按住”，再把自己写进轮询状态，
            // 否则会把自身算成已按住而永不计数。
            var alreadyHeld = IsAnyHeldInGroupLocked(keyId);
            if (!_polledHeld.Add(keyId))
            {
                return KeyProcessResult.RecognizedOnly(keyId);
            }

            // Raw Input 已经在管这个物理键时，轮询只做状态记录，不重复计数。
            if (alreadyHeld || IsPaused)
            {
                return KeyProcessResult.RecognizedOnly(keyId);
            }

            CountPressLocked(keyId);
            Interlocked.Increment(ref _pollOnlyCountedPresses);
            return KeyProcessResult.CountedPress(keyId);
        }
    }

    /// <summary>
    /// 用物理键状态复核按下状态：只对 <paramref name="observableKeys"/> 里能观测到的键生效，
    /// 物理上已经抬起的按键会被释放。用于修复“丢失松开事件后该键永久不再计数”的问题。
    /// </summary>
    /// <returns>被纠正（释放）的键数量。</returns>
    public int ReconcilePhysicallyUp(
        IReadOnlySet<KeyId> observableKeys,
        IReadOnlySet<KeyId> physicallyDownKeys)
    {
        ArgumentNullException.ThrowIfNull(observableKeys);
        ArgumentNullException.ThrowIfNull(physicallyDownKeys);

        var released = 0;
        lock (_pressedGate)
        {
            if (_rawHoldCounts.Count > 0)
            {
                foreach (var keyId in _rawHoldCounts.Keys.ToArray())
                {
                    // 观测与“是否抬起”都要按物理别名判断：NumLock 状态不同，同一个物理键
                    // 在 Raw Input 与轮询两侧可能对应不同的 KeyId（例如 Numpad0 / Insert）。
                    if (!IsObservableInGroup(keyId, observableKeys) ||
                        IsPhysicallyDownInGroup(keyId, physicallyDownKeys))
                    {
                        continue;
                    }

                    _rawPressed.RemoveWhere(pair => pair.KeyId == keyId);
                    _rawHoldCounts.Remove(keyId);
                    released++;
                }
            }

            if (_polledHeld.Count > 0)
            {
                foreach (var keyId in _polledHeld.ToArray())
                {
                    if (IsObservableInGroup(keyId, observableKeys) &&
                        !IsPhysicallyDownInGroup(keyId, physicallyDownKeys))
                    {
                        _polledHeld.Remove(keyId);
                    }
                }
            }
        }

        if (released > 0)
        {
            Interlocked.Add(ref _reconciledReleaseCount, released);
        }

        return released;
    }

    public void SetPaused(bool paused) => Interlocked.Exchange(ref _isPaused, paused ? 1 : 0);

    public void ClearCounts(bool resetPressedState = false)
    {
        foreach (ref var count in _counts.AsSpan())
        {
            Interlocked.Exchange(ref count, 0);
        }

        Interlocked.Exchange(ref _totalCount, 0);

        if (resetPressedState)
        {
            lock (_pressedGate)
            {
                _rawPressed.Clear();
                _rawHoldCounts.Clear();
                _polledHeld.Clear();
            }
        }
    }

    public void RemoveDevice(nint deviceHandle)
    {
        lock (_pressedGate)
        {
            var removed = _rawPressed.RemoveWhere(key => key.DeviceHandle == deviceHandle);
            if (removed > 0)
            {
                RebuildRawHoldCountsLocked();
            }
        }
    }

    public void ReleaseKey(KeyId keyId)
    {
        lock (_pressedGate)
        {
            var removed = _rawPressed.RemoveWhere(key => key.KeyId == keyId);
            if (removed > 0)
            {
                RebuildRawHoldCountsLocked();
            }

            _polledHeld.Remove(keyId);
        }
    }

    public KeyboardSnapshot GetSnapshot()
    {
        var counts = new long[_counts.Length];
        for (var index = 0; index < _counts.Length; index++)
        {
            counts[index] = Interlocked.Read(ref _counts[index]);
        }

        HashSet<KeyId> pressedKeys;
        lock (_pressedGate)
        {
            pressedKeys = new HashSet<KeyId>(_polledHeld);
            foreach (var key in _rawPressed)
            {
                pressedKeys.Add(key.KeyId);
            }
        }

        return new KeyboardSnapshot(
            Interlocked.Read(ref _totalCount),
            counts,
            pressedKeys,
            IsPaused);
    }

    private void CountPressLocked(KeyId keyId)
    {
        Interlocked.Increment(ref _counts[(int)keyId]);
        Interlocked.Increment(ref _totalCount);
    }

    private void IncrementRawHoldLocked(KeyId keyId)
    {
        _rawHoldCounts.TryGetValue(keyId, out var current);
        _rawHoldCounts[keyId] = current + 1;
    }

    private void DecrementRawHoldLocked(KeyId keyId)
    {
        if (!_rawHoldCounts.TryGetValue(keyId, out var current))
        {
            return;
        }

        if (current <= 1)
        {
            _rawHoldCounts.Remove(keyId);
        }
        else
        {
            _rawHoldCounts[keyId] = current - 1;
        }
    }

    private void RebuildRawHoldCountsLocked()
    {
        _rawHoldCounts.Clear();
        foreach (var key in _rawPressed)
        {
            IncrementRawHoldLocked(key.KeyId);
        }
    }

    private int RawHoldersInGroupLocked(KeyId keyId)
    {
        var total = 0;
        foreach (var member in AliasLookup[keyId])
        {
            if (_rawHoldCounts.TryGetValue(member, out var count))
            {
                total += count;
            }
        }

        return total;
    }

    private bool AnyPolledHeldInGroupLocked(KeyId keyId)
    {
        foreach (var member in AliasLookup[keyId])
        {
            if (_polledHeld.Contains(member))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAnyHeldInGroupLocked(KeyId keyId)
    {
        foreach (var member in AliasLookup[keyId])
        {
            if (_polledHeld.Contains(member) ||
                (_rawHoldCounts.TryGetValue(member, out var count) && count > 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsObservableInGroup(KeyId keyId, IReadOnlySet<KeyId> observableKeys)
    {
        foreach (var member in AliasLookup[keyId])
        {
            if (observableKeys.Contains(member))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPhysicallyDownInGroup(KeyId keyId, IReadOnlySet<KeyId> physicallyDownKeys)
    {
        foreach (var member in AliasLookup[keyId])
        {
            if (physicallyDownKeys.Contains(member))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<KeyId, KeyId[]> BuildAliasLookup()
    {
        var lookup = new Dictionary<KeyId, KeyId[]>(KeyCatalog.Count);
        foreach (var keyId in Enum.GetValues<KeyId>())
        {
            lookup[keyId] = [keyId];
        }

        foreach (var group in PhysicalAliasGroups)
        {
            foreach (var keyId in group)
            {
                lookup[keyId] = group;
            }
        }

        return lookup;
    }

    private readonly record struct PressedKey(nint DeviceHandle, KeyId KeyId);
}
