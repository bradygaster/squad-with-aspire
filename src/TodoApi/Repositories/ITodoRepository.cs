using TodoApi.Models;

namespace TodoApi.Repositories;

public interface ITodoRepository
{
    Task<IEnumerable<TodoItem>> GetAllAsync(string userId);
    Task<TodoItem?> GetByIdAsync(string id, string userId);
    Task<TodoItem> CreateAsync(TodoItem item);
    Task<TodoItem> UpdateAsync(TodoItem item);
    Task<bool> DeleteAsync(string id, string userId);
}
