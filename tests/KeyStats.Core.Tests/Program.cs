using System.Runtime.InteropServices;
using KeyStats.Core;
using KeyStats.Storage;

var tests = new (string Name, Func<Task> Run)[]
{
    ("标准字母按扫描码映射", Sync(StandardLetterMapsByScanCode)),
    ("左右修饰键分离", Sync(LeftAndRightModifiersAreDistinct)),
    ("主 Enter 与数字键盘 Enter 分离", Sync(MainAndNumpadEnterAreDistinct)),
    ("导航键与数字键盘分离", Sync(NavigationAndNumpadAreDistinct)),
    ("F13 到 F24 映射", Sync(ExtendedFunctionKeysMap)),
    ("媒体键回退映射", Sync(MediaKeyFallbackMaps)),
    ("媒体虚拟键优先于冲突扫描码", Sync(MediaVirtualKeyWinsOverScanCode)),
    ("长按自动连发只计一次", Sync(RepeatedMakeCountsOnce)),
    ("松开后再次按下重新计数", Sync(ReleaseThenPressCountsAgain)),
    ("两把键盘分别按下会各计一次", Sync(TwoDevicesCountSeparately)),
    ("组合键中的每个物理键分别计数", Sync(ChordKeysCountSeparately)),
    ("系统切换丢失松开消息后可恢复计数", Sync(SystemSwitchReleaseCanBeReconciled)),
    ("Alt+Tab 补偿与 Raw Input 不会重复计数", Sync(AltTabFallbackAndRawInputAreDeduplicated)),
    ("鼠标左右键分别计数且按住不连发", Sync(MouseButtonsCountSeparately)),
    ("暂停期间不计数且恢复不误计长按", Sync(PauseMaintainsPressedState)),
    ("清零不破坏按下状态", Sync(ClearDoesNotBreakPressedState)),
    ("界面清零可重置残留按下状态", Sync(ClearCanResetPressedState)),
    ("设备移除会清理按下状态", Sync(RemovingDeviceClearsPressedState)),
    ("未知键不会写入计数", Sync(UnknownKeyIsIgnored)),
    ("稀疏计数向量往返一致", Sync(CountVectorRoundTrips)),
    ("阶段 2 的 Key Map v1 数据仍可读取", LegacyKeyMapRowsRemainReadableAsync),
    ("数据库一分钟写入与绝对快照更新", RepositoryUpsertIsAbsoluteAsync),
    ("全部范围边界覆盖首末有效分钟", DataRangeTracksSparseBoundsAsync),
    ("历史总数量可排除当前分钟", TotalCountCanExcludeCurrentMinuteAsync),
    ("十分钟派生汇总正确", TenMinuteRollupIsCorrectAsync),
    ("十分钟时段序列正确处理范围边缘", TenMinuteSeriesHandlesRangeEdgesAsync),
    ("同一分钟重启后继续累计", RecorderResumesCurrentMinuteAsync),
    ("CSV 导出包含分钟与键位", CsvExportContainsMinuteRowsAsync),
    ("设置文件可原子创建和读取", SettingsStoreRoundTripsAsync),
    ("PrtSc 的假 Shift 不会误计为 Shift", Sync(FakeShiftForPrintScreenIsNotCounted)),
    ("Raw Input 缺少按下事件时轮询可补计", Sync(PolledPressCoversMissingMake)),
    ("两个来源不会重复计数（Raw 先到）", Sync(DualSourceDeduplicatesWhenRawArrivesFirst)),
    ("两个来源不会重复计数（轮询先到）", Sync(DualSourceDeduplicatesWhenPollArrivesFirst)),
    ("丢失松开后物理复核可自愈并重新计数", Sync(LostBreakIsReconciledPhysically)),
    ("物理仍按下的键与无法观测的键不会被误释放", Sync(ReconcileKeepsPhysicallyHeldKey)),
    ("NumLock 关闭时小键盘别名不会重复计数", Sync(NumpadAliasDoesNotDoubleCount)),
    ("轮询映射覆盖键位目录", Sync(PolledKeyMapCoversCatalog)),
    ("轮询映射区分左右修饰键", Sync(PolledKeyMapKeepsLeftRightDistinct)),
    ("轮询映射为每个键保留全部可用虚拟键", Sync(PolledKeyMapKeepsAllVirtualKeys)),
    ("自愈判断按物理别名进行（NumLock 关闭的小键盘）", Sync(ReconcileUsesPhysicalAliases)),
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        await run();
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

static Func<Task> Sync(Action action) => () =>
{
    action();
    return Task.CompletedTask;
};

static void StandardLetterMapsByScanCode()
{
    Equal(KeyId.A, Normalize(0x1E, 0x41));
    Equal(KeyId.Q, Normalize(0x10, 0x51));
    Equal(KeyId.Space, Normalize(0x39, 0x20));
}

static void LeftAndRightModifiersAreDistinct()
{
    Equal(KeyId.LeftShift, Normalize(0x2A, 0x10));
    Equal(KeyId.RightShift, Normalize(0x36, 0x10));
    Equal(KeyId.LeftControl, Normalize(0x1D, 0x11));
    Equal(KeyId.RightControl, Normalize(0x1D, 0x11, RawKeyFlags.E0));
    Equal(KeyId.LeftAlt, Normalize(0x38, 0x12));
    Equal(KeyId.RightAlt, Normalize(0x38, 0x12, RawKeyFlags.E0));
}

static void MainAndNumpadEnterAreDistinct()
{
    Equal(KeyId.Enter, Normalize(0x1C, 0x0D));
    Equal(KeyId.NumpadEnter, Normalize(0x1C, 0x0D, RawKeyFlags.E0));
}

static void NavigationAndNumpadAreDistinct()
{
    Equal(KeyId.Numpad7, Normalize(0x47, 0x67));
    Equal(KeyId.Home, Normalize(0x47, 0x24, RawKeyFlags.E0));
    Equal(KeyId.NumpadDecimal, Normalize(0x53, 0x6E));
    Equal(KeyId.Delete, Normalize(0x53, 0x2E, RawKeyFlags.E0));
}

static void ExtendedFunctionKeysMap()
{
    Equal(KeyId.F13, Normalize(0, 0x7C));
    Equal(KeyId.F24, Normalize(0, 0x87));
}

static void MediaKeyFallbackMaps()
{
    Equal(KeyId.MediaPlayPause, Normalize(0, 0xB3, RawKeyFlags.E0));
    Equal(KeyId.VolumeUp, Normalize(0, 0xAF, RawKeyFlags.E0));
}

static void MediaVirtualKeyWinsOverScanCode()
{
    Equal(KeyId.VolumeMute, Normalize(0x22, 0xAD, RawKeyFlags.E0));
}

static void RepeatedMakeCountsOnce()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.Process(1, MakeA());
    counter.Process(1, MakeA());
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void ReleaseThenPressCountsAgain()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.Process(1, BreakA());
    counter.Process(1, MakeA());
    var snapshot = counter.GetSnapshot();
    Equal(2L, snapshot.Counts[(int)KeyId.A]);
    Equal(2L, snapshot.TotalCount);
}

