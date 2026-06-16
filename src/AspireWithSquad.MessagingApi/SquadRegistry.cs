namespace AspireWithSquad.MessagingApi;

/// <summary>
/// Single source of truth for the registered squad names.
/// Registered as a singleton and consumed by CoordinatorService and the /api/squads endpoint.
/// </summary>
public sealed class SquadRegistry
{
    public string[] Names { get; }

    public SquadRegistry(string[] names)
    {
        Names = names;
    }

    /// <summary>
    /// Detects if the message body contains @squad-name and returns that squad,
    /// so the coordinator can route to just that squad instead of broadcasting.
    /// </summary>
    public string? DetectMentionedSquad(string body)
    {
        foreach (var squad in Names)
        {
            if (body.Contains($"@{squad}", StringComparison.OrdinalIgnoreCase))
                return squad;
        }
        return null;
    }

    /// <summary>
    /// Detects messages that are just acknowledgments/status updates and don't need a response.
    /// Prevents infinite "acknowledged - nothing actionable" ping-pong loops.
    /// </summary>
    public static bool IsNonActionable(string body)
    {
        var lower = body.ToLowerInvariant();
        string[] markers =
        [
            "nothing actionable",
            "no action needed",
            "nothing to act on",
            "nothing to do here",
            "acknowledged",
            "just a status",
            "just a mutual",
            "no messages to send",
            "already took my turn",
            "story has moved past",
            "moved past both of us",
            "my turn is done",
            "turn is long done"
        ];
        return markers.Any(m => lower.Contains(m));
    }
}
