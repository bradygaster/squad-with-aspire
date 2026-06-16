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

            var contextualPrompt = await BuildContextualPrompt(squadName, message, ct);

            _logger.LogInformation("Dispatching to SquadAgent '{Squad}' (ephemeral session)", squadName);

            using var mcpActivity = ActivitySource.StartActivity(
                $"{squadName} session",
                ActivityKind.Client);

            var response = await agent.RunAsync(contextualPrompt, session: null, options: null, cancellationToken: ct);

            mcpActivity?.SetTag("response.length", response.Text?.Length ?? 0);

            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                // Extract and persist any knowledge before sending the reply
                var (visibleReply, knowledge) = ExtractKnowledge(response.Text);

                if (!string.IsNullOrWhiteSpace(knowledge))
                {
                    await AppendKnowledge(squadName, knowledge, ct);
                    activity?.AddEvent(new ActivityEvent($"{squadName} learned"));
                }

                if (!string.IsNullOrWhiteSpace(visibleReply))
                {
                    await _bus.ReplyAsync(message.Id, squadName, visibleReply.Trim(), ct);
                    activity?.AddEvent(new ActivityEvent($"{squadName} → {message.From} (reply)"));
                    _logger.LogInformation("Squad '{Squad}' replied: {Preview}",
                        squadName, visibleReply[..Math.Min(100, visibleReply.Length)]);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SquadAgent dispatch failed for '{Squad}'", squadName);
            await _bus.ReplyAsync(message.Id, squadName,
                $"⚠️ {squadName} encountered an error processing your request.", ct);
        }
    }

    /// <summary>
    /// Extracts <knowledge>...</knowledge> blocks from the agent response.
    /// Returns the visible reply (without the block) and the knowledge content.
    /// </summary>
    internal static (string visibleReply, string? knowledge) ExtractKnowledge(string responseText)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            responseText,
            @"<knowledge>(.*?)</knowledge>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (!match.Success)
            return (responseText, null);

        var knowledge = match.Groups[1].Value.Trim();
        var visible = responseText[..match.Index] + responseText[(match.Index + match.Length)..];
        return (visible.Trim(), knowledge);
    }

    /// <summary>
    /// Appends new learnings to the squad's persistent knowledge store.
    /// Each entry is timestamped so the squad can see when it learned what.
    /// </summary>
    private async Task AppendKnowledge(string squadName, string newKnowledge, CancellationToken ct)
    {
        var key = $"knowledge:{squadName}";
        var existing = await _config.GetAsync(key, ct) ?? "";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var updated = string.IsNullOrWhiteSpace(existing)
            ? $"[{timestamp}] {newKnowledge}"
            : $"{existing}\n[{timestamp}] {newKnowledge}";

        // Cap at ~16000 chars to keep prompts reasonable — trim oldest entries
        if (updated.Length > 16000)
        {
            var lines = updated.Split('\n');
            while (updated.Length > 14000 && lines.Length > 1)
            {
                lines = lines[1..];
                updated = string.Join('\n', lines);
            }
        }

        await _config.SetAsync(key, updated, ct);
        _logger.LogInformation("Knowledge appended for '{Squad}' ({Length} chars total)", squadName, updated.Length);
    }

    private async Task<string> BuildContextualPrompt(string squadName, SquadMessage message, CancellationToken ct)
    {
        var targetRepo = await _config.GetAsync("target-repo", ct);
        var knowledge = await _config.GetAsync($"knowledge:{squadName}", ct);

        var recentMessages = await _bus.GetRecentAsync(20, ct);
        var history = recentMessages
            .Where(m => m.Id != message.Id)
            .Select(m => $"[{m.From} → {m.To}]: {m.Body}")
            .ToList();

        var repoInstruction = string.IsNullOrWhiteSpace(targetRepo)
            ? ""
            : $"""
            IMPORTANT: You are working on GitHub repository "{targetRepo}". ALL GitHub operations (creating issues, pull requests, code changes, reading files) MUST target this repo. Do NOT create issues or PRs in any other repository. When using gh CLI, always pass --repo {targetRepo}.

            """;

        var knowledgeBlock = string.IsNullOrWhiteSpace(knowledge)
            ? ""
            : $"""
            YOUR ACCUMULATED KNOWLEDGE (from prior conversations):
            ---
            {knowledge}
            ---

            """;

        var contextBlock = history.Count > 0
            ? $"""
            Recent chat history:
            ---
            {string.Join("\n", history.TakeLast(15))}
            ---

            New message from {message.From}: {message.Body}
            """
            : message.Body;

        return $"""
            {repoInstruction}{knowledgeBlock}Respond as {squadName}.

            {contextBlock}

            RULES:
            - You get ONE reply to this message. Make it count — be thoughtful and complete.
            - If a message requires NO action from you (status updates, acknowledgments, "nothing to do"), DO NOT REPLY. Silence is correct.
            - To send a message to another squad, CALL the squad_send_message tool. Writing "@squad" in text does nothing.
            - Do NOT send acknowledgment messages like "got it", "nothing actionable", "my turn is done". Just stay silent.
            - Only reply if you have substantive content to add or a task to hand off.
            - After your reply, if you learned anything new (decisions, context, technical details, project state), append a <knowledge> block with a brief summary of what you learned. Example:
              <knowledge>User wants a todo app with ASP.NET Core. Team agreed on Cosmos DB. Security squad owns auth.</knowledge>
              If nothing new was learned, omit the block entirely.

            Available squads: {string.Join(", ", AllSquads)}
            """;
    }
}
