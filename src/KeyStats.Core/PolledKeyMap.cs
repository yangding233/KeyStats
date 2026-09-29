namespace KeyStats.Core;

/// <summary>
/// 构建轮询兜底源所需的 KeyId → 虚拟键（VK）映射。
/// 复用 <see cref="KeyNormalizer"/>，保证轮询源与 Raw Input 源对同一个物理键给出一致的 KeyId，
/// 因而新增键位只需维护归一化表，不会出现两套映射互相漂移。
/// </summary>
public static class PolledKeyMap
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int FirstVirtualKey = 0x01;
    private const int LastVirtualKey = 0xFE;

    // 侧别明确的修饰键必须优先映射：否则 VK_SHIFT(0x10) 会先占用 LeftShift，
    // 而 GetAsyncKeyState(0x10) 对左右 Shift 都会返回按下，造成左 Shift 误计。
    private static readonly int[] SideSpecificVirtualKeys =
    [
        0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C,
    ];

    // 鼠标按键不由 MapVirtualKey 翻译（没有扫描码），单独补齐。
    private static readonly (KeyId KeyId, int VirtualKey)[] MouseButtons =
    [
        (KeyId.MouseLeftButton, 0x01),
        (KeyId.MouseRightButton, 0x02),
    ];

    // 扩展键集合：这些键是否带 E0 前缀由虚拟键本身决定，是 Windows 的固定约定。
    // 不能依赖 MapVirtualKey(MAPVK_VK_TO_VSC_EX) 的高字节 —— 实测它在导航键上并不返回 E0，
    // 会把 Insert/Home/方向键误判成对应的小键盘键。
    private static readonly int[] ExtendedVirtualKeys =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // PageUp PageDown End Home ← ↑ → ↓
        0x2C,                                           // PrintScreen
        0x2D, 0x2E,                                     // Insert Delete
        0x5B, 0x5C, 0x5D,                               // 左 Win / 右 Win / 菜单键
        0x6C,                                           // Separator（数字键盘 Enter）
        0x6F,                                           // 数字键盘 /
        0x90,                                           // NumLock
        0xA3, 0xA5,                                     // 右 Ctrl / 右 Alt
    ];

    public static IReadOnlyDictionary<KeyId, int[]> Build(Func<int, uint> virtualKeyToScanCodeExtended)
    {
        ArgumentNullException.ThrowIfNull(virtualKeyToScanCodeExtended);

        var map = new Dictionary<KeyId, List<int>>(KeyCatalog.Count);

        void AddCandidate(int virtualKey)
        {
            var scanCodeWithFlags = virtualKeyToScanCodeExtended(virtualKey);
            var scanCode = (ushort)(scanCodeWithFlags & 0xFF);

            // MAPVK_VK_TO_VSC_EX 会把扩展标志放在高字节（0xE0 / 0xE1），但它并不总是填写，
            // 因此再与显式的扩展键集合求并集。
            var flags = (scanCodeWithFlags & 0xE000) != 0 || Array.IndexOf(ExtendedVirtualKeys, virtualKey) >= 0
                ? RawKeyFlags.E0
                : RawKeyFlags.None;

            var keyId = KeyNormalizer.Normalize(new RawKeyEvent(scanCode, flags, (ushort)virtualKey));
            if (keyId is not { } id)
            {
                return;
            }

            if (!map.TryGetValue(id, out var virtualKeys))
            {
                virtualKeys = [];
                map[id] = virtualKeys;
            }

            if (!virtualKeys.Contains(virtualKey))
            {
                virtualKeys.Add(virtualKey);
            }
        }

        // 侧别明确的修饰键在前，保证左右修饰键各自拿到自己的虚拟键。
        foreach (var virtualKey in SideSpecificVirtualKeys)
        {
            AddCandidate(virtualKey);
        }

        for (var virtualKey = FirstVirtualKey; virtualKey <= LastVirtualKey; virtualKey++)
        {
            // 通用修饰键会把左右合并，交给 SideSpecificVirtualKeys 处理。
            if (virtualKey is VkShift or VkControl or VkMenu)
            {
                continue;
            }

            if (Array.IndexOf(SideSpecificVirtualKeys, virtualKey) >= 0)
            {
                continue;
            }

            AddCandidate(virtualKey);
        }

        // 鼠标按键不由 MapVirtualKey 翻译（没有扫描码），单独补齐。
        foreach (var (keyId, virtualKey) in MouseButtons)
        {
            if (!map.TryGetValue(keyId, out var virtualKeys))
            {
                virtualKeys = [];
                map[keyId] = virtualKeys;
            }

            if (!virtualKeys.Contains(virtualKey))
            {
                virtualKeys.Add(virtualKey);
            }
        }

        return map.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}
