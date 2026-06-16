using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

// --- TodoList Infrastructure Resources ---

var insights = builder.AddAzureApplicationInsights("app-insights");

var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsEmulator()
    .AddDatabase("tododb");

var todoApi = builder.AddProject<Projects.TodoList_Api>("todolist-api")
    .WithReference(cosmos)
    .WithReference(insights)
    .WaitFor(cosmos)
    .WithExternalHttpEndpoints();

// --- Squad Orchestration ---

// Each squad is its own siloed resource in the Aspire topology.
// Squad messaging and chat infrastructure below.
var ideationSquad = builder.AddSquad("ideation-research-planning-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "ideation-research-planning"));

var experienceSquad = builder.AddSquad("experience-design-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "experience-design"));

var appDevSquad = builder.AddSquad("application-development-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "application-development"));

var infraSquad = builder.AddSquad("azure-infrastructure-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "azure-infrastructure"));

var qualitySquad = builder.AddSquad("quality-testing-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "quality-testing"));

var securitySquad = builder.AddSquad("security-hardening-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "security-hardening"));

var reviewSquad = builder.AddSquad("review-deployment-squad",
    teamRoot: Path.Combine(repoRoot, "squads", "review-deployment"));

// Messaging API service (HTTP endpoints for inter-squad communication)
var messagingApi = builder.AddProject<Projects.AspireWithSquad_MessagingApi>("messaging-api")
    .WithHttpEndpoint(port: 5001)
    .WithEnvironment("SQUAD_MESSAGES_DB", Path.Combine(repoRoot, "squad-messages.db"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", Environment.GetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL") ?? "https://localhost:21117")
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_SERVICE_NAME", "messaging-api")
    .WithReference(ideationSquad)
    .WithReference(experienceSquad)
    .WithReference(appDevSquad)
    .WithReference(infraSquad)
    .WithReference(qualitySquad)
    .WithReference(securitySquad)
    .WithReference(reviewSquad);

// Chat UI (React + Vite) — proxies /api to messaging-api
builder.AddNpmApp("squad-chat", Path.Combine(repoRoot, "src", "squad-chat-ui"), "dev")
    .WithReference(messagingApi)
    .WaitFor(messagingApi)
    .WithHttpEndpoint(env: "PORT", port: 5173)
    .WithExternalHttpEndpoints();

builder.Build().Run();
