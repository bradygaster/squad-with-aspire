using Aspire.Hosting;
using BrutalGames.MessagingApi;
using GitHub.Copilot;
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
// Each agent gets the messaging bus MCP tools so they can talk to other squads and the user.
var extensionPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".github", "extensions", "squad-messaging", "extension.mjs"));

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
    builder.Services.AddKeyedSquadAgent(squadName, options =>
    {
        options.ConfigureSession = session =>
        {
            session.McpServers ??= new Dictionary<string, McpServerConfig>();
            session.McpServers.Add("squad-bus", new McpStdioServerConfig
            {
                Command = "node",
                Args = [extensionPath],
                Env = new Dictionary<string, string> { ["MESSAGING_API_URL"] = "http://localhost:5001" },
            });
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

app.Run();
