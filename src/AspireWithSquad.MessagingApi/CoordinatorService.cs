using System.Collections.Concurrent;
using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Squad.Agents.AI;

namespace AspireWithSquad.MessagingApi;

/// <summary>
/// The coordinator receives messages from the user, routes them to all squads,
/// and dispatches to real SquadAgent instances for AI-powered responses.
/// Each squad maintains a persistent session with full chat history context.
/// </summary>
public sealed class CoordinatorService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Squad.Coordinator", "1.0.0");

    private readonly ISquadMessageBus _bus;
    private readonly ISquadConfigStore _config;
    private readonly IServiceProvider _services;
    private readonly SquadRegistry _registry;
    private readonly ILogger<CoordinatorService> _logger;

    // Persistent sessions — one per squad. RunAsync handles session persistence internally
    // when the session parameter is null.
    private readonly ConcurrentDictionary<string, AgentSession?> _sessions = new();

    // Track which messages each squad has already responded to (one reply per message received)
    private readonly ConcurrentDictionary<string, HashSet<string>> _respondedMessages = new();

    private string[] AllSquads => _registry.Names;

    public CoordinatorService(ISquadMessageBus bus, ISquadConfigStore config, IServiceProvider services, SquadRegistry registry, ILogger<CoordinatorService> logger)
    {
        _bus = bus;
        _config = config;
        _services = services;
            _registry = registry;
            _logger = logger;
        }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(300, stoppingToken);

        _logger.LogInformation("Coordinator service starting — will route user messages and inter-squad messages");

        try
        {
            // Start the user→coordinator listener
            var userTask = ListenForUserMessages(stoppingToken);

            // Start inter-squad listeners (one per squad)
            var squadTasks = AllSquads.Select(squad => ListenForSquadMessages(squad, stoppingToken)).ToArray();

            // Wait for all (they run until cancellation)
            await Task.WhenAll([userTask, .. squadTasks]);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Coordinator service shutting down");
        }
    }

    /// <summary>
    /// Listens for user messages addressed to "coordinator" and fans out to all squads.
    /// </summary>
    private async Task ListenForUserMessages(CancellationToken ct)
    {
        await foreach (var message in _bus.SubscribeAsync("coordinator", ct))
        {
            if (message.From != "user")
                continue;

            using var activity = ActivitySource.StartActivity(
                $"user → coordinator",
                ActivityKind.Server);
            activity?.SetTag("messaging.from", message.From);
            activity?.SetTag("messaging.to", "coordinator");
            activity?.SetTag("messaging.message.id", message.Id);
            activity?.SetTag("messaging.body.preview", message.Body[..Math.Min(80, message.Body.Length)]);

            _logger.LogInformation("Coordinator routing message from user: {Subject}", message.Subject);

            // Check if this is an @mention targeting a specific squad
            var targetSquad = _registry.DetectMentionedSquad(message.Body);

            if (targetSquad is not null)
            {
                activity?.SetTag("messaging.routing", "direct");
                activity?.SetTag("messaging.target", targetSquad);

                // DM to a specific squad
                await _bus.ReplyAsync(message.Id, "coordinator",
                    $"📨 Got your message! Routing to {targetSquad}...", ct);

                var fanout = new SquadMessage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    From = "coordinator",
                    To = targetSquad,
                    Subject = message.Subject,
                    Body = message.Body,
                    CorrelationId = message.CorrelationId ?? message.Id,
                };

                await _bus.SendAsync(fanout, ct);
                activity?.AddEvent(new ActivityEvent($"coordinator → {targetSquad}"));
                _ = DispatchToAgent(targetSquad, message, ct);

                _logger.LogInformation("Coordinator routed @mention to {Squad}", targetSquad);
            }
            else
            {
                activity?.SetTag("messaging.routing", "broadcast");
                activity?.SetTag("messaging.target", "all-squads");

                // Broadcast to all squads
                await _bus.ReplyAsync(message.Id, "coordinator",
                    $"📨 Got your message! Dispatching to all squads...", ct);

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

                    await _bus.SendAsync(fanout, ct);
                    activity?.AddEvent(new ActivityEvent($"coordinator → {squad}"));
                    _ = DispatchToAgent(squad, message, ct);
                }

                _logger.LogInformation("Coordinator dispatched message to {Count} squads", AllSquads.Length);
            }
        }
    }

    /// <summary>
    /// Listens for messages addressed to a specific squad (from other squads or direct from user)
    /// and dispatches them to the target SquadAgent.
    /// </summary>
    private async Task ListenForSquadMessages(string squadName, CancellationToken ct)
    {
        await foreach (var message in _bus.SubscribeAsync(squadName, ct))
        {
            // Skip coordinator fan-out messages (already dispatched by ListenForUserMessages)
            if (message.From == "coordinator")
                continue;

            // Drop non-actionable messages — prevents infinite ack loops
            if (SquadRegistry.IsNonActionable(message.Body))
            {
                _logger.LogDebug("Dropping non-actionable message from {From} to {To}", message.From, squadName);
                continue;
            }

            // One reply per message received — prevents infinite side-conversation loops
            var responded = _respondedMessages.GetOrAdd(squadName, _ => new HashSet<string>());
            lock (responded)
            {
                if (!responded.Add(message.Id))
                {
                    // Already responded to this exact message
                    continue;
                }
            }

            using var activity = ActivitySource.StartActivity(
                $"{message.From} → {squadName}",
                ActivityKind.Consumer);
            activity?.SetTag("messaging.from", message.From);
            activity?.SetTag("messaging.to", squadName);
            activity?.SetTag("messaging.type", message.From == "user" ? "dm" : "inter-squad");
            activity?.SetTag("messaging.body.preview", message.Body[..Math.Min(80, message.Body.Length)]);

            _logger.LogInformation("Direct message: {From} → {To}: {Preview}",
                message.From, squadName, message.Body[..Math.Min(80, message.Body.Length)]);

            // Dispatch to the target squad's agent
            _ = DispatchToAgent(squadName, message, ct);
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
                $"coordinator → {squadName} (agent)",
                ActivityKind.Producer);
            activity?.SetTag("squad.name", squadName);
            activity?.SetTag("messaging.from", message.From);
            activity?.SetTag("messaging.to", squadName);
            activity?.SetTag("messaging.body.preview", message.Body[..Math.Min(80, message.Body.Length)]);

            // Get or create a persistent session for this squad
            var session = await GetOrCreateSessionAsync(agent, squadName, ct);

            // Build prompt with recent chat history for context
            var contextualPrompt = await BuildContextualPrompt(squadName, message, ct);

            _logger.LogInformation("Dispatching to SquadAgent '{Squad}' with history context", squadName);

            // MCP tool calls happen inside RunAsync — wrap in a child span
            using var mcpActivity = ActivitySource.StartActivity(
                $"{squadName} MCP session",
                ActivityKind.Client);
            mcpActivity?.SetTag("mcp.server", "squad-bus");
            mcpActivity?.SetTag("mcp.tools", "squad_send_message, squad_read_recent_messages, squad_read_inbox");

            var response = await agent.RunAsync(contextualPrompt, session, options: null, cancellationToken: ct);

            mcpActivity?.SetTag("mcp.response.length", response.Text?.Length ?? 0);
            activity?.SetTag("squad.response.length", response.Text?.Length ?? 0);

            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                await _bus.ReplyAsync(message.Id, squadName, response.Text, ct);
                activity?.AddEvent(new ActivityEvent($"{squadName} → {message.From} (reply)"));
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

    private Task<AgentSession?> GetOrCreateSessionAsync(SquadAgent agent, string squadName, CancellationToken ct)
    {
        // SquadAgent manages its own session lifecycle internally.
        // We pass null to let it handle persistent session state.
        return Task.FromResult<AgentSession?>(null);
    }

    private async Task<string> BuildContextualPrompt(string squadName, SquadMessage message, CancellationToken ct)
    {
        // Fetch the target repo so agents operate on the right repository
        var targetRepo = await _config.GetAsync("target-repo", ct);

        // Pull recent chat history from the bus for context
        var recentMessages = await _bus.GetRecentAsync(20, ct);

        var history = recentMessages
            .Where(m => m.Id != message.Id) // exclude the current message
            .Select(m => $"[{m.From} → {m.To}]: {m.Body}")
            .ToList();

        var repoInstruction = string.IsNullOrWhiteSpace(targetRepo)
            ? ""
            : $"""
            IMPORTANT: You are working on GitHub repository "{targetRepo}". ALL GitHub operations (creating issues, pull requests, code changes, reading files) MUST target this repo. Do NOT create issues or PRs in any other repository. When using gh CLI, always pass --repo {targetRepo}.

            """;

        if (history.Count == 0)
            return $"""
            {repoInstruction}Respond as {squadName}.

            RULES:
            - You get ONE reply to this message. Make it count — be thoughtful and complete.
            - If a message requires NO action from you (status updates, acknowledgments, "nothing to do"), DO NOT REPLY. Silence is correct.
            - To send a message to another squad, CALL the squad_send_message tool. Writing "@squad" in text does nothing.
            - Do NOT send acknowledgment messages like "got it", "nothing actionable", "my turn is done". Just stay silent.
            - Only reply if you have substantive content to add or a task to hand off.

            Available squads: {string.Join(", ", AllSquads)}

            {message.Body}
            """;

        var contextBlock = string.Join("\n", history.TakeLast(15));

        return $"""
            {repoInstruction}Here is the recent chat history for context:
            ---
            {contextBlock}
            ---

            New message from {message.From}: {message.Body}

            Respond as {squadName}.

            RULES:
            - You get ONE reply to this message. Make it count — be thoughtful and complete.
            - If a message requires NO action from you (status updates, acknowledgments, "nothing to do"), DO NOT REPLY. Silence is correct.
            - To send a message to another squad, CALL the squad_send_message tool. Writing "@squad" in text does nothing.
            - Do NOT send acknowledgment messages like "got it", "nothing actionable", "my turn is done". Just stay silent.
            - Only reply if you have substantive content to add or a task to hand off.

            Available squads: {string.Join(", ", AllSquads)}
            """;
    }
}
