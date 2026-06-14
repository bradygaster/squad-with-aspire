namespace Aspire.Hosting.ApplicationModel;

public sealed record SquadMessage
{
    public required string Id { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public string? CorrelationId { get; init; }
    public string? ReplyTo { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsRead { get; init; }

    // OpenTelemetry trace propagation
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
}
