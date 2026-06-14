using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Squad.Agents.AI;

namespace BrutalGames.MessagingApi;

/// <summary>
/// The coordinator receives messages from the user, routes them to all squads,
/// and dispatches to real SquadAgent instances for AI-powered responses.
/// </summary>
public sealed class CoordinatorService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Squad.Coordinator", "1.0.0");

    private readonly ISquadMessageBus _bus;
    private readonly IServiceProvider _services;
    private readonly ILogger<CoordinatorService> _logger;

    private static readonly string[] AllSquads =
    [
        "research-and-ideation-squad",
        "site-design-squad",
        "game-development-squad",
        "qa-squad",
    ];

    public CoordinatorService(ISquadMessageBus bus, IServiceProvider services, ILogger<CoordinatorService> logger)
    {
        _bus = bus;
        _services = services;
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

                // Acknowledge to the user
                await _bus.ReplyAsync(message.Id, "coordinator",
                    $"📨 Got your message! Dispatching to all squads...", stoppingToken);

                // Broadcast to all squads AND dispatch to real SquadAgent instances
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

                    // Dispatch to the real SquadAgent (fire and forget — responses come back on the bus)
                    _ = DispatchToAgent(squad, message, stoppingToken);
                }

                _logger.LogInformation("Coordinator dispatched message to {Count} squads", AllSquads.Length);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Coordinator service shutting down");
        }
    }

    private async Task DispatchToAgent(string squadName, SquadMessage message, CancellationToken ct)
    {
        try
        {
            var agent = _services.GetKeyedService<SquadAgent>(squadName);
            if (agent is null)
            {
                _logger.LogWarning("No SquadAgent registered for '{Squad}'", squadName);
                return;
            }

            using var activity = ActivitySource.StartActivity(
                $"squad.agent.dispatch {squadName}",
                ActivityKind.Internal);
            activity?.SetTag("squad.name", squadName);
            activity?.SetTag("messaging.message.body", message.Body);

            _logger.LogInformation("Dispatching to SquadAgent '{Squad}': {Body}", squadName, message.Body);

            var session = await agent.CreateSessionAsync(ct);
            var response = await agent.RunAsync(message.Body, session, cancellationToken: ct);

            activity?.SetTag("squad.response.length", response.Text?.Length ?? 0);

            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                await _bus.ReplyAsync(message.Id, squadName, response.Text, ct);
                _logger.LogInformation("Squad '{Squad}' replied: {Preview}",
                    squadName, response.Text[..Math.Min(100, response.Text.Length)]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SquadAgent dispatch failed for '{Squad}'", squadName);
            await _bus.ReplyAsync(message.Id, squadName,
                $"⚠️ {squadName} encountered an error processing your request.", ct);
        }
    }
}
