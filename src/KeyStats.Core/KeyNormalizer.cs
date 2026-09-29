namespace KeyStats.Core;

public static class KeyNormalizer
{
    private const ushort VkPause = 0x13;
    private const ushort VkSnapshot = 0x2C;
    private const ushort VkF13 = 0x7C;
    private const ushort VkF24 = 0x87;

    public static KeyId? Normalize(RawKeyEvent keyEvent)
    {
        if (keyEvent.VirtualKey is 0 or 0xFF)
        {
            return null;
        }

        if (keyEvent.VirtualKey == VkPause)
        {
            return KeyId.Pause;
        }

        if (keyEvent.VirtualKey == VkSnapshot)
        {
            return KeyId.PrintScreen;
        }

        if (keyEvent.VirtualKey is >= VkF13 and <= VkF24)
        {
            return (KeyId)((int)KeyId.F13 + keyEvent.VirtualKey - VkF13);
        }

        var extendedVirtualKey = NormalizeExtendedVirtualKey(keyEvent.VirtualKey);
        if (extendedVirtualKey is { } specialKey)
        {
            return specialKey;
        }

        var extended = (keyEvent.Flags & RawKeyFlags.E0) != 0;
        var e1 = (keyEvent.Flags & RawKeyFlags.E1) != 0;

        var scanMapped = keyEvent.MakeCode switch
        {
            0x01 => KeyId.Escape,
            0x02 => KeyId.Digit1,
            0x03 => KeyId.Digit2,
            0x04 => KeyId.Digit3,
            0x05 => KeyId.Digit4,
            0x06 => KeyId.Digit5,
            0x07 => KeyId.Digit6,
            0x08 => KeyId.Digit7,
            0x09 => KeyId.Digit8,
            0x0A => KeyId.Digit9,
            0x0B => KeyId.Digit0,
            0x0C => KeyId.Minus,
            0x0D => KeyId.Equal,
            0x0E => KeyId.Backspace,
            0x0F => KeyId.Tab,
            0x10 => KeyId.Q,
            0x11 => KeyId.W,
            0x12 => KeyId.E,
            0x13 => KeyId.R,
            0x14 => KeyId.T,
            0x15 => KeyId.Y,
            0x16 => KeyId.U,
            0x17 => KeyId.I,
            0x18 => KeyId.O,
            0x19 => KeyId.P,
            0x1A => KeyId.LeftBracket,
            0x1B => KeyId.RightBracket,
            0x1C => extended ? KeyId.NumpadEnter : KeyId.Enter,
            0x1D => extended ? KeyId.RightControl : KeyId.LeftControl,
            0x1E => KeyId.A,
            0x1F => KeyId.S,
            0x20 => KeyId.D,
            0x21 => KeyId.F,
            0x22 => KeyId.G,
            0x23 => KeyId.H,
            0x24 => KeyId.J,
            0x25 => KeyId.K,
            0x26 => KeyId.L,
            0x27 => KeyId.Semicolon,
            0x28 => KeyId.Apostrophe,
            0x29 => KeyId.Grave,
            0x2A when !extended => KeyId.LeftShift,
            0x2B => KeyId.Backslash,
            0x2C => KeyId.Z,
            0x2D => KeyId.X,
            0x2E => KeyId.C,
            0x2F => KeyId.V,
            0x30 => KeyId.B,
            0x31 => KeyId.N,
            0x32 => KeyId.M,
            0x33 => KeyId.Comma,
            0x34 => KeyId.Period,
            0x35 => extended ? KeyId.NumpadDivide : KeyId.Slash,
            0x36 when !extended => KeyId.RightShift,
            0x37 => extended ? KeyId.PrintScreen : KeyId.NumpadMultiply,
            0x38 => extended ? KeyId.RightAlt : KeyId.LeftAlt,
            0x39 => KeyId.Space,
            0x3A => KeyId.CapsLock,
            0x3B => KeyId.F1,
            0x3C => KeyId.F2,
            0x3D => KeyId.F3,
            0x3E => KeyId.F4,
            0x3F => KeyId.F5,
            0x40 => KeyId.F6,
            0x41 => KeyId.F7,
            0x42 => KeyId.F8,
            0x43 => KeyId.F9,
            0x44 => KeyId.F10,
            0x45 when e1 => KeyId.Pause,
            0x45 => KeyId.NumLock,
            0x46 => KeyId.ScrollLock,
            0x47 => extended ? KeyId.Home : KeyId.Numpad7,
            0x48 => extended ? KeyId.ArrowUp : KeyId.Numpad8,
            0x49 => extended ? KeyId.PageUp : KeyId.Numpad9,
            0x4A => KeyId.NumpadSubtract,
            0x4B => extended ? KeyId.ArrowLeft : KeyId.Numpad4,
            0x4C => KeyId.Numpad5,
            0x4D => extended ? KeyId.ArrowRight : KeyId.Numpad6,
            0x4E => KeyId.NumpadAdd,
            0x4F => extended ? KeyId.End : KeyId.Numpad1,
            0x50 => extended ? KeyId.ArrowDown : KeyId.Numpad2,
            0x51 => extended ? KeyId.PageDown : KeyId.Numpad3,
            0x52 => extended ? KeyId.Insert : KeyId.Numpad0,
            0x53 => extended ? KeyId.Delete : KeyId.NumpadDecimal,
            0x56 => KeyId.Oem102,
            0x57 => KeyId.F11,
            0x58 => KeyId.F12,
            0x5B => KeyId.LeftWindows,
            0x5C => KeyId.RightWindows,
            0x5D => KeyId.Application,
            _ => (KeyId?)null,
        };

        return scanMapped;
    }

    private static KeyId? NormalizeExtendedVirtualKey(ushort virtualKey) => virtualKey switch
    {
        0x5F => KeyId.Sleep,
        0xA6 => KeyId.BrowserBack,
        0xA7 => KeyId.BrowserForward,
        0xA8 => KeyId.BrowserRefresh,
        0xA9 => KeyId.BrowserStop,
        0xAA => KeyId.BrowserSearch,
        0xAB => KeyId.BrowserFavorites,
        0xAC => KeyId.BrowserHome,
        0xAD => KeyId.VolumeMute,
        0xAE => KeyId.VolumeDown,
        0xAF => KeyId.VolumeUp,
        0xB0 => KeyId.MediaNext,
        0xB1 => KeyId.MediaPrevious,
        0xB2 => KeyId.MediaStop,
        0xB3 => KeyId.MediaPlayPause,
        0xB4 => KeyId.LaunchMail,
        0xB5 => KeyId.LaunchMedia,
        0xB6 => KeyId.LaunchApplication1,
        0xB7 => KeyId.LaunchApplication2,
        _ => null,
    };
}
