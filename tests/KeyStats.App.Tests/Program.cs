// KeyStats.App 层的回归测试：自启动注册（Run 键优先、被拒时回退到「启动」文件夹）。
// 与 tests/KeyStats.Core.Tests 一样，不依赖第三方测试框架，返回码 0 表示全部通过。
using System.IO;
using KeyStats.App;

var tests = new (string Name, Action Run)[]
{
    ("启用时优先写入 Run 键且不留下启动文件夹入口", EnablePrefersRegistry),
    ("Run 键被拒时回退到启动文件夹", FallbackToStartupFolderWhenRegistryDenied),
    ("关闭自启动会同时清理 Run 键与启动文件夹", DisableRemovesBothEntries),
    ("指向旧路径的启动项被识别为失效", StaleRegistryEntryIsReported),
    ("只有启动文件夹入口也算已启用", StartupFolderArtifactCountsAsEnabled),
    ("更新自启动位置会覆盖旧路径", RepairOverwritesStaleEntry),
    ("注册表被拒时更新自启动位置返回失败", RepairReportsFailureWhenDenied),
    ("注册表读取失败不会抛异常并会记录原因", ReadFailureIsReported),
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.Message}");
        Console.WriteLine($"FAIL  {name}\n      {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"结果：{tests.Length - failures.Count}/{tests.Length} 通过");
if (failures.Count > 0)
{
    Console.WriteLine(string.Join(Environment.NewLine, failures));
    Environment.ExitCode = 1;
}

return;

static void EnablePrefersRegistry()
{
    var registry = new FakeRegistryStore();
    using var scope = new TestScope(registry);
    var startupDirectory = scope.StartupDirectory;

    var mode = scope.Service.SetEnabled(true);

    Equal(StartupRegistrationMode.RegistryRun, mode);
    Equal(true, registry.Value is not null);
    Contains("--background", registry.Value!);
    Equal(0, Directory.GetFiles(startupDirectory).Length);

    var snapshot = scope.Service.GetSnapshot();
    Equal(true, snapshot.IsEnabled);
    Equal(true, snapshot.UsesRegistry);
    Equal(false, snapshot.UsesStartupFolder);
    Equal(null, snapshot.StaleRegistryCommand);
}

static void FallbackToStartupFolderWhenRegistryDenied()
{
    var registry = new FakeRegistryStore { DenyWrites = true };
    using var scope = new TestScope(registry);

    var mode = scope.Service.SetEnabled(true);

    Equal(StartupRegistrationMode.StartupFolder, mode);
    Equal(null, registry.Value);
    Equal(true, scope.Service.RegistryFailureReason is { Length: > 0 });

    var artifacts = Directory.GetFiles(scope.StartupDirectory);
    Equal(1, artifacts.Length);
    var fileName = Path.GetFileName(artifacts[0]);
    Equal(true, fileName is "KeyStats.lnk" or "KeyStats.cmd");

    var snapshot = scope.Service.GetSnapshot();
    Equal(true, snapshot.IsEnabled);
    Equal(false, snapshot.UsesRegistry);
    Equal(true, snapshot.UsesStartupFolder);
}

static void DisableRemovesBothEntries()
{
    var registry = new FakeRegistryStore
    {
        DenyWrites = true,
        Value = "\"C:\\old\\KeyStats.exe\" --background",
    };
    using var scope = new TestScope(registry);

    // 注册表被拒 → 先制造一个「启动」文件夹入口。
    scope.Service.SetEnabled(true);
    Equal(1, Directory.GetFiles(scope.StartupDirectory).Length);

    // 允许写入后再关闭自启动：两种入口都应被清理。
    registry.DenyWrites = false;
    var mode = scope.Service.SetEnabled(false);

    Equal(StartupRegistrationMode.None, mode);
    Equal(null, registry.Value);
    Equal(0, Directory.GetFiles(scope.StartupDirectory).Length);
    Equal(false, scope.Service.GetSnapshot().IsEnabled);
}

static void StaleRegistryEntryIsReported()
{
    var registry = new FakeRegistryStore { Value = "\"C:\\old\\KeyStats.exe\" --background" };
    using var scope = new TestScope(registry);

    var snapshot = scope.Service.GetSnapshot();

    Equal(false, snapshot.IsEnabled);
    Equal(false, snapshot.UsesRegistry);
    Equal("\"C:\\old\\KeyStats.exe\" --background", snapshot.StaleRegistryCommand);
}

static void StartupFolderArtifactCountsAsEnabled()
{
    var registry = new FakeRegistryStore();
    using var scope = new TestScope(registry);
    File.WriteAllText(Path.Combine(scope.StartupDirectory, "KeyStats.cmd"), "@echo off\r\n");

    var snapshot = scope.Service.GetSnapshot();

    Equal(true, snapshot.IsEnabled);
    Equal(true, snapshot.UsesStartupFolder);
    Equal(false, snapshot.UsesRegistry);
    Equal(null, snapshot.StaleRegistryCommand);
}

static void RepairOverwritesStaleEntry()
{
    var registry = new FakeRegistryStore { Value = "\"C:\\old\\KeyStats.exe\" --background" };
    using var scope = new TestScope(registry);

    Equal(true, scope.Service.TryRepairRegistryEntry());

    var snapshot = scope.Service.GetSnapshot();
    Equal(true, snapshot.IsEnabled);
    Equal(true, snapshot.UsesRegistry);
    Equal(null, snapshot.StaleRegistryCommand);
    Contains("KeyStats.exe", registry.Value!);
}

static void RepairReportsFailureWhenDenied()
{
    var registry = new FakeRegistryStore { DenyWrites = true };
    using var scope = new TestScope(registry);

    Equal(false, scope.Service.TryRepairRegistryEntry());
    Equal(true, scope.Service.RegistryFailureReason is { Length: > 0 });
}

static void ReadFailureIsReported()
{
    var registry = new FakeRegistryStore { DenyReads = true };
    using var scope = new TestScope(registry);

    var snapshot = scope.Service.GetSnapshot();

    Equal(false, snapshot.IsEnabled);
    Equal(true, snapshot.ReadError is { Length: > 0 });
}

static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"没有找到期望文本：{expected}（实际：{actual}）");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}。");
    }
}

/// <summary>临时目录 + 服务实例，测试结束时自动清理。</summary>
file sealed class TestScope : IDisposable
{
    private readonly string _rootDirectory;

    public TestScope(FakeRegistryStore registry)
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "KeyStats.AppTests", Guid.NewGuid().ToString("N"));
        StartupDirectory = Path.Combine(_rootDirectory, "Startup");
        Directory.CreateDirectory(StartupDirectory);

        var executablePath = Path.Combine(_rootDirectory, "KeyStats.exe");
        File.WriteAllText(executablePath, string.Empty);
        Service = new StartupRegistrationService(executablePath, registry, StartupDirectory);
    }

    public string StartupDirectory { get; }

    public StartupRegistrationService Service { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

file sealed class FakeRegistryStore : IStartupRegistryStore
{
    public string? Value { get; set; }

    public bool DenyWrites { get; set; }

    public bool DenyReads { get; set; }

    public string? ReadValue() => DenyReads
        ? throw new UnauthorizedAccessException("拒绝访问注册表项。")
        : Value;

    public void WriteValue(string command)
    {
        if (DenyWrites)
        {
            throw new UnauthorizedAccessException("拒绝访问注册表项。");
        }

        Value = command;
    }

    public void DeleteValue()
    {
        if (DenyWrites)
        {
            throw new UnauthorizedAccessException("拒绝访问注册表项。");
        }

        Value = null;
    }
}
