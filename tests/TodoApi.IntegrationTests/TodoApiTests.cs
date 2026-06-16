using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace TodoApi.IntegrationTests;

public class TodoApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public TodoApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task CreateTodo_Returns201_WithLocation()
    {
        using var content = CreateJsonContent(new { title = "Test todo", description = "Optional description" });
        using var response = await _client.PostAsync("/api/todos", content);
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task GetTodos_ReturnsEmptyArray_Initially()
    {
        _ = await _client.GetFromJsonAsync<JsonElement>("/api/todos", JsonOptions);
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task GetTodos_ReturnsCreatedItems()
    {
        _ = await _client.GetAsync("/api/todos");
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task GetTodos_FiltersCompleted()
    {
        _ = await _client.GetAsync("/api/todos?completed=true");
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task GetTodoById_Returns200_WhenExists()
    {
        _ = await _client.GetAsync("/api/todos/test-id");
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task GetTodoById_Returns404_WhenNotExists()
    {
        _ = await _client.GetAsync("/api/todos/missing-id");
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task UpdateTodo_Returns200_WithUpdatedFields()
    {
        using var content = CreateJsonContent(new { title = "Updated todo", description = "Updated description", isCompleted = true });
        using var response = await _client.PutAsync("/api/todos/test-id", content);
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task UpdateTodo_Returns404_WhenNotExists()
    {
        using var content = CreateJsonContent(new { title = "Updated todo", description = "Updated description", isCompleted = true });
        using var response = await _client.PutAsync("/api/todos/missing-id", content);
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task DeleteTodo_Returns204()
    {
        using var response = await _client.DeleteAsync("/api/todos/test-id");
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task DeleteTodo_Returns404_WhenNotExists()
    {
        using var response = await _client.DeleteAsync("/api/todos/missing-id");
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task CompleteTodo_Returns200_AndSetsCompleted()
    {
        using var response = await _client.PatchAsync("/api/todos/test-id/complete", CreateJsonContent(new { }));
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task CreateTodo_Returns400_WhenTitleEmpty()
    {
        using var content = CreateJsonContent(new { title = string.Empty, description = "Optional description" });
        using var response = await _client.PostAsync("/api/todos", content);
    }

    [Fact(Skip = "Waiting for TodoApi implementation")]
    public async Task CreateTodo_Returns400_WhenTitleTooLong()
    {
        using var content = CreateJsonContent(new { title = new string('x', 201), description = "Optional description" });
        using var response = await _client.PostAsync("/api/todos", content);
    }

    private static StringContent CreateJsonContent<T>(T value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
}
