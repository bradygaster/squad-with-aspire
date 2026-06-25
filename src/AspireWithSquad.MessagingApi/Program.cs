using System.Diagnostics;
using System.Linq;
using Aspire.Hosting;
using AspireWithSquad.MessagingApi;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using Squad.Agents.AI;

// Allow gRPC over HTTP/2 without TLS for OTLP export (Aspire dev cert)
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Register the messaging bus (SQLite-backed)
var dbPath = Environment.GetEnvironmentVariable("SQUAD_MESSAGES_DB")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "squad-messages.db");
builder.Services.AddSquadMessaging(dbPath);

// Register real SquadAgent instances (one per squad) via keyed DI.
// Connection strings are injected by Aspire AppHost via WithReference().
// Each agent gets a native squad_send_message tool so they can DM other squads directly.

var squadNames = new[]
{
    "ideation-research-planning-squad",
    "experience-design-squad",
    "application-development-squad",
    "azure-infrastructure-squad",
    "quality-testing-squad",
    "security-hardening-squad",
    "review-deployment-squad",
};

builder.Services.AddSingleton(new SquadRegistry(squadNames));

foreach (var squadName in squadNames)
{
    var capturedName = squadName; // capture for closure
    builder.Services.AddKeyedSquadAgent(squadName, options =>
    {
        options.ConfigureSession = session =>
        {
            // Force the coordinator persona at .github/agents/squad.agent.md to load.
            // Without it the CLI uses the default Copilot prompt and the coordinator
            // never reliably dispatches to named subagents via the `task` tool.
            session.Agent = "squad";

            session.Tools ??= [];
            session.Tools.Add(CopilotTool.DefineTool(
                async (string to, string body, string? subject) =>
                {
                    using var client = new HttpClient();
                    var payload = new { from = capturedName, to, subject = subject ?? body[..Math.Min(50, body.Length)], body };
                    var response = await client.PostAsJsonAsync("http://localhost:5001/api/messages", payload);
                    if (!response.IsSuccessStatusCode)
                        return $"Failed to send: {response.StatusCode}";
                    return "Message sent successfully";
                },
                new CopilotToolOptions { SkipPermission = true },
                new AIFunctionFactoryOptions
                {
                    Name = "squad_send_message",
                    Description = "Send a DM to another squad or to the user. MUST be called to actually deliver a message — writing @squad-name in your response text does nothing. The 'to' parameter is the recipient squad name (e.g. 'experience-design-squad') or 'user'. The 'body' parameter is the message content.",
                }));
        };
        options.OnSubagentTrace = traceEvent =>
        {
            // The wrapper exposes a narrow surface — `SubagentName` is the `agent_type`
            // (almost always "general-purpose" because squad.agent.md line 814 instructs
            // `agent_type: "general-purpose"`), but the actual per-dispatch persona
            // identity ("🛡️ Cipher: Build the threat model") is supposed to live in
            // the `name` and `description` parameters the LLM passes to the `task`
            // tool. The Squad SDK rolls these into `RawEvent.Data`. We reflect over
            // every public string field on Data so we can both diagnose where the
            // persona info ended up AND surface it as OTel tags for the dashboard.
            var rawTags = new Dictionary<string, string>();
            try
            {
                var raw = traceEvent.RawEvent;
                var rawData = raw?.GetType().GetProperty("Data")?.GetValue(raw);
                if (rawData is not null)
                {
                    foreach (var p in rawData.GetType().GetProperties())
                    {
                        try
                        {
                            var v = p.GetValue(rawData);
                            if (v is string s && !string.IsNullOrEmpty(s))
                            {
                                rawTags[p.Name] = s;
                            }
                            else if (v is not null && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(decimal)))
                            {
                                rawTags[p.Name] = v.ToString() ?? string.Empty;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // Swallow reflection errors — telemetry must never break the host.
            }

            var rawTagSummary = rawTags.Count == 0
                ? ""
                : " // " + string.Join(" | ", rawTags.Select(kv =>
                {
                    var v = kv.Value;
                    if (v.Length > 80) v = v.Substring(0, 80) + "…";
                    return $"{kv.Key}={v}";
                }));
            Console.WriteLine($"[{capturedName}] {traceEvent.Kind}: {traceEvent.SubagentName ?? ""} {traceEvent.RawEventType}{rawTagSummary}");

            // The toolkit starts a child Activity per subagent event when
            // `EmitSubagentActivities = true`. Copy the raw event's string fields
            // onto the activity as `squad.subagent.raw.<PropertyName>` tags so the
            // Aspire trace viewer surfaces every per-dispatch detail.
            if (Activity.Current is { } activity)
            {
                activity.SetTag("squad.subagent.dispatcher", capturedName);
                foreach (var kv in rawTags)
                {
                    activity.SetTag($"squad.subagent.raw.{kv.Key}", kv.Value);
                }
            }
        };
        options.TraceEvents = true;
        options.EmitSubagentActivities = true;
    });
}

// Squad coordinator routes user messages to all squads and dispatches to SquadAgents.
// Register as a singleton AND as a hosted service so the reset endpoint below can
// resolve the same instance and clear its in-memory dictionaries.
builder.Services.AddSingleton<CoordinatorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CoordinatorService>());

// OpenTelemetry: export traces and metrics to the Aspire dashboard via OTLP
var otel = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(SquadMessagingServiceExtensions.ActivitySourceName)
        .AddSource(SquadMessagingServiceExtensions.ConfigActivitySourceName)
        .AddSource("Squad.Coordinator")
        .AddSource(SquadTelemetry.ServiceName)
        .AddSource(SquadAgentDiagnostics.ActivitySourceName))
    .WithMetrics(metrics => metrics
        .AddMeter(SquadTelemetry.MeterName));
otel.UseOtlpExporter();

// CORS for the chat UI
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseDeveloperExceptionPage();
app.UseCors();

// Map the inter-squad messaging endpoints
app.MapSquadMessagingApi();
app.MapSquadConfigApi();

// Expose the registered squad list so the UI doesn't hardcode names
app.MapGet("/api/squads", (SquadRegistry registry) => registry.Names);

// Reset everything: clears all messages, all config (target-repo + per-squad knowledge),
// and the coordinator's in-memory state (sessions, turn budgets, pending outputs).
// Called from the chat UI when the user wants to change the target repo.
app.MapPost("/api/state/reset", async (
    ISquadMessageBus bus,
    ISquadConfigStore config,
    CoordinatorService coordinator,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("StateReset");
    logger.LogInformation("State reset requested — clearing messages, config, and in-memory coordinator state");

    await bus.ClearAllAsync(ct);
    await config.ClearAllAsync(ct);
    coordinator.ResetState();

    return Results.NoContent();
});

app.Run();
