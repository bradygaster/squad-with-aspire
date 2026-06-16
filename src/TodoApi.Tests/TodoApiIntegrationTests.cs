using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoApi.Models;

namespace TodoApi.Tests;

/// <summary>
/// Extended integration tests covering edge cases, partial updates,
/// lifecycle scenarios, and validation beyond the basic CRUD tests.
/// </summary>
public class TodoApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public TodoApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    #region POST /api/todos - Validation

    [Fact]
    public async Task CreateTodo_WithMissingTitle_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTodo_WithWhitespaceTitle_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { Title = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTodo_SetsCreatedAtTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var response = await _client.PostAsJsonAsync("/api/todos", new { Title = "Timestamp test" });
        response.EnsureSuccessStatusCode();

        var todo = await response.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(todo);
        Assert.True(todo.CreatedAt >= before.AddSeconds(-1));
    }

    [Fact]
    public async Task CreateTodo_WithDescription_PersistsDescription()
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { Title = "Has desc", Description = "A description" });
        response.EnsureSuccessStatusCode();

        var todo = await response.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(todo);
        Assert.Equal("A description", todo.Description);
    }

    #endregion

    #region PUT /api/todos/{id} - Partial Updates

    [Fact]
    public async Task UpdateTodo_PartialUpdate_OnlyChangesTitle()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/todos", new { Title = "Original" });
        var created = await createResponse.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(created);

        var response = await _client.PutAsJsonAsync($"/api/todos/{created.Id}", new { Title = "Renamed" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated.Title);
        Assert.False(updated.IsComplete);
    }

    [Fact]
    public async Task UpdateTodo_MarkComplete_SetsCompletedAt()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/todos", new { Title = "Complete me" });
        var created = await createResponse.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(created);
        Assert.Null(created.CompletedAt);

        var response = await _client.PutAsJsonAsync($"/api/todos/{created.Id}", new { IsComplete = true });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(updated);
        Assert.True(updated.IsComplete);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task UpdateTodo_WithNonExistentId_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync("/api/todos/does-not-exist-xyz", new { Title = "Irrelevant" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region DELETE /api/todos/{id} - Edge Cases

    [Fact]
    public async Task DeleteTodo_WithNonExistentId_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync("/api/todos/does-not-exist-abc");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTodo_ThenGetById_ReturnsNotFound()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/todos", new { Title = "Will vanish" });
        var created = await createResponse.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(created);

        await _client.DeleteAsync($"/api/todos/{created.Id}");

        var getResponse = await _client.GetAsync($"/api/todos/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    #endregion

    #region Full Lifecycle

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateCompleteDelete()
    {
        // Create
        var createResponse = await _client.PostAsJsonAsync("/api/todos", new { Title = "Lifecycle item", Description = "Full flow" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var todo = await createResponse.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(todo);
        Assert.Equal("Lifecycle item", todo.Title);

        // Read back from list
        var listResponse = await _client.GetAsync("/api/todos");
        var todos = await listResponse.Content.ReadFromJsonAsync<TodoItem[]>();
        Assert.NotNull(todos);
        Assert.Contains(todos, t => t.Id == todo.Id);

        // Update title
        var updateResponse = await _client.PutAsJsonAsync($"/api/todos/{todo.Id}", new { Title = "Renamed item" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(updated);
        Assert.Equal("Renamed item", updated.Title);

        // Mark complete
        var completeResponse = await _client.PutAsJsonAsync($"/api/todos/{todo.Id}", new { IsComplete = true });
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var completed = await completeResponse.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(completed);
        Assert.True(completed.IsComplete);

        // Delete
        var deleteResponse = await _client.DeleteAsync($"/api/todos/{todo.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Confirm gone
        var finalList = await _client.GetAsync("/api/todos");
        var remaining = await finalList.Content.ReadFromJsonAsync<TodoItem[]>();
        Assert.NotNull(remaining);
        Assert.DoesNotContain(remaining, t => t.Id == todo.Id);
    }

    [Fact]
    public async Task MultipleTodos_CreateSeveral_AllAppearInList()
    {
        var titles = new[] { "Todo A", "Todo B", "Todo C" };
        foreach (var title in titles)
        {
            var resp = await _client.PostAsJsonAsync("/api/todos", new { Title = title });
            resp.EnsureSuccessStatusCode();
        }

        var listResponse = await _client.GetAsync("/api/todos");
        var todos = await listResponse.Content.ReadFromJsonAsync<TodoItem[]>();
        Assert.NotNull(todos);

        foreach (var title in titles)
        {
            Assert.Contains(todos, t => t.Title == title);
        }
    }

    #endregion
}