static void TwoDevicesCountSeparately()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.Process(2, MakeA());
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void ChordKeysCountSeparately()
{
    var counter = new KeyboardCounter();

    // Ctrl+C：两个按下消息都必须分别计数。
    counter.Process(1, new RawKeyEvent(0x1D, RawKeyFlags.None, 0x11));
    counter.Process(1, new RawKeyEvent(0x2E, RawKeyFlags.None, 0x43));
    counter.Process(1, new RawKeyEvent(0x2E, RawKeyFlags.Break, 0x43));

    // Ctrl 保持按下时再按 V：Ctrl 仍是一次物理按下，V 另计一次。
    counter.Process(1, new RawKeyEvent(0x2F, RawKeyFlags.None, 0x56));
    counter.Process(1, new RawKeyEvent(0x2F, RawKeyFlags.Break, 0x56));
    counter.Process(1, new RawKeyEvent(0x1D, RawKeyFlags.Break, 0x11));

    var snapshot = counter.GetSnapshot();
    Equal(1L, snapshot.Counts[(int)KeyId.LeftControl]);
    Equal(1L, snapshot.Counts[(int)KeyId.C]);
    Equal(1L, snapshot.Counts[(int)KeyId.V]);
    Equal(3L, snapshot.TotalCount);
}

static void SystemSwitchReleaseCanBeReconciled()
{
    var counter = new KeyboardCounter();
    var altDown = new RawKeyEvent(0x38, RawKeyFlags.None, 0x12);
    var tabDown = new RawKeyEvent(0x0F, RawKeyFlags.None, 0x09);

    counter.Process(1, altDown);
    counter.Process(1, tabDown);

    // 模拟 Windows 接管 Alt+Tab 后，应用没有收到对应松开消息。
    counter.ReleaseKey(KeyId.LeftAlt);
    counter.ReleaseKey(KeyId.Tab);
    counter.Process(1, altDown);
    counter.Process(1, tabDown);

    var snapshot = counter.GetSnapshot();
    Equal(2L, snapshot.Counts[(int)KeyId.LeftAlt]);
    Equal(2L, snapshot.Counts[(int)KeyId.Tab]);
}

