using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace KeyStats.App;

/// <summary>开机自启动实际使用的机制。</summary>
internal enum StartupRegistrationMode
{
    /// <summary>未启用。</summary>
    None,

    /// <summary>写入了当前用户的 Run 键（首选方式）。</summary>
    RegistryRun,

    /// <summary>Run 键被拒绝写入，回退到「启动」文件夹快捷方式。</summary>
    StartupFolder,
}

/// <summary>界面需要的自启动状态快照。</summary>
internal sealed record StartupRegistrationSnapshot(
    bool IsEnabled,
    bool UsesRegistry,
    bool UsesStartupFolder,
    string? StaleRegistryCommand,
    string? ReadError);

/// <summary>当前用户 Run 键的读写（抽出来便于测试“被拒绝”的分支）。</summary>
internal interface IStartupRegistryStore
{
    string? ReadValue();

    void WriteValue(string command);

    void DeleteValue();
}

internal sealed class CurrentUserRunRegistryStore(string runKeyPath, string valueName) : IStartupRegistryStore
{
    public string? ReadValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void WriteValue(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的 Windows 启动项。");
        key.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void DeleteValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
        if (key?.GetValue(valueName) is not null)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}

/// <summary>
/// 管理“登录 Windows 后自动启动”。
/// 首选写入当前用户的 Run 键；若注册表被安全软件或组策略拒绝，则回退到
/// 「启动」文件夹里创建快捷方式（<c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup</c>），
/// 这样在锁定型机器上也能正常自启。
/// </summary>
internal sealed class StartupRegistrationService
{
    private const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultValueName = "KeyStats";
    private const string ShortcutFileName = "KeyStats.lnk";
    private const string LauncherFileName = "KeyStats.cmd";
    private const string BackgroundArgument = "--background";

    private readonly string _executablePath;
    private readonly string _command;
    private readonly IStartupRegistryStore _registry;
    private readonly string _startupDirectory;
    private readonly string _shortcutPath;
    private readonly string _launcherPath;

    public StartupRegistrationService(
        string executablePath,
        IStartupRegistryStore? registry = null,
        string? startupDirectory = null)
    {
        _executablePath = Path.GetFullPath(executablePath);
        _command = $"\"{_executablePath}\" {BackgroundArgument}";
        _registry = registry ?? new CurrentUserRunRegistryStore(DefaultRunKeyPath, DefaultValueName);
        _startupDirectory = startupDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        _shortcutPath = Path.Combine(_startupDirectory, ShortcutFileName);
        _launcherPath = Path.Combine(_startupDirectory, LauncherFileName);
    }

    /// <summary>「启动」文件夹路径（注册表被拒绝时在界面上提示用户使用）。</summary>
    public string StartupFolderPath => _startupDirectory;

    /// <summary>上一次写入注册表失败的原因（若有）。</summary>
    public string? RegistryFailureReason { get; private set; }

    public StartupRegistrationSnapshot GetSnapshot()
    {
        string? registryCommand = null;
        string? readError = null;
        try
        {
            registryCommand = _registry.ReadValue();
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            readError = exception.Message;
        }

        var registryMatches = string.Equals(registryCommand, _command, StringComparison.OrdinalIgnoreCase);
        var shortcutExists = File.Exists(_shortcutPath);
        var launcherExists = File.Exists(_launcherPath);

        return new StartupRegistrationSnapshot(
            IsEnabled: registryMatches || shortcutExists || launcherExists,
            UsesRegistry: registryMatches,
            UsesStartupFolder: shortcutExists || launcherExists,
            StaleRegistryCommand: registryCommand is { Length: > 0 } && !registryMatches ? registryCommand : null,
            ReadError: readError);
    }

    /// <summary>启用或关闭自启动，返回实际生效的机制。</summary>
    public StartupRegistrationMode SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            TryDeleteRegistryValue();
            RemoveStartupArtifacts();
            return StartupRegistrationMode.None;
        }

        try
        {
            _registry.WriteValue(_command);
            RegistryFailureReason = null;

            // 注册表可用时清掉之前可能留下的「启动」文件夹入口，避免重复自启。
            RemoveStartupArtifacts();
            return StartupRegistrationMode.RegistryRun;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            RegistryFailureReason = exception.Message;
            CreateStartupArtifact();
            return StartupRegistrationMode.StartupFolder;
        }
    }

    /// <summary>把指向旧位置的启动项更新为当前位置（界面上的“更新自启动位置”）。</summary>
    public bool TryRepairRegistryEntry()
    {
        try
        {
            _registry.WriteValue(_command);
            RegistryFailureReason = null;
            RemoveStartupArtifacts();
            return true;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            RegistryFailureReason = exception.Message;
            return false;
        }
    }

    private void TryDeleteRegistryValue()
    {
        try
        {
            _registry.DeleteValue();
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            RegistryFailureReason = exception.Message;
        }
    }

    private void RemoveStartupArtifacts()
    {
        foreach (var path in new[] { _shortcutPath, _launcherPath })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 删除失败不影响其他清理动作。
            }
        }
    }

    private void CreateStartupArtifact()
    {
        Directory.CreateDirectory(_startupDirectory);
        if (TryCreateShortcut())
        {
            return;
        }

        // 快捷方式创建失败时退回到 .cmd 启动器（登录时会短暂出现一个控制台窗口）。
        File.WriteAllText(
            _launcherPath,
            $"@echo off\r\nstart \"\" \"{_executablePath}\" {BackgroundArgument}\r\n");
    }

    private bool TryCreateShortcut()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            try
            {
                var link = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    binder: null,
                    target: shell,
                    args: [_shortcutPath]);
                if (link is null)
                {
                    return false;
                }

                var linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, [_executablePath]);
                linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, [BackgroundArgument]);
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, [Path.GetDirectoryName(_executablePath)]);
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, ["KeyStats 键盘与鼠标使用统计"]);
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                return File.Exists(_shortcutPath);
            }
            finally
            {
                if (Marshal.IsComObject(shell))
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
        }
        catch (Exception exception) when (exception is COMException
            or MissingMethodException
            or InvalidOperationException
            or TargetInvocationException
            or UnauthorizedAccessException
            or IOException
            or NotSupportedException)
        {
            return false;
        }
    }
}
