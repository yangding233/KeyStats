using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeyStats.Core;

namespace KeyStats.App.Controls;

public enum HeatScaleMode
{
    SquareRoot,
    Linear,
}

public sealed class KeyboardHeatmapControl : Viewbox
{
    private const double Unit = 48;
    private const double Gap = 4;
    private const double Step = Unit + Gap;
    private readonly Dictionary<KeyId, KeyCapVisual> _keyCaps = [];

    public KeyboardHeatmapControl()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var canvas = new Canvas
        {
            Width = 24 * Step,
            Height = (6 * Step) + 4,
            Background = Brushes.Transparent,
        };
        Child = canvas;
        BuildKeyboard(canvas);
    }

    public void SetData(IReadOnlyList<ulong> counts, IReadOnlySet<KeyId> pressedKeys, HeatScaleMode scaleMode)
    {
        if (counts.Count != KeyCatalog.Count)
        {
            throw new ArgumentException("热力图计数向量长度不正确。", nameof(counts));
        }

        var displayedCounts = _keyCaps.Keys.Select(key => counts[(int)key]).ToArray();
        var maximum = displayedCounts.DefaultIfEmpty().Max();
        var total = counts.Aggregate(0UL, (sum, count) => checked(sum + count));
        var ranks = counts
            .Select((count, index) => (Count: count, Key: (KeyId)index))
            .Where(item => item.Count > 0)
            .OrderByDescending(item => item.Count)
            .ThenBy(item => (int)item.Key)
            .Select((item, index) => (item.Key, Rank: index + 1))
            .ToDictionary(item => item.Key, item => item.Rank);

        foreach (var (keyId, visual) in _keyCaps)
        {
            var count = counts[(int)keyId];
            var intensity = maximum == 0 ? 0d : count / (double)maximum;
            if (scaleMode == HeatScaleMode.SquareRoot)
            {
                intensity = Math.Sqrt(intensity);
            }

            visual.Border.Background = new SolidColorBrush(GetHeatColor(count, intensity));
            visual.Border.BorderBrush = pressedKeys.Contains(keyId)
                ? new SolidColorBrush(Color.FromRgb(33, 103, 184))
                : new SolidColorBrush(Color.FromRgb(206, 214, 223));
            visual.Border.BorderThickness = pressedKeys.Contains(keyId) ? new Thickness(2.5) : new Thickness(1);
            visual.CountText.Text = count.ToString("N0");
            visual.CountText.Foreground = intensity >= 0.72
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(35, 43, 52));
            visual.NameText.Foreground = intensity >= 0.72
                ? new SolidColorBrush(Color.FromRgb(255, 248, 238))
                : new SolidColorBrush(Color.FromRgb(80, 90, 102));

            var percentage = total == 0 ? 0d : count * 100d / total;
            var rankText = ranks.TryGetValue(keyId, out var rank) ? rank.ToString() : "—";
            visual.Border.ToolTip = $"{KeyCatalog.Get(keyId).DisplayName}\n次数：{count:N0}\n占全部输入：{percentage:F2}%\n排名：{rankText}";
        }
    }

    private void BuildKeyboard(Canvas canvas)
    {
        Add(canvas, KeyId.Escape, "Esc", 0, 0);
        AddSequence(canvas, 2, 0,
            K(KeyId.F1, "F1"), K(KeyId.F2, "F2"), K(KeyId.F3, "F3"), K(KeyId.F4, "F4"));
        AddSequence(canvas, 6.5, 0,
            K(KeyId.F5, "F5"), K(KeyId.F6, "F6"), K(KeyId.F7, "F7"), K(KeyId.F8, "F8"));
        AddSequence(canvas, 11, 0,
            K(KeyId.F9, "F9"), K(KeyId.F10, "F10"), K(KeyId.F11, "F11"), K(KeyId.F12, "F12"));
        AddSequence(canvas, 15.5, 0,
            K(KeyId.PrintScreen, "PrtSc"), K(KeyId.ScrollLock, "ScrLk"), K(KeyId.Pause, "Pause"));
        Add(canvas, KeyId.MouseLeftButton, "鼠标左键", 20, 0, 1.7);
        Add(canvas, KeyId.MouseRightButton, "鼠标右键", 21.7, 0, 1.7);

        AddSequence(canvas, 0, 1,
            K(KeyId.Grave, "`"), K(KeyId.Digit1, "1"), K(KeyId.Digit2, "2"), K(KeyId.Digit3, "3"),
            K(KeyId.Digit4, "4"), K(KeyId.Digit5, "5"), K(KeyId.Digit6, "6"), K(KeyId.Digit7, "7"),
            K(KeyId.Digit8, "8"), K(KeyId.Digit9, "9"), K(KeyId.Digit0, "0"), K(KeyId.Minus, "-"),
            K(KeyId.Equal, "="), K(KeyId.Backspace, "Backspace", 2));
        AddSequence(canvas, 16, 1,
            K(KeyId.Insert, "Ins"), K(KeyId.Home, "Home"), K(KeyId.PageUp, "PgUp"));
        AddSequence(canvas, 20, 1,
            K(KeyId.NumLock, "Num"), K(KeyId.NumpadDivide, "/"), K(KeyId.NumpadMultiply, "*"), K(KeyId.NumpadSubtract, "-"));

        AddSequence(canvas, 0, 2,
            K(KeyId.Tab, "Tab", 1.5), K(KeyId.Q, "Q"), K(KeyId.W, "W"), K(KeyId.E, "E"), K(KeyId.R, "R"),
            K(KeyId.T, "T"), K(KeyId.Y, "Y"), K(KeyId.U, "U"), K(KeyId.I, "I"), K(KeyId.O, "O"), K(KeyId.P, "P"),
            K(KeyId.LeftBracket, "["), K(KeyId.RightBracket, "]"), K(KeyId.Backslash, "\\", 1.5));
        AddSequence(canvas, 16, 2,
            K(KeyId.Delete, "Del"), K(KeyId.End, "End"), K(KeyId.PageDown, "PgDn"));
        AddSequence(canvas, 20, 2,
            K(KeyId.Numpad7, "7"), K(KeyId.Numpad8, "8"), K(KeyId.Numpad9, "9"));
        Add(canvas, KeyId.NumpadAdd, "+", 23, 2, 1, 2);

        AddSequence(canvas, 0, 3,
            K(KeyId.CapsLock, "Caps", 1.75), K(KeyId.A, "A"), K(KeyId.S, "S"), K(KeyId.D, "D"), K(KeyId.F, "F"),
            K(KeyId.G, "G"), K(KeyId.H, "H"), K(KeyId.J, "J"), K(KeyId.K, "K"), K(KeyId.L, "L"),
            K(KeyId.Semicolon, ";"), K(KeyId.Apostrophe, "'"), K(KeyId.Enter, "Enter", 2.25));
        AddSequence(canvas, 20, 3,
            K(KeyId.Numpad4, "4"), K(KeyId.Numpad5, "5"), K(KeyId.Numpad6, "6"));

        AddSequence(canvas, 0, 4,
            K(KeyId.LeftShift, "Shift", 2.25), K(KeyId.Z, "Z"), K(KeyId.X, "X"), K(KeyId.C, "C"), K(KeyId.V, "V"),
            K(KeyId.B, "B"), K(KeyId.N, "N"), K(KeyId.M, "M"), K(KeyId.Comma, ","), K(KeyId.Period, "."),
            K(KeyId.Slash, "/"), K(KeyId.RightShift, "Shift", 2.75));
        Add(canvas, KeyId.ArrowUp, "↑", 17, 4);
        AddSequence(canvas, 20, 4,
            K(KeyId.Numpad1, "1"), K(KeyId.Numpad2, "2"), K(KeyId.Numpad3, "3"));
        Add(canvas, KeyId.NumpadEnter, "Enter", 23, 4, 1, 2);

        AddSequence(canvas, 0, 5,
            K(KeyId.LeftControl, "Ctrl", 1.25), K(KeyId.LeftWindows, "Win", 1.25), K(KeyId.LeftAlt, "Alt", 1.25),
            K(KeyId.Space, "Space", 6.25), K(KeyId.RightAlt, "Alt", 1.25), K(KeyId.RightWindows, "Win", 1.25),
            K(KeyId.Application, "Menu", 1.25), K(KeyId.RightControl, "Ctrl", 1.25));
        AddSequence(canvas, 16, 5,
            K(KeyId.ArrowLeft, "←"), K(KeyId.ArrowDown, "↓"), K(KeyId.ArrowRight, "→"));
        Add(canvas, KeyId.Numpad0, "0", 20, 5, 2);
        Add(canvas, KeyId.NumpadDecimal, ".", 22, 5);
    }

    private void AddSequence(Canvas canvas, double startX, double y, params KeySpec[] keys)
    {
        var x = startX;
        foreach (var key in keys)
        {
            Add(canvas, key.Id, key.Label, x, y, key.Width);
            x += key.Width;
        }
    }

    private void Add(Canvas canvas, KeyId keyId, string label, double x, double y, double width = 1, double height = 1)
    {
        var nameText = new TextBlock
        {
            Text = label,
            FontSize = width < 1.4 ? 10 : 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var countText = new TextBlock
        {
            Text = "0",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        content.Children.Add(nameText);
        content.Children.Add(countText);

        var border = new Border
        {
            Width = (width * Step) - Gap,
            Height = (height * Step) - Gap,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(206, 214, 223)),
            Background = new SolidColorBrush(Color.FromRgb(247, 248, 250)),
            Child = content,
            SnapsToDevicePixels = true,
        };
        Canvas.SetLeft(border, x * Step);
        Canvas.SetTop(border, y * Step);
        canvas.Children.Add(border);
        _keyCaps[keyId] = new KeyCapVisual(border, nameText, countText);
    }

    private static KeySpec K(KeyId id, string label, double width = 1) => new(id, label, width);

    private static Color GetHeatColor(ulong count, double intensity)
    {
        if (count == 0)
        {
            return Color.FromRgb(247, 248, 250);
        }

        var low = Color.FromRgb(218, 235, 249);
        var middle = Color.FromRgb(91, 151, 211);
        var high = Color.FromRgb(235, 139, 57);
        return intensity <= 0.58
            ? Interpolate(low, middle, intensity / 0.58)
            : Interpolate(middle, high, (intensity - 0.58) / 0.42);
    }

    private static Color Interpolate(Color start, Color end, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(start.R + ((end.R - start.R) * amount)),
            (byte)Math.Round(start.G + ((end.G - start.G) * amount)),
            (byte)Math.Round(start.B + ((end.B - start.B) * amount)));
    }

    private sealed record KeyCapVisual(Border Border, TextBlock NameText, TextBlock CountText);

    private readonly record struct KeySpec(KeyId Id, string Label, double Width);
}
