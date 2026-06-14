using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace BrutalGames.MessagingApi;

/// <summary>
/// Background service that subscribes to each squad's inbox and generates replies.
/// Each squad has a personality and role that shapes its responses.
/// </summary>
public sealed class SquadResponderService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Squad.Responder", "1.0.0");

    private readonly ISquadMessageBus _bus;
    private readonly ILogger<SquadResponderService> _logger;

    private static readonly Dictionary<string, SquadPersonality> Squads = new(StringComparer.OrdinalIgnoreCase)
    {
        ["research-and-ideation-squad"] = new(
            "Research & Ideation",
            "We research game concepts, explore market trends, and brainstorm creative directions.",
            ["game-researcher", "creative-director", "game-curator", "vibe-checker"]),

        ["site-design-squad"] = new(
            "Site Design",
            "We handle frontend architecture, theme development, layout design, and user experience.",
            ["lead-designer", "frontend-architect", "theme-developer", "layout-specialist"]),

        ["game-development-squad"] = new(
            "Game Development",
            "We build game mechanics, UI systems, rendering pipelines, and core gameplay loops.",
            ["lead", "mechanic", "ui", "renderer"]),

        ["qa-squad"] = new(
            "QA",
            "We run test automation, gameplay testing, visual review, and ensure quality standards.",
            ["test-lead", "automation-engineer", "gameplay-tester", "visual-reviewer"]),
    };

    public SquadResponderService(ISquadMessageBus bus, ILogger<SquadResponderService> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the system a moment to start up
        await Task.Delay(500, stoppingToken);

        _logger.LogInformation("Squad responder service starting — monitoring {Count} squads", Squads.Count);

        // Launch a subscriber task for each squad
        var tasks = Squads.Keys.Select(squad => SubscribeAndRespond(squad, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task SubscribeAndRespond(string squadName, CancellationToken ct)
    {
        _logger.LogInformation("Squad '{Squad}' responder is now listening for messages", squadName);

        try
        {
            await foreach (var message in _bus.SubscribeAsync(squadName, ct))
            {
                // Don't respond to our own messages
                if (Squads.ContainsKey(message.From))
                    continue;

                using var activity = ActivitySource.StartActivity(
                    $"squad.responder.process {squadName}",
                    ActivityKind.Consumer);
                activity?.SetTag("messaging.squad", squadName);
                activity?.SetTag("messaging.message.id", message.Id);
                activity?.SetTag("messaging.from", message.From);

                _logger.LogInformation(
                    "Squad '{Squad}' received message from '{From}': {Subject}",
                    squadName, message.From, message.Subject);

                // Simulate thinking time
                await Task.Delay(Random.Shared.Next(800, 2000), ct);

                var personality = Squads[squadName];
                var response = GenerateResponse(personality, message);

                activity?.SetTag("messaging.response.length", response.Length);

                await _bus.ReplyAsync(message.Id, squadName, response, ct);

                _logger.LogInformation("Squad '{Squad}' replied to '{From}'", squadName, message.From);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Squad '{Squad}' responder shutting down", squadName);
        }
    }

    private static string GenerateResponse(SquadPersonality personality, SquadMessage message)
    {
        var body = message.Body.ToLowerInvariant();

        // Introductions
        if (body.Contains("introduce") || body.Contains("who are you") || body.Contains("hello") || body.Contains("hey"))
        {
            var members = string.Join(", ", personality.Members);
            return $"👋 Hey! We're the {personality.DisplayName} squad. {personality.Description} " +
                   $"Our team members are: {members}. Ready to get to work!";
        }

        // Status/progress questions
        if (body.Contains("status") || body.Contains("progress") || body.Contains("update"))
        {
            return $"📊 {personality.DisplayName} squad reporting in. We're standing by and ready for tasks. " +
                   $"Assign us work via GitHub issues and we'll get on it.";
        }

        // Work requests
        if (body.Contains("start") || body.Contains("build") || body.Contains("create") || body.Contains("implement"))
        {
            return $"🚀 Got it! The {personality.DisplayName} squad is on it. " +
                   $"We'll create GitHub issues to track our work and keep you posted on progress. " +
                   $"{personality.Description}";
        }

        // Questions about capabilities
        if (body.Contains("can you") || body.Contains("what do you") || body.Contains("capable"))
        {
            return $"💡 {personality.Description} " +
                   $"Our specialists ({string.Join(", ", personality.Members)}) are ready to collaborate. " +
                   $"Just tell us what you need!";
        }

        // Acknowledge anything else
        return $"✅ Acknowledged. The {personality.DisplayName} squad received your message. " +
               $"{personality.Description} Let us know how we can help!";
    }

    private sealed record SquadPersonality(string DisplayName, string Description, string[] Members);
}
