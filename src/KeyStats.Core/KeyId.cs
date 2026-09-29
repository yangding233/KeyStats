namespace KeyStats.Core;

public enum KeyId
{
    Escape,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    PrintScreen, ScrollLock, Pause,

    Grave, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0,
    Minus, Equal, Backspace,
    Tab, Q, W, E, R, T, Y, U, I, O, P, LeftBracket, RightBracket, Backslash,
    CapsLock, A, S, D, F, G, H, J, K, L, Semicolon, Apostrophe, Enter,
    LeftShift, Z, X, C, V, B, N, M, Comma, Period, Slash, RightShift,
    LeftControl, LeftWindows, LeftAlt, Space, RightAlt, RightWindows, Application, RightControl,

    Insert, Home, PageUp, Delete, End, PageDown,
    ArrowUp, ArrowLeft, ArrowDown, ArrowRight,

    NumLock, NumpadDivide, NumpadMultiply, NumpadSubtract,
    Numpad7, Numpad8, Numpad9, NumpadAdd,
    Numpad4, Numpad5, Numpad6,
    Numpad1, Numpad2, Numpad3, NumpadEnter,
    Numpad0, NumpadDecimal,

    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
    BrowserBack, BrowserForward, BrowserRefresh, BrowserStop, BrowserSearch, BrowserFavorites, BrowserHome,
    VolumeMute, VolumeDown, VolumeUp,
    MediaNext, MediaPrevious, MediaStop, MediaPlayPause,
    LaunchMail, LaunchMedia, LaunchApplication1, LaunchApplication2,
    Sleep, Oem102,

    // 只能在枚举末尾追加，保证已有数据库中的键位下标保持不变。
    MouseLeftButton, MouseRightButton,
}
