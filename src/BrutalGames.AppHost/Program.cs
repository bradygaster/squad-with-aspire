using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

// Messaging API service (HTTP endpoints for inter-squad communication)
var messagingApi = builder.AddProject<Projects.BrutalGames_MessagingApi>("messaging-api")
    .WithEnvironment("SQUAD_MESSAGES_DB", Path.Combine(repoRoot, "squad-messages.db"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", Environment.GetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL") ?? "https://localhost:21117")
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_SERVICE_NAME", "messaging-api");

// Chat UI (React + Vite) — proxies /api to messaging-api
builder.AddNpmApp("squad-chat", Path.Combine(repoRoot, "src", "squad-chat-ui"), "dev")
    .WithReference(messagingApi)
    .WaitFor(messagingApi)
    .WithHttpEndpoint(env: "PORT", port: 5173)
    .WithExternalHttpEndpoints();

// Each squad is its own siloed resource in the Aspire topology.
builder.AddSquad("research-and-ideation-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "research-and-ideation"));

builder.AddSquad("site-design-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "site-design"));

builder.AddSquad("game-development-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "game-development"));

builder.AddSquad("qa-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "qa"));

builder.Build().Run();
