using System.Windows;
using System.Windows.Interop;

namespace KeyStats.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = @"Local\KeyStats.SingleInstance.v1";
    private const string ShowSignalName = @"Local\KeyStats.Show.v1";
    private const string ExitSignalName = @"Local\KeyStats.Exit.v1";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSignal;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _showWait;
    private RegisteredWaitHandle? _exitWait;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        var startInBackground = eventArgs.Args.Any(
            argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        var requestExit = eventArgs.Args.Any(
            argument => string.Equals(argument, "--exit", StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = new Mutex(false, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance(requestExit ? ExitSignalName : ShowSignalName, startInBackground && !requestExit);
            Shutdown();
            return;
        }

        if (requestExit)
        {
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName);
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;

        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showSignal,
            (_, _) => Dispatcher.BeginInvoke(_mainWindow.ShowFromTray),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            _exitSignal,
            (_, _) => Dispatcher.BeginInvoke(_mainWindow.ExitFromExternalRequest),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        if (startInBackground)
        {
            _ = new WindowInteropHelper(_mainWindow).EnsureHandle();
        }
        else
        {
            _mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _showWait?.Unregister(null);
        _exitWait?.Unregister(null);
        _showSignal?.Dispose();
        _exitSignal?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(eventArgs);
    }

    private static void SignalExistingInstance(string signalName, bool suppressSignal)
    {
        if (suppressSignal)
        {
            return;
        }

        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, signalName);
        signal.Set();
    }
}
