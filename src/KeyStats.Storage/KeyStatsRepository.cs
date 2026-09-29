using System.Globalization;
using System.Text;
using KeyStats.Core;
using Microsoft.Data.Sqlite;

namespace KeyStats.Storage;

public sealed class KeyStatsRepository : IDisposable
{
    public const int CurrentSchemaVersion = 1;
    private readonly string _connectionString;

    public KeyStatsRepository(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        };
        _connectionString = builder.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("数据库路径缺少父目录。");
        Directory.CreateDirectory(directory);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous=NORMAL;", cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA wal_autocheckpoint=256;", cancellationToken);

        var integrity = await ExecuteScalarStringAsync(connection, "PRAGMA quick_check;", cancellationToken);
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SQLite 快速完整性检查失败：{integrity}");
        }

        var schemaVersion = await ExecuteScalarLongAsync(connection, "PRAGMA user_version;", cancellationToken);
        if (schemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"数据库版本 {schemaVersion} 高于当前程序支持的 {CurrentSchemaVersion}，已拒绝修改。");
        }

        if (schemaVersion == 0)
        {
            await CreateSchemaAsync(connection, cancellationToken);
        }
    }

    public async Task<MinuteBucketSnapshot?> GetMinuteBucketAsync(
        long bucketStartUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT keymap_version, counts
            FROM minute_buckets
            WHERE bucket_start_utc = $start;
            """;
        command.Parameters.AddWithValue("$start", bucketStartUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        ValidateKeyMapVersion(reader.GetInt32(0));
        return new MinuteBucketSnapshot(bucketStartUtc, CountVectorCodec.Decode((byte[])reader[1]));
    }

    public async Task UpsertMinuteBucketAsync(
        MinuteBucketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ValidateMinuteSnapshot(snapshot);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO minute_buckets (
                    bucket_start_utc, keymap_version, counts, total_count, updated_at_utc)
                VALUES ($start, $keymap, $counts, $total, $updated)
                ON CONFLICT(bucket_start_utc) DO UPDATE SET
                    keymap_version = excluded.keymap_version,
                    counts = excluded.counts,
                    total_count = excluded.total_count,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$start", snapshot.BucketStartUtc);
            command.Parameters.AddWithValue("$keymap", CountVectorCodec.KeyMapVersion);
            command.Parameters.AddWithValue("$counts", CountVectorCodec.Encode(snapshot.Counts));
            command.Parameters.AddWithValue("$total", snapshot.TotalCount);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await RebuildTenMinuteRollupAsync(connection, transaction, snapshot.BucketStartUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<StatisticsAggregate> QueryAggregateAsync(
        long startUtc,
        long endUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(startUtc, endUtc);
        var totals = new ulong[KeyCatalog.Count];
        var rollupStart = Math.Min(TimeBuckets.AlignUp(startUtc, TimeBuckets.TenMinuteSeconds), endUtc);
        var rollupEnd = Math.Max(TimeBuckets.AlignDown(endUtc, TimeBuckets.TenMinuteSeconds), rollupStart);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await AccumulateRowsAsync(
            connection,
            "minute_buckets",
            startUtc,
            rollupStart,
            totals,
            cancellationToken);
        await AccumulateRowsAsync(
            connection,
            "rollup_10m",
            rollupStart,
            rollupEnd,
            totals,
            cancellationToken);
        await AccumulateRowsAsync(
            connection,
            "minute_buckets",
            rollupEnd,
            endUtc,
            totals,
            cancellationToken);

        return new StatisticsAggregate(startUtc, endUtc, totals);
    }

    public async Task<StoredDataRange?> GetDataRangeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(bucket_start_utc), MAX(bucket_start_utc) FROM minute_buckets;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            return null;
        }

        return new StoredDataRange(reader.GetInt64(0), checked(reader.GetInt64(1) + TimeBuckets.MinuteSeconds));
    }

    public async Task<ulong> QueryTotalCountAsync(
        long? excludedBucketStartUtc = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (excludedBucketStartUtc is { } excludedBucket)
        {
            command.CommandText = "SELECT COALESCE(SUM(total_count), 0) FROM minute_buckets WHERE bucket_start_utc <> $excluded;";
            command.Parameters.AddWithValue("$excluded", excludedBucket);
        }
        else
        {
            command.CommandText = "SELECT COALESCE(SUM(total_count), 0) FROM minute_buckets;";
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0UL : checked((ulong)Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<TenMinuteBucketTotal>> QueryTenMinuteTotalsAsync(
        long startUtc,
        long endUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(startUtc, endUtc);
        var totals = new Dictionary<long, ulong>();
        var rollupStart = Math.Min(TimeBuckets.AlignUp(startUtc, TimeBuckets.TenMinuteSeconds), endUtc);
        var rollupEnd = Math.Max(TimeBuckets.AlignDown(endUtc, TimeBuckets.TenMinuteSeconds), rollupStart);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await AccumulateMinuteTotalsAsync(connection, startUtc, rollupStart, totals, cancellationToken);
        await AccumulateBucketTotalsAsync(
            connection,
            "rollup_10m",
            rollupStart,
            rollupEnd,
            totals,
            cancellationToken);
        await AccumulateMinuteTotalsAsync(connection, rollupEnd, endUtc, totals, cancellationToken);

        return totals
            .OrderBy(pair => pair.Key)
            .Select(pair => new TenMinuteBucketTotal(pair.Key, pair.Value))
            .ToArray();
    }

    public async Task ExportCsvAsync(
        long startUtc,
        long endUtc,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(startUtc, endUtc);
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidOperationException("导出路径缺少父目录。");
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = fullDestinationPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT bucket_start_utc, keymap_version, counts
                FROM minute_buckets
                WHERE bucket_start_utc >= $start AND bucket_start_utc < $end
                ORDER BY bucket_start_utc;
                """;
            command.Parameters.AddWithValue("$start", startUtc);
            command.Parameters.AddWithValue("$end", endUtc);

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true))
            await using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                await writer.WriteLineAsync("minute_start_local,minute_start_utc,key_id,key_name,count");

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var minuteStart = reader.GetInt64(0);
                    ValidateKeyMapVersion(reader.GetInt32(1));
                    var counts = CountVectorCodec.Decode((byte[])reader[2]);
                    var localTime = DateTimeOffset.FromUnixTimeSeconds(minuteStart).ToLocalTime();

                    for (var keyIndex = 0; keyIndex < counts.Length; keyIndex++)
                    {
                        if (counts[keyIndex] == 0)
                        {
                            continue;
                        }

                        var key = KeyCatalog.Get((KeyId)keyIndex);
                        await writer.WriteLineAsync(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{localTime:yyyy-MM-dd HH:mm},{minuteStart},{key.Id},{CsvEscape(key.DisplayName)},{counts[keyIndex]}"));
                    }
                }

                await writer.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<string> CheckIntegrityAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await ExecuteScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken);
    }

    public long GetStorageSizeBytes()
    {
        var total = 0L;
        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                total = checked(total + new FileInfo(path).Length);
            }
        }

        return total;
    }

    public void Dispose() => SqliteConnection.ClearAllPools();

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA busy_timeout=5000;", cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys=ON;", cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous=NORMAL;", cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA wal_autocheckpoint=256;", cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        const string schema = """
            CREATE TABLE IF NOT EXISTS minute_buckets (
                bucket_start_utc INTEGER PRIMARY KEY CHECK(bucket_start_utc % 60 = 0),
                keymap_version INTEGER NOT NULL,
                counts BLOB NOT NULL,
                total_count INTEGER NOT NULL CHECK(total_count >= 0),
                updated_at_utc INTEGER NOT NULL
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS rollup_10m (
                bucket_start_utc INTEGER PRIMARY KEY CHECK(bucket_start_utc % 600 = 0),
                keymap_version INTEGER NOT NULL,
                counts BLOB NOT NULL,
                total_count INTEGER NOT NULL CHECK(total_count >= 0)
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS app_meta (
                name TEXT PRIMARY KEY,
                value TEXT NOT NULL
            ) WITHOUT ROWID;

            INSERT INTO app_meta(name, value) VALUES('schema_version', '1')
            ON CONFLICT(name) DO UPDATE SET value = excluded.value;
            PRAGMA user_version=1;
            """;
        await ExecuteNonQueryAsync(connection, transaction, schema, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RebuildTenMinuteRollupAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long changedMinuteStart,
        CancellationToken cancellationToken)
    {
        var rollupStart = TimeBuckets.ToTenMinuteStart(changedMinuteStart);
        var rollupCounts = new ulong[KeyCatalog.Count];
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT keymap_version, counts
                FROM minute_buckets
                WHERE bucket_start_utc >= $start AND bucket_start_utc < $end;
                """;
            query.Parameters.AddWithValue("$start", rollupStart);
            query.Parameters.AddWithValue("$end", rollupStart + TimeBuckets.TenMinuteSeconds);

            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ValidateKeyMapVersion(reader.GetInt32(0));
                AddCounts(rollupCounts, CountVectorCodec.Decode((byte[])reader[1]));
            }
        }

        var encodedCounts = new uint[KeyCatalog.Count];
        long total = 0;
        for (var index = 0; index < rollupCounts.Length; index++)
        {
            encodedCounts[index] = checked((uint)rollupCounts[index]);
            total = checked(total + encodedCounts[index]);
        }

        await using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO rollup_10m (bucket_start_utc, keymap_version, counts, total_count)
            VALUES ($start, $keymap, $counts, $total)
            ON CONFLICT(bucket_start_utc) DO UPDATE SET
                keymap_version = excluded.keymap_version,
                counts = excluded.counts,
                total_count = excluded.total_count;
            """;
        upsert.Parameters.AddWithValue("$start", rollupStart);
        upsert.Parameters.AddWithValue("$keymap", CountVectorCodec.KeyMapVersion);
        upsert.Parameters.AddWithValue("$counts", CountVectorCodec.Encode(encodedCounts));
        upsert.Parameters.AddWithValue("$total", total);
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AccumulateRowsAsync(
        SqliteConnection connection,
        string tableName,
        long startUtc,
        long endUtc,
        ulong[] destination,
        CancellationToken cancellationToken)
    {
        if (startUtc >= endUtc)
        {
            return;
        }

        if (tableName is not ("minute_buckets" or "rollup_10m"))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT keymap_version, counts
            FROM {tableName}
            WHERE bucket_start_utc >= $start AND bucket_start_utc < $end;
            """;
        command.Parameters.AddWithValue("$start", startUtc);
        command.Parameters.AddWithValue("$end", endUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ValidateKeyMapVersion(reader.GetInt32(0));
            AddCounts(destination, CountVectorCodec.Decode((byte[])reader[1]));
        }
    }

    private static async Task AccumulateMinuteTotalsAsync(
        SqliteConnection connection,
        long startUtc,
        long endUtc,
        Dictionary<long, ulong> destination,
        CancellationToken cancellationToken)
    {
        if (startUtc >= endUtc)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bucket_start_utc, total_count
            FROM minute_buckets
            WHERE bucket_start_utc >= $start AND bucket_start_utc < $end;
            """;
        command.Parameters.AddWithValue("$start", startUtc);
        command.Parameters.AddWithValue("$end", endUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var bucketStart = TimeBuckets.ToTenMinuteStart(reader.GetInt64(0));
            AddBucketTotal(destination, bucketStart, checked((ulong)reader.GetInt64(1)));
        }
    }

    private static async Task AccumulateBucketTotalsAsync(
        SqliteConnection connection,
        string tableName,
        long startUtc,
        long endUtc,
        Dictionary<long, ulong> destination,
        CancellationToken cancellationToken)
    {
        if (startUtc >= endUtc)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT bucket_start_utc, total_count
            FROM {tableName}
            WHERE bucket_start_utc >= $start AND bucket_start_utc < $end;
            """;
        command.Parameters.AddWithValue("$start", startUtc);
        command.Parameters.AddWithValue("$end", endUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AddBucketTotal(destination, reader.GetInt64(0), checked((ulong)reader.GetInt64(1)));
        }
    }

    private static void AddBucketTotal(Dictionary<long, ulong> destination, long bucketStart, ulong count)
    {
        destination.TryGetValue(bucketStart, out var existing);
        destination[bucketStart] = checked(existing + count);
    }

    private static void AddCounts(ulong[] destination, uint[] source)
    {
        if (destination.Length != source.Length)
        {
            throw new InvalidDataException("计数向量长度不一致。");
        }

        for (var index = 0; index < source.Length; index++)
        {
            destination[index] = checked(destination[index] + source[index]);
        }
    }

    private static void ValidateMinuteSnapshot(MinuteBucketSnapshot snapshot)
    {
        if (snapshot.BucketStartUtc % TimeBuckets.MinuteSeconds != 0)
        {
            throw new ArgumentException("分钟分段起点没有对齐到整分钟。", nameof(snapshot));
        }

        if (snapshot.Counts.Length != KeyCatalog.Count)
        {
            throw new ArgumentException("分钟分段计数向量长度不正确。", nameof(snapshot));
        }
    }

    private static void ValidateRange(long startUtc, long endUtc)
    {
        if (startUtc % TimeBuckets.MinuteSeconds != 0 || endUtc % TimeBuckets.MinuteSeconds != 0)
        {
            throw new ArgumentException("查询范围必须对齐到整分钟。", nameof(startUtc));
        }

        if (endUtc <= startUtc)
        {
            throw new ArgumentException("结束时间必须晚于开始时间。", nameof(endUtc));
        }
    }

    private static void ValidateKeyMapVersion(int keyMapVersion)
    {
        if (keyMapVersion < CountVectorCodec.MinimumSupportedKeyMapVersion ||
            keyMapVersion > CountVectorCodec.KeyMapVersion)
        {
            throw new InvalidDataException($"不支持的 Key Map 版本：{keyMapVersion}。");
        }
    }

    private static string CsvEscape(string value) =>
        '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string> ExecuteScalarStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture)
            ?? string.Empty;
    }
}
