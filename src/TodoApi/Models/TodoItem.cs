using System.Text.Json.Serialization;

namespace TodoApi.Models;

public class TodoItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "default-user";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }
}

public class CreateTodoRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateTodoRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool? IsComplete { get; set; }
}
