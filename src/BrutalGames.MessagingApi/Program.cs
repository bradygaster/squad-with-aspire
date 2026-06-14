using Aspire.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register the messaging bus (SQLite-backed)
var dbPath = Environment.GetEnvironmentVariable("SQUAD_MESSAGES_DB")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "squad-messages.db");
builder.Services.AddSquadMessaging(dbPath);

// OpenTelemetry: export Squad.Messaging and Squad.Config traces to the Aspire dashboard
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(SquadMessagingServiceExtensions.ActivitySourceName)
        .AddSource(SquadMessagingServiceExtensions.ConfigActivitySourceName));

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
