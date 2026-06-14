using System.ClientModel;
using Aspire.Hosting;
using BrutalGames.MessagingApi;
using Microsoft.Extensions.AI;
using OpenAI;

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

// OpenTelemetry: export Squad.Messaging and Squad.Config traces to the Aspire dashboard
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(SquadMessagingServiceExtensions.ActivitySourceName)
        .AddSource(SquadMessagingServiceExtensions.ConfigActivitySourceName)
        .AddSource("Squad.Coordinator")
        .AddSource("Squad.Responder"));

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