static void AltTabFallbackAndRawInputAreDeduplicated()
{
    var counter = new KeyboardCounter();
    var tabDown = new RawKeyEvent(0x0F, RawKeyFlags.None, 0x09);
    var tabUp = new RawKeyEvent(0x0F, RawKeyFlags.Break, 0x09);

    Equal(true, counter.ProcessButton(0, KeyId.Tab, isBreak: false).Counted);
    Equal(false, counter.ProcessDetailed(7, tabDown).Counted);
    counter.ProcessDetailed(7, tabUp);

    Equal(true, counter.ProcessDetailed(7, tabDown).Counted);
    Equal(false, counter.ProcessButton(0, KeyId.Tab, isBreak: false).Counted);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.Tab]);
}

static void MouseButtonsCountSeparately()
{
    var counter = new KeyboardCounter();
    Equal(true, counter.ProcessButton(9, KeyId.MouseLeftButton, isBreak: false).Counted);
    Equal(false, counter.ProcessButton(9, KeyId.MouseLeftButton, isBreak: false).Counted);
    counter.ProcessButton(9, KeyId.MouseRightButton, isBreak: false);

    var heldSnapshot = counter.GetSnapshot();
    Equal(1L, heldSnapshot.Counts[(int)KeyId.MouseLeftButton]);
    Equal(1L, heldSnapshot.Counts[(int)KeyId.MouseRightButton]);
    Equal(true, heldSnapshot.PressedKeys.Contains(KeyId.MouseLeftButton));
    Equal(true, heldSnapshot.PressedKeys.Contains(KeyId.MouseRightButton));

    counter.ProcessButton(9, KeyId.MouseLeftButton, isBreak: true);
    counter.ProcessButton(9, KeyId.MouseLeftButton, isBreak: false);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.MouseLeftButton]);
}

