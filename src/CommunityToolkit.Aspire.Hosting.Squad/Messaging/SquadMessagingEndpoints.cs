using Aspire.Hosting.ApplicationModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aspire.Hosting;

internal sealed record SendMessageRequest(string From, string To, string Subject, string Body);

internal sealed record ReplyMessageRequest(string From, string Body);

internal sealed record ConfigValueRequest(string Value);

public static class SquadMessagingEndpoints
{
    public static IEndpointRouteBuilder MapSquadMessagingApi(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/messages");

        // Configure CORS for this group at the app level as needed.
        group.MapPost("/", async (SendMessageRequest req, ISquadMessageBus bus, CancellationToken ct) =>
        {
            var message = new SquadMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                From = req.From,
                To = req.To,
                Subject = req.Subject,
                Body = req.Body,
                CorrelationId = Guid.NewGuid().ToString("N"),
            };

            await bus.SendAsync(message, ct);

            return Results.Created($"/api/messages/{message.Id}", message);
        });

        group.MapDelete("/", async (ISquadMessageBus bus, CancellationToken ct) =>
        {
            await bus.ClearAllAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/{squadName}/inbox", async (string squadName, bool? unreadOnly, ISquadMessageBus bus, CancellationToken ct) =>
        {
            var messages = await bus.GetInboxAsync(squadName, unreadOnly ?? false, ct);
            return Results.Ok(messages);
        });

        group.MapGet("/recent", async (int? limit, ISquadMessageBus bus, CancellationToken ct) =>
        {
            var messages = await bus.GetRecentAsync(limit ?? 50, ct);
            return Results.Ok(messages);
        });

        group.MapPost("/{messageId}/reply", async (string messageId, ReplyMessageRequest req, ISquadMessageBus bus, CancellationToken ct) =>
        {
            var reply = await bus.ReplyAsync(messageId, req.From, req.Body, ct);
            return Results.Created($"/api/messages/{reply.Id}", reply);
        });

        group.MapGet("/{correlationId}/thread", async (string correlationId, ISquadMessageBus bus, CancellationToken ct) =>
        {
            var thread = await bus.GetConversationAsync(correlationId, ct);
            return Results.Ok(thread);
        });

        group.MapPost("/{messageId}/read", async (string messageId, ISquadMessageBus bus, CancellationToken ct) =>
        {
            await bus.MarkReadAsync(messageId, ct);
            return Results.NoContent();
        });

        group.MapGet("/stream", async (HttpContext context, string? squad, ISquadMessageBus bus, CancellationToken ct) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var targetSquad = squad ?? "*";
            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            };

            await foreach (var message in bus.SubscribeAsync(targetSquad, ct))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(message, jsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        });

        return app;
    }

    /// <summary>
    /// Maps the squad configuration API endpoints (/api/config).
    /// </summary>
    public static IEndpointRouteBuilder MapSquadConfigApi(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/config");

        // GET /api/config — get all config
        group.MapGet("/", async (ISquadConfigStore config, CancellationToken ct) =>
        {
            var all = await config.GetAllAsync(ct);
            return Results.Ok(all);
        });

        // GET /api/config/{key}
        group.MapGet("/{key}", async (string key, ISquadConfigStore config, CancellationToken ct) =>
        {
            var value = await config.GetAsync(key, ct);
            return value is not null
                ? Results.Ok(new { key, value })
                : Results.NotFound();
        });

        // PUT /api/config/{key}
        group.MapPut("/{key}", async (string key, ConfigValueRequest req, ISquadConfigStore config, CancellationToken ct) =>
        {
            await config.SetAsync(key, req.Value, ct);
            return Results.Ok(new { key, value = req.Value });
        });

        // DELETE /api/config/{key}
        group.MapDelete("/{key}", async (string key, ISquadConfigStore config, CancellationToken ct) =>
        {
            await config.DeleteAsync(key, ct);
            return Results.NoContent();
        });

        return app;
    }
}
