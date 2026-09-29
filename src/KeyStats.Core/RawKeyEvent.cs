namespace KeyStats.Core;

[Flags]
public enum RawKeyFlags : ushort
{
    None = 0,
    Break = 0x0001,
    E0 = 0x0002,
    E1 = 0x0004,
}

public readonly record struct RawKeyEvent(ushort MakeCode, RawKeyFlags Flags, ushort VirtualKey)
{
    public bool IsBreak => (Flags & RawKeyFlags.Break) != 0;
}

