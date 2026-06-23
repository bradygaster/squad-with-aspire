using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Data.Sqlite;

namespace Aspire.Hosting;

public sealed class SqliteSquadMessageBus : ISquadMessageBus, IDisposable
{
    private static readonly ActivitySource ActivitySource = new(SquadMessagingServiceExtensions.ActivitySourceName, "1.0.0");

    private readonly string _connectionString;

    private readonly ConcurrentDictionary<string, Channel<SquadMessage>> _channels = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public SqliteSquadMessageBus(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(dbPath),
        }.ToString();

        var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                from_squad TEXT NOT NULL,
                to_squad TEXT NOT NULL,
                subject TEXT NOT NULL,
                body TEXT NOT NULL,
                correlation_id TEXT NULL,
                reply_to TEXT NULL,
                timestamp TEXT NOT NULL,
                is_read INTEGER NOT NULL,
                trace_id TEXT NULL,
                span_id TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public async Task SendAsync(SquadMessage message, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);

        using var activity = ActivitySource.StartActivity(
            $"squad.message.send {message.From} → {message.To}",
            ActivityKind.Producer);

        activity?.SetTag("messaging.system", "squad-bus");
        activity?.SetTag("messaging.destination.name", message.To);
        activity?.SetTag("messaging.source.name", message.From);
        activity?.SetTag("messaging.message.id", message.Id);
        activity?.SetTag("messaging.message.subject", message.Subject);
        activity?.SetTag("squad.message.to", message.To);
        activity?.SetTag("squad.message.from", message.From);
        activity?.SetTag("messaging.correlation_id", message.CorrelationId);

        // Stamp trace context onto the message for downstream consumers
        var tracedMessage = message with
        {
            TraceId = activity?.TraceId.ToString() ?? message.TraceId,
            SpanId = activity?.SpanId.ToString() ?? message.SpanId,
        };

        await PersistMessageAsync(tracedMessage, ct).ConfigureAwait(false);

        await PublishAsync(tracedMessage.To, tracedMessage, ct).ConfigureAwait(false);
        await PublishAsync("*", tracedMessage, ct).ConfigureAwait(false);

