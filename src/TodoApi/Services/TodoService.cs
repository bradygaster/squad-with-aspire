using System.Collections.Concurrent;
using TodoApi.Models;

namespace TodoApi.Services;

public class TodoService
{
    private readonly ConcurrentDictionary<string, TodoItem> _todos = new();

    public IEnumerable<TodoItem> GetAll() => _todos.Values.OrderByDescending(t => t.CreatedAt);

    public TodoItem? GetById(string id) => _todos.GetValueOrDefault(id);

    public TodoItem Create(string title)
    {
        var item = new TodoItem { Title = title };
        _todos[item.Id] = item;
        return item;
    }

    public TodoItem? Update(string id, string? title, bool? isComplete)
    {
        if (!_todos.TryGetValue(id, out var item)) return null;
        if (title is not null) item.Title = title;
        if (isComplete.HasValue) item.IsComplete = isComplete.Value;
        return item;
    }

    public bool Delete(string id) => _todos.TryRemove(id, out _);
}
