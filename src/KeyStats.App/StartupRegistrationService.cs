using Microsoft.Win32;
using System.IO;

namespace KeyStats.App;

internal sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyStats";
    private readonly string _command;

    public StartupRegistrationService(string executablePath)
    {
        _command = $"\"{Path.GetFullPath(executablePath)}\" --background";
    }

    public bool IsEnabledForCurrentExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return string.Equals(value, _command, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的 Windows 启动项。 ");
        if (enabled)
        {
            key.SetValue(ValueName, _command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
