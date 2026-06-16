using TodoApi.Models;
using TodoApi.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TodoService>();
builder.Services.AddOpenApi();

// CORS for SPA frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    await next();
});

app.UseCors();
app.MapOpenApi();

var api = app.MapGroup("/api/todos");

api.MapGet("/", (TodoService svc) => Results.Ok(svc.GetAll()));

api.MapGet("/{id}", (string id, TodoService svc) =>
    svc.GetById(id) is { } todo ? Results.Ok(todo) : Results.NotFound());

api.MapPost("/", (CreateTodoRequest req, TodoService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Title))
        return Results.BadRequest(new { error = "Title is required" });
    if (req.Title.Length > 200)
        return Results.BadRequest(new { error = "Title must be 200 characters or fewer" });

    var item = svc.Create(req.Title.Trim(), req.Description?.Trim());
    return Results.Created($"/api/todos/{item.Id}", item);
});

api.MapPut("/{id}", (string id, UpdateTodoRequest req, TodoService svc) =>
    svc.Update(id, req.Title, req.IsComplete, req.Description) is { } todo
        ? Results.Ok(todo)
        : Results.NotFound());

api.MapDelete("/{id}", (string id, TodoService svc) =>
    svc.Delete(id) ? Results.NoContent() : Results.NotFound());

app.Run();

public partial class Program { }

public partial class Program { }
