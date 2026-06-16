using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Data;
using TodoApp.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("TodoDb")
        ?? "Data Source=todos.db"));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();

var todos = app.MapGroup("/api/todos");

todos.MapGet("/", async (TodoDbContext db) =>
    await db.Todos.OrderByDescending(t => t.CreatedAt).ToListAsync());

todos.MapPost("/", async (CreateTodoRequest request, TodoDbContext db) =>
{
    var todo = new TodoItem
    {
        Title = request.Title,
        IsComplete = false,
        CreatedAt = DateTime.UtcNow
    };
    db.Todos.Add(todo);
    await db.SaveChangesAsync();
    return Results.Created($"/api/todos/{todo.Id}", todo);
});

todos.MapPut("/{id}", async (int id, UpdateTodoRequest request, TodoDbContext db) =>
{
    var todo = await db.Todos.FindAsync(id);
    if (todo is null) return Results.NotFound();

    if (request.Title is not null) todo.Title = request.Title;
    if (request.IsComplete is not null) todo.IsComplete = request.IsComplete.Value;

    await db.SaveChangesAsync();
    return Results.Ok(todo);
});

todos.MapDelete("/{id}", async (int id, TodoDbContext db) =>
{
    var todo = await db.Todos.FindAsync(id);
    if (todo is null) return Results.NotFound();

    db.Todos.Remove(todo);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

public record CreateTodoRequest(string Title);
public record UpdateTodoRequest(string? Title, bool? IsComplete);

// Make Program accessible for integration tests
public partial class Program { }