static void PauseMaintainsPressedState()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.SetPaused(true);
    counter.Process(1, BreakA());
    counter.Process(1, MakeA());
    counter.SetPaused(false);
    counter.Process(1, MakeA());
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.A]);
    counter.Process(1, BreakA());
    counter.Process(1, MakeA());
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void ClearDoesNotBreakPressedState()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.ClearCounts();
    counter.Process(1, MakeA());
    Equal(0L, counter.GetSnapshot().Counts[(int)KeyId.A]);
    counter.Process(1, BreakA());
    counter.Process(1, MakeA());
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void ClearCanResetPressedState()
{
    var counter = new KeyboardCounter();
    counter.Process(1, MakeA());
    counter.ClearCounts(resetPressedState: true);
    counter.Process(1, MakeA());
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void RemovingDeviceClearsPressedState()
{
    var counter = new KeyboardCounter();
    counter.Process(17, MakeA());
    counter.RemoveDevice(17);
    counter.Process(17, MakeA());
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void UnknownKeyIsIgnored()
{
    var counter = new KeyboardCounter();
    Equal(false, counter.Process(1, new RawKeyEvent(0x7F, RawKeyFlags.None, 0xFE)));
    Equal(0L, counter.GetSnapshot().TotalCount);
}

static void CountVectorRoundTrips()
{
    var counts = new uint[KeyCatalog.Count];
    counts[(int)KeyId.A] = 17;
    counts[(int)KeyId.Space] = 43;
    counts[(int)KeyId.NumpadEnter] = 2;
    var encoded = CountVectorCodec.Encode(counts);
    var decoded = CountVectorCodec.Decode(encoded);
    Equal(counts.Length, decoded.Length);
    for (var index = 0; index < counts.Length; index++)
    {
        Equal(counts[index], decoded[index]);
    }
}

static async Task LegacyKeyMapRowsRemainReadableAsync()
{
    await WithRepositoryAsync(async (directory, repository) =>
    {
        const long minuteStart = 1_800_000_000;
        var blob = CountVectorCodec.Encode(NewCounts((KeyId.C, 4), (KeyId.LeftControl, 2)));
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "test.db"),
        }.ToString();

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO minute_buckets (
                    bucket_start_utc, keymap_version, counts, total_count, updated_at_utc)
                VALUES ($start, 1, $counts, 6, $start);
                """;
            command.Parameters.AddWithValue("$start", minuteStart);
            command.Parameters.AddWithValue("$counts", blob);
            await command.ExecuteNonQueryAsync();
        }

        var aggregate = await repository.QueryAggregateAsync(minuteStart, minuteStart + 60);
        Equal(4UL, aggregate.Counts[(int)KeyId.C]);
        Equal(2UL, aggregate.Counts[(int)KeyId.LeftControl]);
        Equal(0UL, aggregate.Counts[(int)KeyId.MouseLeftButton]);
    });
}

static async Task RepositoryUpsertIsAbsoluteAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long minuteStart = 1_800_000_000;
        var counts = NewCounts((KeyId.A, 3));
        await repository.UpsertMinuteBucketAsync(new MinuteBucketSnapshot(minuteStart, counts));

        var loaded = await repository.GetMinuteBucketAsync(minuteStart);
        Equal(3u, loaded!.Counts[(int)KeyId.A]);

        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(minuteStart, NewCounts((KeyId.A, 5))));
        var aggregate = await repository.QueryAggregateAsync(minuteStart, minuteStart + 60);
        Equal(5UL, aggregate.TotalCount);
        Equal("ok", await repository.CheckIntegrityAsync());
    });
}

static async Task DataRangeTracksSparseBoundsAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long firstMinute = 1_800_000_000;
        const long lastMinute = firstMinute + 3_600;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(firstMinute, NewCounts((KeyId.A, 1))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(lastMinute, NewCounts((KeyId.MouseLeftButton, 2))));

        var range = await repository.GetDataRangeAsync();
        Equal(firstMinute, range!.StartUtc);
        Equal(lastMinute + 60, range.EndUtc);
    });
}

static async Task TotalCountCanExcludeCurrentMinuteAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long firstMinute = 1_800_000_000;
        const long currentMinute = firstMinute + 60;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(firstMinute, NewCounts((KeyId.A, 2))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(currentMinute, NewCounts((KeyId.W, 3))));

        Equal(5UL, await repository.QueryTotalCountAsync());
        Equal(2UL, await repository.QueryTotalCountAsync(currentMinute));
    });
}

static async Task TenMinuteRollupIsCorrectAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long rollupStart = 1_800_000_000;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(rollupStart, NewCounts((KeyId.A, 2))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(rollupStart + 60, NewCounts((KeyId.B, 3))));

        var aggregate = await repository.QueryAggregateAsync(rollupStart, rollupStart + 600);
        Equal(5UL, aggregate.TotalCount);
        Equal(2UL, aggregate.Counts[(int)KeyId.A]);
        Equal(3UL, aggregate.Counts[(int)KeyId.B]);
    });
}

static async Task TenMinuteSeriesHandlesRangeEdgesAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        const long baseTime = 1_800_000_000;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(baseTime + 60, NewCounts((KeyId.A, 1))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(baseTime + 600, NewCounts((KeyId.B, 2))));
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(baseTime + 1_200, NewCounts((KeyId.C, 3))));

        var series = await repository.QueryTenMinuteTotalsAsync(baseTime + 60, baseTime + 1_260);
        Equal(3, series.Count);
        Equal(baseTime, series[0].BucketStartUtc);
        Equal(1UL, series[0].TotalCount);
        Equal(baseTime + 600, series[1].BucketStartUtc);
        Equal(2UL, series[1].TotalCount);
        Equal(baseTime + 1_200, series[2].BucketStartUtc);
        Equal(3UL, series[2].TotalCount);
    });
}

static async Task RecorderResumesCurrentMinuteAsync()
{
    await WithRepositoryAsync(async (_, repository) =>
    {
        var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_030));
        await using (var recorder = await StatisticsRecorder.CreateAsync(
            repository,
            TimeSpan.FromHours(1),
            clock))
        {
            recorder.RecordKeyPress(KeyId.A);
            await recorder.FlushNowAsync();
        }

        await using (var recorder = await StatisticsRecorder.CreateAsync(
            repository,
            TimeSpan.FromHours(1),
            clock))
        {
            recorder.RecordKeyPress(KeyId.A);
            await recorder.FlushNowAsync();
        }

        var minuteStart = TimeBuckets.ToMinuteStart(clock.UtcNow);
        var aggregate = await repository.QueryAggregateAsync(minuteStart, minuteStart + 60);
        Equal(2UL, aggregate.TotalCount);
    });
}

static async Task CsvExportContainsMinuteRowsAsync()
{
    await WithRepositoryAsync(async (directory, repository) =>
    {
        const long minuteStart = 1_800_000_000;
        await repository.UpsertMinuteBucketAsync(
            new MinuteBucketSnapshot(minuteStart, NewCounts((KeyId.Space, 4))));
        var csvPath = Path.Combine(directory, "export.csv");
        await repository.ExportCsvAsync(minuteStart, minuteStart + 60, csvPath);
        var csv = await File.ReadAllTextAsync(csvPath);
        Contains("minute_start_local", csv);
        Contains("Space", csv);
        Contains(",4", csv);
    });
}

static async Task SettingsStoreRoundTripsAsync()
{
    var directory = CreateTestDirectory();
    try
    {
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var settings = await store.LoadOrCreateAsync();
        Equal(30, settings.FlushIntervalSeconds);
        settings.FlushIntervalSeconds = 45;
        await store.SaveAsync(settings);
        Equal(45, (await store.LoadOrCreateAsync()).FlushIntervalSeconds);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void FakeShiftForPrintScreenIsNotCounted()
{
    // Windows 为 PrtSc 注入的假 Shift：Scan 0x2A + VK 0xFF + E0（实测于本机）。
    // 它必须保持“不可归一化”，否则每次按 PrtSc 都会误计一次左 Shift。
    Equal(null, Normalize(0x2A, 0xFF, RawKeyFlags.E0));

    // 真实左 Shift 与 PrtSc 自身的映射不受影响。
    Equal(KeyId.LeftShift, Normalize(0x2A, 0x10));
    Equal(KeyId.PrintScreen, Normalize(0x37, 0x2C, RawKeyFlags.E0));
}

static void PolledPressCoversMissingMake()
{
    var counter = new KeyboardCounter();

    // PrtSc 实测只有“松开”事件到达 Raw Input，永远数不到。
    Equal(false, counter.ProcessButton(1, KeyId.PrintScreen, isBreak: true).Counted);
    Equal(0L, counter.GetSnapshot().Counts[(int)KeyId.PrintScreen]);

    // 轮询兜底看到物理按下 → 计数，并被记为“轮询兜底”。
    Equal(true, counter.ProcessPolledButton(KeyId.PrintScreen, isDown: true).Counted);
    counter.ProcessPolledButton(KeyId.PrintScreen, isDown: false);
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.PrintScreen]);
    Equal(1L, counter.PollOnlyCountedPresses);

    // 第二次按下依然只计一次，且松开事件不会把计数抹掉。
    counter.ProcessButton(1, KeyId.PrintScreen, isBreak: true);
    Equal(true, counter.ProcessPolledButton(KeyId.PrintScreen, isDown: true).Counted);
    counter.ProcessPolledButton(KeyId.PrintScreen, isDown: false);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.PrintScreen]);
}

static void DualSourceDeduplicatesWhenRawArrivesFirst()
{
    var counter = new KeyboardCounter();
    Equal(true, counter.ProcessButton(7, KeyId.Z, isBreak: false).Counted);

    // Raw Input 已经在管这个键，轮询只记录状态，不重复计数。
    Equal(false, counter.ProcessPolledButton(KeyId.Z, isDown: true).Counted);
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.Z]);

    counter.ProcessButton(7, KeyId.Z, isBreak: true);
    counter.ProcessPolledButton(KeyId.Z, isDown: false);

    Equal(true, counter.ProcessButton(7, KeyId.Z, isBreak: false).Counted);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.Z]);
}

static void DualSourceDeduplicatesWhenPollArrivesFirst()
{
    var counter = new KeyboardCounter();
    Equal(true, counter.ProcessPolledButton(KeyId.Z, isDown: true).Counted);

    // 轮询已经计过这一次物理按下，随后到达的 Raw Input 不能重复计数。
    Equal(false, counter.ProcessButton(7, KeyId.Z, isBreak: false).Counted);
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.Z]);

    counter.ProcessButton(7, KeyId.Z, isBreak: true);
    counter.ProcessPolledButton(KeyId.Z, isDown: false);

    Equal(true, counter.ProcessButton(7, KeyId.Z, isBreak: false).Counted);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.Z]);
}

static void LostBreakIsReconciledPhysically()
{
    var counter = new KeyboardCounter();
    Equal(true, counter.ProcessButton(3, KeyId.A, isBreak: false).Counted);

    // 松开事件被吞掉：按键卡在“按下中”，之后所有按下都会被误判成长按连发。
    Equal(true, counter.GetSnapshot().PressedKeys.Contains(KeyId.A));

    var observable = new HashSet<KeyId> { KeyId.A };
    Equal(1, counter.ReconcilePhysicallyUp(observable, new HashSet<KeyId>()));
    Equal(false, counter.GetSnapshot().PressedKeys.Contains(KeyId.A));
    Equal(1L, counter.ReconciledReleaseCount);

    // 自愈之后该键可以重新计数。
    Equal(true, counter.ProcessButton(3, KeyId.A, isBreak: false).Counted);
    Equal(2L, counter.GetSnapshot().Counts[(int)KeyId.A]);
}

static void ReconcileKeepsPhysicallyHeldKey()
{
    var counter = new KeyboardCounter();
    counter.ProcessButton(3, KeyId.A, isBreak: false);

    var observable = new HashSet<KeyId> { KeyId.A };
    var stillDown = new HashSet<KeyId> { KeyId.A };
    Equal(0, counter.ReconcilePhysicallyUp(observable, stillDown));
    Equal(true, counter.GetSnapshot().PressedKeys.Contains(KeyId.A));

    // 轮询源观测不到的键不能被误释放（避免把无法采样的键全部清空）。
    Equal(0, counter.ReconcilePhysicallyUp(new HashSet<KeyId>(), new HashSet<KeyId>()));
    Equal(true, counter.GetSnapshot().PressedKeys.Contains(KeyId.A));
}

static void NumpadAliasDoesNotDoubleCount()
{
    // NumLock 关闭：Raw Input 报 Numpad2（扫描码 0x50 + E0），轮询按虚拟键报 ArrowDown。
    var rawFirst = new KeyboardCounter();
    Equal(true, rawFirst.ProcessButton(5, KeyId.Numpad2, isBreak: false).Counted);
    Equal(false, rawFirst.ProcessPolledButton(KeyId.ArrowDown, isDown: true).Counted);
    Equal(1L, rawFirst.GetSnapshot().Counts[(int)KeyId.Numpad2]);
    Equal(0L, rawFirst.GetSnapshot().Counts[(int)KeyId.ArrowDown]);

    // 反向顺序同样只计一次。
    var pollFirst = new KeyboardCounter();
    Equal(true, pollFirst.ProcessPolledButton(KeyId.ArrowDown, isDown: true).Counted);
    Equal(false, pollFirst.ProcessButton(5, KeyId.Numpad2, isBreak: false).Counted);
    Equal(1L, pollFirst.GetSnapshot().Counts[(int)KeyId.Numpad2] + pollFirst.GetSnapshot().Counts[(int)KeyId.ArrowDown]);
}

static int[] VirtualKeysOf(IReadOnlyDictionary<KeyId, int[]> map, KeyId keyId) =>
    map.TryGetValue(keyId, out var virtualKeys) && virtualKeys is not null
        ? virtualKeys
        : throw new InvalidOperationException($"轮询映射缺少键位：{keyId}。");

static void PolledKeyMapCoversCatalog()
{
    var map = PolledKeyMap.Build(virtualKey => NativeMethods.MapVirtualKey((uint)virtualKey, 4));

    // Raw Input 已经可靠覆盖、且无法用虚拟键采样的键允许不在映射里。
    KeyId[] allowed = [KeyId.NumpadEnter];
    var unexpected = Enum.GetValues<KeyId>()
        .Where(id => !map.ContainsKey(id) && !allowed.Contains(id))
        .ToArray();
    if (unexpected.Length > 0)
    {
        throw new InvalidOperationException($"轮询映射缺少键位：{string.Join(", ", unexpected)}");
    }

    Equal(true, VirtualKeysOf(map, KeyId.PrintScreen).Contains(0x2C));
    Equal(true, VirtualKeysOf(map, KeyId.Z).Contains(0x5A));
}

static void PolledKeyMapKeepsLeftRightDistinct()
{
    var map = PolledKeyMap.Build(virtualKey => NativeMethods.MapVirtualKey((uint)virtualKey, 4));
    Equal(0, VirtualKeysOf(map, KeyId.LeftShift).Intersect(VirtualKeysOf(map, KeyId.RightShift)).Count());
    Equal(0, VirtualKeysOf(map, KeyId.LeftControl).Intersect(VirtualKeysOf(map, KeyId.RightControl)).Count());
    Equal(0, VirtualKeysOf(map, KeyId.LeftAlt).Intersect(VirtualKeysOf(map, KeyId.RightAlt)).Count());
}

static void PolledKeyMapKeepsAllVirtualKeys()
{
    var map = PolledKeyMap.Build(virtualKey => NativeMethods.MapVirtualKey((uint)virtualKey, 4));

    // 数字键盘 5：NumLock 关闭时是 VK_CLEAR(0x0C)，打开时是 VK_NUMPAD5(0x65)。
    // 两者都必须保留，否则按下状态自愈会在其中一种 NumLock 状态下把它误判为已抬起。
    Equal(true, VirtualKeysOf(map, KeyId.Numpad5).Contains(0x0C));
    Equal(true, VirtualKeysOf(map, KeyId.Numpad5).Contains(0x65));

    Equal(true, VirtualKeysOf(map, KeyId.Numpad0).Contains(0x60));
    Equal(true, VirtualKeysOf(map, KeyId.MouseLeftButton).Contains(0x01));
    Equal(true, VirtualKeysOf(map, KeyId.MouseRightButton).Contains(0x02));
}

static void ReconcileUsesPhysicalAliases()
{
    var counter = new KeyboardCounter();

    // NumLock 关闭：Raw Input 报 Numpad0，而轮询侧观测到的是别名 Insert 仍按着。
    Equal(true, counter.ProcessButton(4, KeyId.Numpad0, isBreak: false).Counted);
    var observable = new HashSet<KeyId> { KeyId.Numpad0, KeyId.Insert };
    var aliasStillDown = new HashSet<KeyId> { KeyId.Insert };

    // 别名仍按下 → 不能释放；否则随后的长按连发会被当成新的一次按下而重复计数。
    Equal(0, counter.ReconcilePhysicallyUp(observable, aliasStillDown));
    Equal(true, counter.GetSnapshot().PressedKeys.Contains(KeyId.Numpad0));
    Equal(false, counter.ProcessButton(4, KeyId.Numpad0, isBreak: false).Counted);
    Equal(1L, counter.GetSnapshot().Counts[(int)KeyId.Numpad0]);

    // 真正抬起后才释放。
    Equal(1, counter.ReconcilePhysicallyUp(observable, new HashSet<KeyId>()));
    Equal(false, counter.GetSnapshot().PressedKeys.Contains(KeyId.Numpad0));
}

static async Task WithRepositoryAsync(Func<string, KeyStatsRepository, Task> test)
{
    var directory = CreateTestDirectory();
    try
    {
        using var repository = new KeyStatsRepository(Path.Combine(directory, "test.db"));
        await repository.InitializeAsync();
        await test(directory, repository);
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}

static string CreateTestDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), "KeyStats.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static uint[] NewCounts(params (KeyId Key, uint Count)[] values)
{
    var counts = new uint[KeyCatalog.Count];
    foreach (var (key, count) in values)
    {
        counts[(int)key] = count;
    }

    return counts;
}

static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"没有找到期望文本：{expected}");
    }
}

static KeyId? Normalize(ushort scanCode, ushort virtualKey, RawKeyFlags flags = RawKeyFlags.None) =>
    KeyNormalizer.Normalize(new RawKeyEvent(scanCode, flags, virtualKey));

static RawKeyEvent MakeA() => new(0x1E, RawKeyFlags.None, 0x41);
static RawKeyEvent BreakA() => new(0x1E, RawKeyFlags.Break, 0x41);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}。");
    }
}

file static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKey(uint code, uint mapType);
}

file sealed class ManualClock(DateTimeOffset utcNow) : IUtcClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
