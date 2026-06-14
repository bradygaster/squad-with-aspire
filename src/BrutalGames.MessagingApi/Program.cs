using System.ClientModel;
using Aspire.Hosting;
using BrutalGames.MessagingApi;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTelemetry;

// Allow gRPC over HTTP/2 without TLS for OTLP export (Aspire dev cert)
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Register the messaging bus (SQLite-backed)
var dbPath = Environment.GetEnvironmentVariable("SQUAD_MESSAGES_DB")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "squad-messages.db");
builder.Services.AddSquadMessaging(dbPath);

// GitHub Models as IChatClient for squad AI responses
var ghToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
builder.Services.AddSingleton<IChatClient>(
    new OpenAIClient(
        new ApiKeyCredential(ghToken),
        new OpenAIClientOptions { Endpoint = new Uri("https://models.inference.ai.azure.com") })
    .GetChatClient("gpt-4o-mini")
    .AsIChatClient());

// Squad coordinator routes user messages to all squads
builder.Services.AddHostedService<CoordinatorService>();
// LLM-powered responder for real-time chat replies
builder.Services.AddHostedService<SquadResponderService>();

// OpenTelemetry: export traces to the Aspire dashboard via OTLP
var otel = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(SquadMessagingServiceExtensions.ActivitySourceName)
        .AddSource(SquadMessagingServiceExtensions.ConfigActivitySourceName)
        .AddSource("Squad.Coordinator")
        .AddSource("Squad.Responder"));
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
