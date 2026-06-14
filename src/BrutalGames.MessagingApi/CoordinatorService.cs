using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace BrutalGames.MessagingApi;

/// <summary>
/// The coordinator receives messages from the user and routes them to all squads.
/// It acts as the central dispatch, broadcasting user requests to the team.
/// </summary>
public sealed class CoordinatorService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Squad.Coordinator", "1.0.0");

    private readonly ISquadMessageBus _bus;
    private readonly ILogger<CoordinatorService> _logger;

    private static readonly string[] AllSquads =
    [
        "research-and-ideation-squad",
        "site-design-squad",
        "game-development-squad",
        "qa-squad",
    ];

    public CoordinatorService(ISquadMessageBus bus, ILogger<CoordinatorService> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(300, stoppingToken);

        _logger.LogInformation("Coordinator service starting — will route user messages to squads");

        try
        {
            await foreach (var message in _bus.SubscribeAsync("coordinator", stoppingToken))
            {
                // Only process messages from users, not from squads
                if (message.From != "user")
                    continue;

                using var activity = ActivitySource.StartActivity(
                    "squad.coordinator.route",
                    ActivityKind.Internal);
                activity?.SetTag("messaging.message.id", message.Id);
                activity?.SetTag("messaging.from", message.From);

                _logger.LogInformation("Coordinator routing message from user: {Subject}", message.Subject);

                // First, acknowledge to the user
                await _bus.ReplyAsync(message.Id, "coordinator",
                    $"📨 Got your message! Routing to all squads now...", stoppingToken);

                // Broadcast to all squads
                foreach (var squad in AllSquads)
                {
                    var fanout = new SquadMessage
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        From = "coordinator",
                        To = squad,
                        Subject = message.Subject,
                        Body = message.Body,
                        CorrelationId = message.CorrelationId ?? message.Id,
                    };

                    await _bus.SendAsync(fanout, stoppingToken);
                    activity?.AddEvent(new ActivityEvent($"Routed to {squad}"));
                }

                _logger.LogInformation("Coordinator routed message to {Count} squads", AllSquads.Length);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Coordinator service shutting down");
        }
    }
}
