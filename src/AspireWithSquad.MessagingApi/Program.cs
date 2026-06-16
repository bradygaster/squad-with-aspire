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

foreach (var squadName in squadNames)
{
    var capturedName = squadName; // capture for closure
    builder.Services.AddKeyedSquadAgent(squadName, options =>
    {
        options.ConfigureSession = session =>
        {
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
            Console.WriteLine($"[{capturedName}] {traceEvent.Kind}: {traceEvent.SubagentName ?? ""} {traceEvent.RawEventType}");
        };
    });
}

// Squad coordinator routes user messages to all squads and dispatches to SquadAgents
builder.Services.AddHostedService<CoordinatorService>();

// OpenTelemetry: export traces to the Aspire dashboard via OTLP
var otel = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(SquadMessagingServiceExtensions.ActivitySourceName)
        .AddSource(SquadMessagingServiceExtensions.ConfigActivitySourceName)
        .AddSource("Squad.Coordinator")
        .AddSource(SquadAgentDiagnostics.ActivitySourceName));
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
app.MapGet("/api/squads", () => squadNames);

app.Run();
