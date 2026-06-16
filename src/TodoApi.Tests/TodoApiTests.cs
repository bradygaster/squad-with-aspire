using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoApi.Models;

namespace TodoApi.Tests;

public class TodoApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public TodoApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyList_Initially()
    {
        var response = await _client.GetAsync("/api/todos");
        response.EnsureSuccessStatusCode();
        var todos = await response.Content.ReadFromJsonAsync<TodoItem[]>();
        Assert.NotNull(todos);
        Assert.Empty(todos);
    }

    [Fact]
    public async Task CreateTodo_ReturnsCreated()
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { Title = "Test todo" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var todo = await response.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(todo);
        Assert.Equal("Test todo", todo.Title);
        Assert.False(todo.IsComplete);
    }

    [Fact]
    public async Task CreateTodo_EmptyTitle_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { Title = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTodo_MarksComplete()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/todos", new { Title = "Complete me" });
        var created = await createResponse.Content.ReadFromJsonAsync<TodoItem>();

        var updateResponse = await _client.PutAsJsonAsync($"/api/todos/{created!.Id}", new { IsComplete = true });
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<TodoItem>();
        Assert.True(updated!.IsComplete);
    }

    [Fact]
    public async Task DeleteTodo_ReturnsNoContent()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/todos", new { Title = "Delete me" });
        var created = await createResponse.Content.ReadFromJsonAsync<TodoItem>();

        var deleteResponse = await _client.DeleteAsync($"/api/todos/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/todos/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetById_NotFound()
    {
        var response = await _client.GetAsync("/api/todos/nonexistent-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
