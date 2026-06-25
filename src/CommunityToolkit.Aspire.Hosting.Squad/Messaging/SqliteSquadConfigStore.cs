using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Aspire.Hosting;

/// <summary>
/// SQLite-backed key-value config store. Shares the same database as the message bus.
/// </summary>
public sealed class SqliteSquadConfigStore : ISquadConfigStore
{
    private static readonly ActivitySource ActivitySource = new("Squad.Config", "1.0.0");

    private readonly string _connectionString;

    public SqliteSquadConfigStore(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(dbPath),
        }.ToString();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var activity = ActivitySource.StartActivity("squad.config.get", ActivityKind.Internal);
        activity?.SetTag("config.key", key);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM config WHERE key = @key;";
        command.Parameters.AddWithValue("@key", key);

        var result = await command.ExecuteScalarAsync(ct);
        var value = result as string;
        activity?.SetTag("config.found", value is not null);
        return value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        using var activity = ActivitySource.StartActivity("squad.config.set", ActivityKind.Internal);
        activity?.SetTag("config.key", key);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO config (key, value, updated_at) VALUES (@key, @value, @now)
            ON CONFLICT(key) DO UPDATE SET value = @value, updated_at = @now;
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM config WHERE key = @key;";
        command.Parameters.AddWithValue("@key", key);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("squad.config.list", ActivityKind.Internal);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM config ORDER BY key;";

        var result = new Dictionary<string, string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        activity?.SetTag("config.count", result.Count);
        return result;
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("squad.config.clear", ActivityKind.Internal);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM config;";
        var deletedCount = await command.ExecuteNonQueryAsync(ct);
        activity?.SetTag("config.deleted_count", deletedCount);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
