using System.Collections.Concurrent;
using TodoApp.Models;

namespace TodoApp.Services;

public class InMemoryTodoService : ITodoService
{
    private readonly ConcurrentDictionary<string, TodoItem> _todos = new();

    public Task<IEnumerable<TodoItem>> GetAllAsync()
    {
        var items = _todos.Values.OrderBy(t => t.CreatedAt).AsEnumerable();
        return Task.FromResult(items);
    }

    public Task<TodoItem?> GetByIdAsync(string id)
    {
        _todos.TryGetValue(id, out var item);
        return Task.FromResult(item);
    }

    public Task<TodoItem> CreateAsync(string title)
    {
        var item = new TodoItem { Title = title };
        _todos[item.Id] = item;
        return Task.FromResult(item);
    }

    public Task<TodoItem?> UpdateAsync(string id, bool isCompleted)
    {
        if (!_todos.TryGetValue(id, out var item))
            return Task.FromResult<TodoItem?>(null);

        item.IsCompleted = isCompleted;
        return Task.FromResult<TodoItem?>(item);
    }

    public Task<bool> DeleteAsync(string id)
    {
        return Task.FromResult(_todos.TryRemove(id, out _));
    }
}
