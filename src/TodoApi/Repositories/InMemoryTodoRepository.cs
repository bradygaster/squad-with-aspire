using System.Collections.Concurrent;
using TodoApi.Models;

namespace TodoApi.Repositories;

public class InMemoryTodoRepository : ITodoRepository
{
    private readonly ConcurrentDictionary<string, TodoItem> _todos = new();

    public Task<IEnumerable<TodoItem>> GetAllAsync(string userId)
    {
        var items = _todos.Values
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .AsEnumerable();
        return Task.FromResult(items);
    }

    public Task<TodoItem?> GetByIdAsync(string id, string userId)
    {
        _todos.TryGetValue(id, out var item);
        if (item is not null && item.UserId != userId)
            return Task.FromResult<TodoItem?>(null);
        return Task.FromResult(item);
    }

    public Task<TodoItem> CreateAsync(TodoItem item)
    {
        _todos[item.Id] = item;
        return Task.FromResult(item);
    }

    public Task<TodoItem> UpdateAsync(TodoItem item)
    {
        _todos[item.Id] = item;
        return Task.FromResult(item);
    }

    public Task<bool> DeleteAsync(string id, string userId)
    {
        if (_todos.TryGetValue(id, out var item) && item.UserId == userId)
        {
            return Task.FromResult(_todos.TryRemove(id, out _));
        }
        return Task.FromResult(false);
    }
}