        activity?.SetTag("messaging.delivered", true);
    }

    public async Task<SquadMessage> ReplyAsync(string originalMessageId, string fromSquad, string body, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(originalMessageId);
        ArgumentException.ThrowIfNullOrEmpty(fromSquad);
        ArgumentException.ThrowIfNullOrEmpty(body);

        var originalMessage = await GetMessageByIdAsync(originalMessageId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Message '{originalMessageId}' was not found.");

        var reply = new SquadMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            From = fromSquad,
            To = originalMessage.From,
            Subject = originalMessage.Subject,
            Body = body,
            CorrelationId = originalMessage.CorrelationId ?? originalMessage.Id,
            ReplyTo = originalMessage.Id,
            Timestamp = DateTime.UtcNow,
        };

        // Link this reply's trace to the original message's trace
        var links = new List<ActivityLink>();
        if (originalMessage.TraceId is not null && originalMessage.SpanId is not null
            && ActivityTraceId.CreateFromString(originalMessage.TraceId) is var traceId
            && ActivitySpanId.CreateFromString(originalMessage.SpanId) is var spanId)
        {
            links.Add(new ActivityLink(new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded)));
        }

        using var activity = ActivitySource.StartActivity(
            $"squad.message.reply {fromSquad} → {originalMessage.From}",
            ActivityKind.Producer,
            parentContext: default,
            links: links);

        activity?.SetTag("messaging.system", "squad-bus");
        activity?.SetTag("messaging.destination.name", originalMessage.From);
        activity?.SetTag("messaging.source.name", fromSquad);
        activity?.SetTag("messaging.message.id", reply.Id);
        activity?.SetTag("messaging.message.subject", reply.Subject);
        activity?.SetTag("messaging.reply_to", originalMessageId);
        activity?.SetTag("messaging.correlation_id", originalMessage.CorrelationId ?? originalMessage.Id);
        activity?.SetTag("messaging.parent_message.id", originalMessage.Id);
        activity?.SetTag("messaging.parent_message.from", originalMessage.From);
        activity?.SetTag("messaging.parent_message.to", originalMessage.To);
        activity?.SetTag("messaging.parent_trace.id", originalMessage.TraceId);
        activity?.SetTag("messaging.parent_span.id", originalMessage.SpanId);

        await SendAsync(reply, ct).ConfigureAwait(false);
        return reply;
    }

    public async Task<IReadOnlyList<SquadMessage>> GetInboxAsync(string squadName, bool unreadOnly = false, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(squadName);

        using var activity = ActivitySource.StartActivity(
            $"squad.inbox.read {squadName}",
            ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "squad-bus");
        activity?.SetTag("messaging.destination.name", squadName);
        activity?.SetTag("squad.name", squadName);
        activity?.SetTag("messaging.unread_only", unreadOnly);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, from_squad, to_squad, subject, body, correlation_id, reply_to, timestamp, is_read, trace_id, span_id
            FROM messages
            WHERE to_squad = @name
              AND (@unreadOnly = 0 OR is_read = 0)
            ORDER BY timestamp DESC;
            """;
        command.Parameters.AddWithValue("@name", squadName);
        command.Parameters.AddWithValue("@unreadOnly", unreadOnly ? 1 : 0);

        var messages = await ReadMessagesAsync(command, ct).ConfigureAwait(false);
        activity?.SetTag("messaging.message_count", messages.Count);
        return messages;
    }

    public async Task<IReadOnlyList<SquadMessage>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var activity = ActivitySource.StartActivity("squad.messages.recent", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "squad-bus");
        activity?.SetTag("messaging.limit", limit);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, from_squad, to_squad, subject, body, correlation_id, reply_to, timestamp, is_read, trace_id, span_id
            FROM messages
            ORDER BY timestamp DESC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@limit", limit);

        var messages = await ReadMessagesAsync(command, ct).ConfigureAwait(false);
        activity?.SetTag("messaging.message_count", messages.Count);
        return messages;
    }

    public async Task<IReadOnlyList<SquadMessage>> GetConversationAsync(string correlationId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(correlationId);

        using var activity = ActivitySource.StartActivity(
            $"squad.conversation.read",
            ActivityKind.Internal);
        activity?.SetTag("messaging.system", "squad-bus");
        activity?.SetTag("messaging.correlation_id", correlationId);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, from_squad, to_squad, subject, body, correlation_id, reply_to, timestamp, is_read, trace_id, span_id
            FROM messages
            WHERE correlation_id = @id
            ORDER BY timestamp;
            """;
        command.Parameters.AddWithValue("@id", correlationId);

        var messages = await ReadMessagesAsync(command, ct).ConfigureAwait(false);
        activity?.SetTag("messaging.message_count", messages.Count);
        return messages;
    }

    public async Task MarkReadAsync(string messageId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        using var activity = ActivitySource.StartActivity("squad.message.ack", ActivityKind.Internal);
        activity?.SetTag("messaging.system", "squad-bus");
        activity?.SetTag("messaging.message.id", messageId);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE messages SET is_read = 1 WHERE id = @id;";
        command.Parameters.AddWithValue("@id", messageId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var activity = ActivitySource.StartActivity("squad.messages.clear", ActivityKind.Internal);
        activity?.SetTag("messaging.system", "squad-bus");

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM messages;";
        var deletedCount = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        activity?.SetTag("messaging.message_count", deletedCount);
    }

    public async IAsyncEnumerable<SquadMessage> SubscribeAsync(string squadName, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(squadName);

        var channel = _channels.GetOrAdd(squadName, _ => Channel.CreateUnbounded<SquadMessage>());

        await foreach (var message in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            // Reconstruct the producer's trace context from the stamped message so the
            // consumer span nests inside the sender's trace tree (one continuous waterfall
            // from the originating producer through every downstream consumer).
            ActivityContext parentContext = default;
            if (message.TraceId is not null && message.SpanId is not null)
            {
                parentContext = new ActivityContext(
                    ActivityTraceId.CreateFromString(message.TraceId),
                    ActivitySpanId.CreateFromString(message.SpanId),
                    ActivityTraceFlags.Recorded);
            }

            using var activity = ActivitySource.StartActivity(
                $"squad.message.receive {message.From} → {squadName}",
                ActivityKind.Consumer,
                parentContext);
            activity?.SetTag("messaging.system", "squad-bus");
            activity?.SetTag("messaging.source.name", message.From);
            activity?.SetTag("messaging.destination.name", squadName);
            activity?.SetTag("messaging.message.id", message.Id);

            yield return message;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var channel in _channels.Values)
        {
            channel.Writer.TryComplete();
        }

        _channels.Clear();
    }

    private async Task PersistMessageAsync(SquadMessage message, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO messages (id, from_squad, to_squad, subject, body, correlation_id, reply_to, timestamp, is_read, trace_id, span_id)
            VALUES (@id, @from, @to, @subject, @body, @correlationId, @replyTo, @timestamp, @isRead, @traceId, @spanId);
            """;
        command.Parameters.AddWithValue("@id", message.Id);
        command.Parameters.AddWithValue("@from", message.From);
        command.Parameters.AddWithValue("@to", message.To);
        command.Parameters.AddWithValue("@subject", message.Subject);
        command.Parameters.AddWithValue("@body", message.Body);
        command.Parameters.AddWithValue("@correlationId", (object?)message.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@replyTo", (object?)message.ReplyTo ?? DBNull.Value);
        command.Parameters.AddWithValue("@timestamp", message.Timestamp.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("@isRead", message.IsRead ? 1 : 0);
        command.Parameters.AddWithValue("@traceId", (object?)message.TraceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@spanId", (object?)message.SpanId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task PublishAsync(string squadName, SquadMessage message, CancellationToken ct)
    {
        if (_channels.TryGetValue(squadName, out var channel))
        {
            await channel.Writer.WriteAsync(message, ct).ConfigureAwait(false);
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private async Task<SquadMessage?> GetMessageByIdAsync(string messageId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, from_squad, to_squad, subject, body, correlation_id, reply_to, timestamp, is_read, trace_id, span_id
            FROM messages
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", messageId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadMessage(reader) : null;
    }

    private static async Task<IReadOnlyList<SquadMessage>> ReadMessagesAsync(SqliteCommand command, CancellationToken ct)
    {
        var messages = new List<SquadMessage>();

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    private static SquadMessage ReadMessage(SqliteDataReader reader)
    {
        return new SquadMessage
        {
            Id = reader.GetString(0),
            From = reader.GetString(1),
            To = reader.GetString(2),
            Subject = reader.GetString(3),
            Body = reader.GetString(4),
            CorrelationId = reader.IsDBNull(5) ? null : reader.GetString(5),
            ReplyTo = reader.IsDBNull(6) ? null : reader.GetString(6),
            Timestamp = DateTime.Parse(
                reader.GetString(7),
                null,
                System.Globalization.DateTimeStyles.RoundtripKind),
            IsRead = reader.GetInt64(8) != 0,
            TraceId = reader.IsDBNull(9) ? null : reader.GetString(9),
            SpanId = reader.IsDBNull(10) ? null : reader.GetString(10),
        };
    }
}
