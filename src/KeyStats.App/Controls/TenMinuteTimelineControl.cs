using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace KeyStats.App.Controls;

public sealed class TenMinuteTimelineControl : Grid
{
    public const int SegmentCount = 144;
    private readonly Border[] _segmentVisuals = new Border[SegmentCount];

    public TenMinuteTimelineControl()
    {
        Height = 38;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(19) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });

        var segments = new UniformGrid { Columns = SegmentCount };
        for (var index = 0; index < SegmentCount; index++)
        {
            var startMinutes = index * 10;
            var endMinutes = (index + 1) * 10;
            var segment = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(239, 242, 246)),
                BorderBrush = index % 6 == 0
                    ? new SolidColorBrush(Color.FromRgb(167, 181, 196))
                    : new SolidColorBrush(Color.FromRgb(250, 251, 252)),
                BorderThickness = new Thickness(index % 6 == 0 ? 1 : 0.5, 0, 0, 0),
                ToolTip = $"{FormatTime(startMinutes)}—{FormatTime(endMinutes)}\n输入次数：0",
            };
            ToolTipService.SetInitialShowDelay(segment, 500);
            ToolTipService.SetBetweenShowDelay(segment, 100);
            ToolTipService.SetShowDuration(segment, 30_000);
            _segmentVisuals[index] = segment;
            segments.Children.Add(segment);
        }

        Children.Add(segments);

        var labels = new UniformGrid { Columns = 12 };
        SetRow(labels, 1);
        for (var hour = 0; hour < 24; hour += 2)
        {
            labels.Children.Add(new TextBlock
            {
                Text = $"{hour:00}:00",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(105, 113, 122)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(-1, 2, 0, 0),
            });
        }

        Children.Add(labels);
    }

    public void SetData(IReadOnlyList<ulong> counts)
    {
        if (counts.Count != SegmentCount)
        {
            throw new ArgumentException($"时段热力图需要 {SegmentCount} 个十分钟分段。", nameof(counts));
        }

        var maximum = counts.DefaultIfEmpty().Max();
        for (var index = 0; index < SegmentCount; index++)
        {
            var count = counts[index];
            var intensity = maximum == 0 ? 0d : Math.Sqrt(count / (double)maximum);
            _segmentVisuals[index].Background = new SolidColorBrush(GetHeatColor(count, intensity));
            _segmentVisuals[index].ToolTip =
                $"{FormatTime(index * 10)}—{FormatTime((index + 1) * 10)}\n输入次数：{count:N0}";
        }
    }

    private static string FormatTime(int totalMinutes)
    {
        if (totalMinutes == 24 * 60)
        {
            return "24:00";
        }

        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }

    private static Color GetHeatColor(ulong count, double intensity)
    {
        if (count == 0)
        {
            return Color.FromRgb(239, 242, 246);
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
}
