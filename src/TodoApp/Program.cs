using TodoApp.Models;
using TodoApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ITodoService, InMemoryTodoService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api/todos");

api.MapGet("/", async (ITodoService service) =>
    Results.Ok(await service.GetAllAsync()));

api.MapGet("/{id}", async (string id, ITodoService service) =>
    await service.GetByIdAsync(id) is { } todo
        ? Results.Ok(todo)
        : Results.NotFound());

api.MapPost("/", async (CreateTodoRequest request, ITodoService service) =>
{
    if (string.IsNullOrWhiteSpace(request.Title))
        return Results.BadRequest(new { error = "Title is required" });

    var todo = await service.CreateAsync(request.Title.Trim());
    return Results.Created($"/api/todos/{todo.Id}", todo);
});

api.MapPut("/{id}", async (string id, UpdateTodoRequest request, ITodoService service) =>
    await service.UpdateAsync(id, request.IsCompleted) is { } todo
        ? Results.Ok(todo)
        : Results.NotFound());

api.MapDelete("/{id}", async (string id, ITodoService service) =>
    await service.DeleteAsync(id)
        ? Results.NoContent()
        : Results.NotFound());

app.Run();

public record CreateTodoRequest(string Title);
public record UpdateTodoRequest(bool IsCompleted);

public partial class Program { }

