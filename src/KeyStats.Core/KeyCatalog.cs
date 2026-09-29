using System.Collections.ObjectModel;

namespace KeyStats.Core;

public static class KeyCatalog
{
    private static readonly ReadOnlyCollection<KeyDefinition> DefinitionsInternal =
        Enum.GetValues<KeyId>()
            .Select((id, index) => new KeyDefinition(id, GetDisplayName(id), GetCategory(id), index))
            .ToList()
            .AsReadOnly();

    public static IReadOnlyList<KeyDefinition> Definitions => DefinitionsInternal;

    public static int Count => DefinitionsInternal.Count;

    public static KeyDefinition Get(KeyId id) => DefinitionsInternal[(int)id];

    private static string GetCategory(KeyId id)
    {
        if (id is >= KeyId.F1 and <= KeyId.Pause || id is >= KeyId.F13 and <= KeyId.F24)
        {
            return "功能键";
        }

        if (id is >= KeyId.Insert and <= KeyId.ArrowRight)
        {
            return "导航键";
        }

        if (id is >= KeyId.NumLock and <= KeyId.NumpadDecimal)
        {
            return "数字键盘";
        }

        if (id is >= KeyId.BrowserBack and <= KeyId.LaunchApplication2 || id == KeyId.Sleep)
        {
            return "扩展键";
        }

        if (id is KeyId.MouseLeftButton or KeyId.MouseRightButton)
        {
            return "鼠标";
        }

        return "主键区";
    }

    private static string GetDisplayName(KeyId id) => id switch
    {
        KeyId.Escape => "Esc",
        KeyId.PrintScreen => "Print Screen",
        KeyId.ScrollLock => "Scroll Lock",
        KeyId.Grave => "` / ~",
        KeyId.Digit1 => "1",
        KeyId.Digit2 => "2",
        KeyId.Digit3 => "3",
        KeyId.Digit4 => "4",
        KeyId.Digit5 => "5",
        KeyId.Digit6 => "6",
        KeyId.Digit7 => "7",
        KeyId.Digit8 => "8",
        KeyId.Digit9 => "9",
        KeyId.Digit0 => "0",
        KeyId.Minus => "- / _",
        KeyId.Equal => "= / +",
        KeyId.LeftBracket => "[ / {",
        KeyId.RightBracket => "] / }",
        KeyId.Backslash => "\\ / |",
        KeyId.CapsLock => "Caps Lock",
        KeyId.Semicolon => "; / :",
        KeyId.Apostrophe => "' / \"",
        KeyId.LeftShift => "左 Shift",
        KeyId.Comma => ", / <",
        KeyId.Period => ". / >",
        KeyId.Slash => "/ / ?",
        KeyId.RightShift => "右 Shift",
        KeyId.LeftControl => "左 Ctrl",
        KeyId.LeftWindows => "左 Win",
        KeyId.LeftAlt => "左 Alt",
        KeyId.RightAlt => "右 Alt",
        KeyId.RightWindows => "右 Win",
        KeyId.Application => "菜单",
        KeyId.RightControl => "右 Ctrl",
        KeyId.PageUp => "Page Up",
        KeyId.PageDown => "Page Down",
        KeyId.ArrowUp => "↑",
        KeyId.ArrowLeft => "←",
        KeyId.ArrowDown => "↓",
        KeyId.ArrowRight => "→",
        KeyId.NumLock => "Num Lock",
        KeyId.NumpadDivide => "数字键盘 /",
        KeyId.NumpadMultiply => "数字键盘 *",
        KeyId.NumpadSubtract => "数字键盘 -",
        KeyId.NumpadAdd => "数字键盘 +",
        KeyId.NumpadEnter => "数字键盘 Enter",
        KeyId.NumpadDecimal => "数字键盘 .",
        KeyId.Numpad0 => "数字键盘 0",
        KeyId.Numpad1 => "数字键盘 1",
        KeyId.Numpad2 => "数字键盘 2",
        KeyId.Numpad3 => "数字键盘 3",
        KeyId.Numpad4 => "数字键盘 4",
        KeyId.Numpad5 => "数字键盘 5",
        KeyId.Numpad6 => "数字键盘 6",
        KeyId.Numpad7 => "数字键盘 7",
        KeyId.Numpad8 => "数字键盘 8",
        KeyId.Numpad9 => "数字键盘 9",
        KeyId.BrowserBack => "浏览器后退",
        KeyId.BrowserForward => "浏览器前进",
        KeyId.BrowserRefresh => "浏览器刷新",
        KeyId.BrowserStop => "浏览器停止",
        KeyId.BrowserSearch => "浏览器搜索",
        KeyId.BrowserFavorites => "浏览器收藏",
        KeyId.BrowserHome => "浏览器主页",
        KeyId.VolumeMute => "静音",
        KeyId.VolumeDown => "音量减",
        KeyId.VolumeUp => "音量加",
        KeyId.MediaNext => "下一曲",
        KeyId.MediaPrevious => "上一曲",
        KeyId.MediaStop => "停止播放",
        KeyId.MediaPlayPause => "播放/暂停",
        KeyId.LaunchMail => "邮件",
        KeyId.LaunchMedia => "媒体",
        KeyId.LaunchApplication1 => "应用 1",
        KeyId.LaunchApplication2 => "应用 2",
        KeyId.Oem102 => "OEM 102",
        KeyId.MouseLeftButton => "鼠标左键",
        KeyId.MouseRightButton => "鼠标右键",
        _ => id.ToString(),
    };
}
