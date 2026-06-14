using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.AI;

namespace BrutalGames.MessagingApi;

/// <summary>
/// Background service that subscribes to each squad's inbox and generates AI-powered replies
/// using GitHub Models. For real-time chat; Copilot sessions handle deeper work tasks.
/// </summary>
public sealed class SquadResponderService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Squad.Responder", "1.0.0");

    private readonly ISquadMessageBus _bus;
    private readonly IChatClient _chatClient;
    private readonly ILogger<SquadResponderService> _logger;

    private static readonly Dictionary<string, string> SquadPrompts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["research-and-ideation-squad"] = """
            You are the Research & Ideation squad. Your team researches game concepts,
            explores market trends, and brainstorms creative directions. Team members:
            game-researcher, creative-director, game-curator, vibe-checker.
            Respond in character. Be creative and enthusiastic. Keep it to 2-3 sentences.
            Start with a relevant emoji.
            """,

        ["site-design-squad"] = """
            You are the Site Design squad. Your team handles frontend architecture,
            theme development, layout design, and user experience. Team members:
            lead-designer, frontend-architect, theme-developer, layout-specialist.
            Respond in character. Be aesthetic-minded and detail-oriented. Keep it to 2-3 sentences.
            Start with a relevant emoji.
            """,

        ["game-development-squad"] = """
            You are the Game Development squad. Your team builds game mechanics,
            UI systems, rendering pipelines, and core gameplay loops. Team members:
            lead, mechanic, ui, renderer.
            Respond in character. Be technical and pragmatic. Keep it to 2-3 sentences.
            Start with a relevant emoji.
            """,

        ["qa-squad"] = """
            You are the QA squad. Your team runs test automation, gameplay testing,
            visual review, and ensures quality standards. Team members:
            test-lead, automation-engineer, gameplay-tester, visual-reviewer.
            Respond in character. Be thorough and quality-focused. Keep it to 2-3 sentences.
            Start with a relevant emoji.
            """,
    };

    public SquadResponderService(ISquadMessageBus bus, IChatClient chatClient, ILogger<SquadResponderService> logger)
    {
        _bus = bus;
        _chatClient = chatClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(500, stoppingToken);
        _logger.LogInformation("Squad responder (LLM) starting — monitoring {Count} squads", SquadPrompts.Count);

        var tasks = SquadPrompts.Keys.Select(squad => SubscribeAndRespond(squad, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task SubscribeAndRespond(string squadName, CancellationToken ct)
    {
        _logger.LogInformation("Squad '{Squad}' LLM responder listening", squadName);

        try
        {
            await foreach (var message in _bus.SubscribeAsync(squadName, ct))
            {
                // Don't respond to messages from squads (avoid loops)
                if (SquadPrompts.ContainsKey(message.From))
                    continue;

                using var activity = ActivitySource.StartActivity(
                    $"squad.responder.llm {squadName}",
                    ActivityKind.Consumer);
                activity?.SetTag("messaging.squad", squadName);
                activity?.SetTag("messaging.message.id", message.Id);
                activity?.SetTag("messaging.from", message.From);

                _logger.LogInformation("Squad '{Squad}' generating AI response for: {Body}",
                    squadName, message.Body);

                var response = await GenerateResponse(squadName, message, ct);
                activity?.SetTag("messaging.response.length", response.Length);

                await _bus.ReplyAsync(message.Id, squadName, response, ct);
                _logger.LogInformation("Squad '{Squad}' replied via LLM", squadName);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Squad '{Squad}' responder shutting down", squadName);
        }
    }

    private async Task<string> GenerateResponse(string squadName, SquadMessage message, CancellationToken ct)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SquadPrompts[squadName]),
                new(ChatRole.User, message.Body),
            };

            var result = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            return result.Text ?? $"🤔 Processing...";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM call failed for '{Squad}', using fallback", squadName);
            return $"💬 Got your message! We're working on it.";
        }
    }
}
