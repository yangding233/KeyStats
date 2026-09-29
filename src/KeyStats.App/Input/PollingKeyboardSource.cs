using System.Runtime.InteropServices;
using KeyStats.Core;

namespace KeyStats.App.Input;

/// <summary>
/// 轮询兜底源：以 <c>GetAsyncKeyState</c> 采样物理键状态，补上 Raw Input 收不到的按键事件。
/// 已确认的两类场景：
/// <list type="bullet">
///   <item>PrtSc：Windows 只用假 Shift 事件代替“按下”，Raw Input 侧永远没有按下事件；</item>
///   <item>被其它程序的全局热键 / 低级键盘钩子吃掉的组合键（例如翻译工具的 Shift+Z）。</item>
/// </list>
/// 采样周期约 16ms，只把“物理状态跳变”交给 <see cref="KeyboardCounter"/>，
/// 去重与计数由计数器统一负责，因此不会与 Raw Input 重复计数。
/// 同一个采样线程还负责用物理状态复核按下状态，修复“丢失松开事件后该键永久不再计数”的问题。
/// </summary>
public sealed class PollingKeyboardSource : IDisposable
{
    private const int MapVirtualKeyToScanCodeExtended = 4;
    private const int KeyDownMask = 0x8000;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16);

    private readonly KeyboardCounter _counter;
    private readonly Action<KeyId>? _countedKeySink;
    private readonly IReadOnlyDictionary<KeyId, int[]> _virtualKeys;
    private readonly HashSet<KeyId> _observableKeys;
    private readonly HashSet<KeyId> _physicallyDown = [];
    private readonly HashSet<KeyId> _downThisTick = [];
    private readonly object _tickGate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;
    private volatile bool _disposed;
    private long _tickCount;

    public PollingKeyboardSource(KeyboardCounter counter, Action<KeyId>? countedKeySink = null)
    {
        ArgumentNullException.ThrowIfNull(counter);

        _counter = counter;
        _countedKeySink = countedKeySink;
        _virtualKeys = PolledKeyMap.Build(
            virtualKey => MapVirtualKey((uint)virtualKey, MapVirtualKeyToScanCodeExtended));
        _observableKeys = [.. _virtualKeys.Keys];
        _worker = Task.Run(() => PollLoopAsync(_cancellation.Token));
    }

    /// <summary>轮询能够观测到的键数量（界面健康度显示用）。</summary>
    public int ObservableKeyCount => _virtualKeys.Count;

    /// <summary>已完成的采样次数（界面健康度显示用，用于确认轮询线程仍然存活）。</summary>
    public long TickCount => Interlocked.Read(ref _tickCount);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // 退出阶段不再处理轮询线程的异常。
        }

        _cancellation.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_disposed)
                {
                    return;
                }

                Tick();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Tick()
    {
        Interlocked.Increment(ref _tickCount);
        lock (_tickGate)
        {
            _downThisTick.Clear();
            foreach (var (keyId, virtualKeys) in _virtualKeys)
            {
                // 同一个物理键在不同 NumLock 状态下会用不同虚拟键上报（例如数字键盘 5 对应
                // VK_CLEAR 与 VK_NUMPAD5），因此任一虚拟键按下都算该键按下。
                foreach (var virtualKey in virtualKeys)
                {
                    if ((GetAsyncKeyState(virtualKey) & KeyDownMask) != 0)
                    {
                        _downThisTick.Add(keyId);
                        break;
                    }
                }
            }

            foreach (var keyId in _downThisTick)
            {
                if (_physicallyDown.Add(keyId))
                {
                    Notify(keyId, isDown: true);
                }
            }

            if (_physicallyDown.Count > 0)
            {
                foreach (var keyId in _physicallyDown.ToArray())
                {
                    if (_downThisTick.Contains(keyId))
                    {
                        continue;
                    }

                    _physicallyDown.Remove(keyId);
                    Notify(keyId, isDown: false);
                }
            }

            // 物理上已经抬起的按键不应继续停留在“按下中”，否则该键将永久不再计数。
            _counter.ReconcilePhysicallyUp(_observableKeys, _downThisTick);
        }
    }

    private void Notify(KeyId keyId, bool isDown)
    {
        var result = _counter.ProcessPolledButton(keyId, isDown);
        if (result.Counted && result.KeyId is { } counted)
        {
            _countedKeySink?.Invoke(counted);
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
