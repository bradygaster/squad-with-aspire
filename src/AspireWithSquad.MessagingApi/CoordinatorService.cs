using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    private static readonly ActivitySource ActivitySource = SquadTelemetry.ActivitySource;

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

    // Turn budget: tracks how many turns each squad has used per dispatch cycle (keyed by correlationId:squadName)
    private readonly ConcurrentDictionary<string, int> _turnUsage = new();

    // Collected outputs per dispatch cycle for issue synthesis (keyed by correlationId)
    private readonly ConcurrentDictionary<string, ConcurrentBag<(string squad, string output)>> _cycleOutputs = new();

    /// <summary>Max turns any squad gets per dispatch cycle before being cut off.</summary>
    internal const int MaxTurnsPerSquad = 5;

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

            try
            {
                SquadTelemetry.MessagesReceived.Add(1, new TagList { { "source", "user" } });

                using var activity = ActivitySource.StartActivity(
                    $"user → coordinator",
                    ActivityKind.Server);
                activity?.SetTag("messaging.from", message.From);
                activity?.SetTag("messaging.to", "coordinator");
                activity?.SetTag("messaging.message.id", message.Id);
                activity?.SetTag("messaging.body.preview", Truncate(message.Body, 80));

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
                    // Await instead of fire-and-forget to observe exceptions
                    await DispatchToAgent(targetSquad, message, ct);

                    _logger.LogInformation("Coordinator routed @mention to {Squad}", targetSquad);
                }
                else
                {
                    activity?.SetTag("messaging.routing", "phased-broadcast");
                    activity?.SetTag("messaging.target", "all-squads");

                    // Phased dispatch — squads execute in pipeline order
                    await _bus.ReplyAsync(message.Id, "coordinator",
                        $"📨 Got your message! Starting phased dispatch (plan → design → build → verify → ship)...", ct);

                    await DispatchInPhases(message, activity, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coordinator failed processing user message {MessageId}: {Error}", message.Id, ex.Message);
                try { await _bus.ReplyAsync(message.Id, "coordinator", $"⚠️ Coordinator error: {ex.Message}", ct); }
                catch { /* best effort */ }
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
                SquadTelemetry.MessagesDropped.Add(1, SquadTelemetry.AgentTags(squadName));
                _logger.LogDebug("Dropping non-actionable message from {From} to {To}", message.From, squadName);
                continue;
            }

            try
            {
                SquadTelemetry.MessagesReceived.Add(1, new TagList { { "source", "inter-squad" } });

                // One reply per message received — prevents infinite side-conversation loops
                var responded = _respondedMessages.GetOrAdd(squadName, _ => new HashSet<string>());
                lock (responded)
                {
                    if (!responded.Add(message.Id))
                        continue;
                }

                using var activity = ActivitySource.StartActivity(
                    $"{message.From} → {squadName}",
                    ActivityKind.Consumer);
                activity?.SetTag("messaging.from", message.From);
                activity?.SetTag("messaging.to", squadName);
                activity?.SetTag("messaging.type", message.From == "user" ? "dm" : "inter-squad");
                activity?.SetTag("messaging.body.preview", Truncate(message.Body, 80));

                _logger.LogInformation("Direct message: {From} → {To}: {Preview}",
                    message.From, squadName, Truncate(message.Body, 80));

                // Dispatch to the target squad's agent
                await DispatchToAgent(squadName, message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Squad listener '{Squad}' failed on message {MessageId}: {Error}",
                    squadName, message.Id, ex.Message);
            }
        }
    }

    /// <summary>
    /// Dispatches a user message through squads in phased order.
    /// Phase 0 (plan) runs first and must complete before phase 1 (design), etc.
    /// Squads within the same phase run in parallel.
    /// </summary>
    private async Task DispatchInPhases(SquadMessage message, Activity? parentActivity, CancellationToken ct)
    {
        foreach (var phase in SquadRegistry.Phases)
        {
            var squadsInPhase = phase.Squads.Where(s => AllSquads.Contains(s)).ToArray();
            if (squadsInPhase.Length == 0)
                continue;

            parentActivity?.AddEvent(new ActivityEvent($"Phase {phase.Order} ({phase.Name}): {string.Join(", ", squadsInPhase)}"));
            _logger.LogInformation("Starting phase {Phase} ({Name}): {Squads}",
                phase.Order, phase.Name, string.Join(", ", squadsInPhase));

            await _bus.ReplyAsync(message.Id, "coordinator",
                $"⏳ Phase {phase.Order} ({phase.Name}): dispatching to {string.Join(", ", squadsInPhase)}...", ct);

            var phaseStart = Stopwatch.GetTimestamp();

            // Fan out within the phase and send messages
            var phaseTasks = new List<Task>();
            foreach (var squad in squadsInPhase)
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
                parentActivity?.AddEvent(new ActivityEvent($"coordinator → {squad}"));
                phaseTasks.Add(DispatchToAgent(squad, message, ct));
            }

            // Wait for all squads in this phase to finish before moving to the next
            await Task.WhenAll(phaseTasks);

            var phaseElapsed = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;
            SquadTelemetry.PhaseDuration.Record(phaseElapsed, SquadTelemetry.PhaseTags(phase.Order, phase.Name));
            SquadTelemetry.PhasesCompleted.Add(1, SquadTelemetry.PhaseTags(phase.Order, phase.Name));

            _logger.LogInformation("Phase {Phase} ({Name}) complete in {Elapsed:F0}ms", phase.Order, phase.Name, phaseElapsed);
            await _bus.ReplyAsync(message.Id, "coordinator",
                $"✅ Phase {phase.Order} ({phase.Name}) complete.", ct);
        }

        _logger.LogInformation("All phases complete for message {MessageId}", message.Id);

        // Issue synthesis epilogue: collect all outputs and file summary issues
        var correlationId = message.CorrelationId ?? message.Id;
        await SynthesizeIssues(correlationId, message, ct);

        // Clean up turn tracking for this cycle
        foreach (var squad in AllSquads)
            _turnUsage.TryRemove($"{correlationId}:{squad}", out _);
        _cycleOutputs.TryRemove(correlationId, out _);
    }

    /// <summary>
    /// After all phases complete, synthesizes squad outputs into actionable GitHub issues.
    /// The coordinator acts as project manager — it distills the discussion into work items.
    /// </summary>
    private async Task SynthesizeIssues(string correlationId, SquadMessage originalMessage, CancellationToken ct)
    {
        if (!_cycleOutputs.TryGetValue(correlationId, out var outputs) || outputs.IsEmpty)
            return;

        var targetRepo = await _config.GetAsync("target-repo", ct);
        if (string.IsNullOrWhiteSpace(targetRepo))
        {
            _logger.LogInformation("No target-repo configured — skipping issue synthesis");
            await _bus.ReplyAsync(originalMessage.Id, "coordinator",
                "📋 Cycle complete. Set a target repo (`target-repo` config) to enable automatic issue filing.", ct);
            return;
        }

        // Group outputs by squad and build a synthesis prompt
        var outputSummary = string.Join("\n\n", outputs.Select(o =>
            $"### {o.squad}\n{o.output[..Math.Min(2000, o.output.Length)]}"));

        var synthesisPrompt = $"""
            You are the project coordinator. The following squads just completed a work cycle for this user request:

            USER REQUEST: {originalMessage.Body}

            SQUAD OUTPUTS:
            {outputSummary}

            YOUR TASK: Create 1-5 focused GitHub issues that capture the ACTIONABLE work items from this cycle.
            Each issue should be specific enough for a developer to pick up and implement.

            For each issue, use this exact format (one per issue):
            <issue>
            title: [concise title]
            labels: [comma-separated labels like "enhancement", "bug", "design", "security", "testing"]
            body: [2-4 sentence description with acceptance criteria]
            </issue>

            Rules:
            - Only create issues for CONCRETE work items, not vague "consider doing X" suggestions
            - Assign labels from: enhancement, bug, design, security, testing, infrastructure, documentation
            - If squads already filed issues during their turns, don't duplicate them
            - Prefer fewer, well-scoped issues over many vague ones
            """;

        try
        {
            var agent = _services.GetKeyedService<SquadAgent>("ideation");
            if (agent is null)
            {
                _logger.LogWarning("No ideation agent available for issue synthesis");
                return;
            }

            using var activity = ActivitySource.StartActivity("issue-synthesis", ActivityKind.Internal);
            var response = await agent.RunAsync(synthesisPrompt, session: null, options: null, cancellationToken: ct);

            if (string.IsNullOrWhiteSpace(response.Text))
                return;

            // Parse <issue> blocks and file them
            var issueMatches = System.Text.RegularExpressions.Regex.Matches(
                response.Text,
                @"<issue>\s*title:\s*(.+?)\s*labels:\s*(.+?)\s*body:\s*(.+?)\s*</issue>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            var issueCount = 0;
            foreach (System.Text.RegularExpressions.Match match in issueMatches)
            {
                var title = match.Groups[1].Value.Trim();
                var labels = match.Groups[2].Value.Trim();
                var body = match.Groups[3].Value.Trim();

                // File via gh CLI
                var labelArgs = string.Join(",", labels.Split(',').Select(l => l.Trim()));
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "gh",
                        Arguments = $"issue create --repo {targetRepo} --title \"{title.Replace("\"", "\\\"")}\" --body \"{body.Replace("\"", "\\\"")}\" --label \"{labelArgs}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    }
                };

                process.Start();
                var ghOutput = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                if (process.ExitCode == 0)
                {
                    issueCount++;
                    _logger.LogInformation("Filed issue: {Title} → {Url}", title, ghOutput.Trim());
                }
                else
                {
                    var err = await process.StandardError.ReadToEndAsync(ct);
                    _logger.LogWarning("Failed to file issue '{Title}': {Error}", title, err);
                }
            }

            if (issueCount > 0)
            {
                await _bus.ReplyAsync(originalMessage.Id, "coordinator",
                    $"📋 Cycle complete — filed {issueCount} issue(s) in {targetRepo}.", ct);
            }
            else
            {
                await _bus.ReplyAsync(originalMessage.Id, "coordinator",
                    "📋 Cycle complete — squads produced outputs but no discrete issues were synthesized.", ct);
            }

            activity?.SetTag("issues.filed", issueCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Issue synthesis failed: {Error}", ex.Message);
            await _bus.ReplyAsync(originalMessage.Id, "coordinator",
                $"⚠️ Issue synthesis failed: {ex.Message}", ct);
        }
    }

    private async Task DispatchToAgent(string squadName, SquadMessage message, CancellationToken ct)
    {
        var correlationId = message.CorrelationId ?? message.Id;
        var budgetKey = $"{correlationId}:{squadName}";

        // Check turn budget — if exhausted, skip this squad
        var turnsUsed = _turnUsage.GetOrAdd(budgetKey, 0);
        if (turnsUsed >= MaxTurnsPerSquad)
        {
            _logger.LogInformation("Squad '{Squad}' hit turn budget ({Max} turns) for cycle {Correlation}",
                squadName, MaxTurnsPerSquad, correlationId);
            SquadTelemetry.MessagesDropped.Add(1, SquadTelemetry.AgentTags(squadName));
            return;
        }

        // Increment turn count
        _turnUsage.AddOrUpdate(budgetKey, 1, (_, current) => current + 1);
        var turnsRemaining = MaxTurnsPerSquad - (turnsUsed + 1);

        var tags = SquadTelemetry.AgentTags(squadName);
        SquadTelemetry.AgentInvocations.Add(1, tags);
        SquadTelemetry.AgentActive.Add(1, tags);
        var agentStart = Stopwatch.GetTimestamp();

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
            activity?.SetTag("messaging.body.preview", Truncate(message.Body, 80));

            var contextualPrompt = await BuildContextualPrompt(squadName, message, turnsRemaining, ct);

            _logger.LogInformation("Dispatching to SquadAgent '{Squad}' (ephemeral session)", squadName);

            using var mcpActivity = ActivitySource.StartActivity(
                $"{squadName} session",
                ActivityKind.Client);

            var response = await agent.RunAsync(contextualPrompt, session: null, options: null, cancellationToken: ct);

            var elapsed = Stopwatch.GetElapsedTime(agentStart).TotalMilliseconds;
            SquadTelemetry.AgentDuration.Record(elapsed, tags);
            mcpActivity?.SetTag("response.length", response.Text?.Length ?? 0);

            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                SquadTelemetry.AgentResponseLength.Record(response.Text.Length, tags);
                SquadTelemetry.MessagesSent.Add(1, tags);

                // Collect output for issue synthesis at end of cycle
                var outputs = _cycleOutputs.GetOrAdd(correlationId, _ => new());
                outputs.Add((squadName, response.Text));

                // Extract and persist any knowledge before sending the reply
                var (visibleReply, knowledge) = ExtractKnowledge(response.Text);

                if (!string.IsNullOrWhiteSpace(knowledge))
                {
                    await AppendKnowledge(squadName, knowledge, ct);
                    SquadTelemetry.KnowledgeExtractions.Add(1, tags);
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
            SquadTelemetry.AgentErrors.Add(1, tags);
            _logger.LogError(ex, "SquadAgent dispatch failed for '{Squad}': {Error}", squadName, ex.Message);
            await _bus.ReplyAsync(message.Id, squadName,
                $"⚠️ {squadName} error: {ex.Message}", ct);
        }
        finally
        {
            SquadTelemetry.AgentActive.Add(-1, tags);
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

    private async Task<string> BuildContextualPrompt(string squadName, SquadMessage message, int turnsRemaining, CancellationToken ct)
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

        var roleDescription = SquadRegistry.GetRoleDescription(squadName);
        var roleBlock = roleDescription is not null
            ? $"""
            YOUR ROLE: {roleDescription}

            """
            : "";

        var phaseInstruction = SquadRegistry.GetPhaseInstruction(squadName);
        var phaseBlock = phaseInstruction is not null
            ? $"""
            CURRENT PHASE INSTRUCTION: {phaseInstruction}

            """
            : "";

        var contextBlock = history.Count > 0
            ? $"""
            Recent chat history:
            ---
            {string.Join("\n", history.TakeLast(15))}
            ---

            New message from {message.From}: {message.Body}
            """
            : message.Body;

        var budgetBlock = turnsRemaining switch
        {
            0 => """
            ⚠️ FINAL TURN: This is your LAST action. You MUST produce a concrete deliverable NOW — file an issue, submit a PR, write a spec, or create a test. No planning, no "next steps". Ship something.

            """,
            1 => """
            ⏰ BUDGET: 1 turn remaining after this one. Wrap up — produce your final artifact on the next turn.

            """,
            _ => $"""
            BUDGET: You have {turnsRemaining + 1} turns remaining (of {MaxTurnsPerSquad} total). Each turn must produce forward progress. Don't deliberate — act.

            """
        };

        return $"""
            {repoInstruction}{knowledgeBlock}{roleBlock}{phaseBlock}{budgetBlock}Respond as {squadName}.

            {contextBlock}

            RULES:
            - You get ONE reply to this message. Make it count — be thoughtful and complete.
            - PRODUCE ARTIFACTS, not commentary. File issues, write code, create PRs, write tests. Don't just describe what should be done — do it.
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

    /// <summary>Null-safe string truncation to avoid NRE on message bodies.</summary>
    private static string Truncate(string? text, int maxLength) =>
        string.IsNullOrEmpty(text) ? "" : text[..Math.Min(maxLength, text.Length)];
}
