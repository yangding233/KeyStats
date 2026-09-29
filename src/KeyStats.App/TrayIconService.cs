using System.Drawing;

namespace KeyStats.App;

internal sealed class TrayIconService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _pauseItem;
    private readonly Icon? _applicationIcon;
    private bool _hiddenNotificationShown;

    public TrayIconService(Action open, Action togglePause, Action saveNow, Action exit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem("打开统计窗口");
        _pauseItem = new System.Windows.Forms.ToolStripMenuItem("暂停采集");
        var saveItem = new System.Windows.Forms.ToolStripMenuItem("立即保存");
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("完整退出");

        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        openItem.Click += (_, _) => open();
        _pauseItem.Click += (_, _) => togglePause();
        saveItem.Click += (_, _) => saveNow();
        exitItem.Click += (_, _) => exit();

        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(saveItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _applicationIcon = Environment.ProcessPath is { } executablePath
            ? Icon.ExtractAssociatedIcon(executablePath)
            : null;
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _applicationIcon ?? SystemIcons.Application,
            Text = "KeyStats · 正在采集",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => open();
    }

    public void SetPaused(bool isPaused)
    {
        _pauseItem.Text = isPaused ? "恢复采集" : "暂停采集";
        _notifyIcon.Text = isPaused ? "KeyStats · 已暂停" : "KeyStats · 正在采集";
    }

    public void ShowHiddenNotification()
    {
        if (_hiddenNotificationShown)
        {
            return;
        }

        _hiddenNotificationShown = true;
        _notifyIcon.BalloonTipTitle = "KeyStats 仍在后台采集";
        _notifyIcon.BalloonTipText = "双击托盘图标可重新打开；选择“完整退出”才会停止采集。";
        _notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3500);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _applicationIcon?.Dispose();
    }
}
