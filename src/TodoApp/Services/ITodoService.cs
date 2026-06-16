using TodoApp.Models;

namespace TodoApp.Services;

public interface ITodoService
{
    Task<IEnumerable<TodoItem>> GetAllAsync();
    Task<TodoItem?> GetByIdAsync(string id);
    Task<TodoItem> CreateAsync(string title);
    Task<TodoItem?> UpdateAsync(string id, bool isCompleted);
    Task<bool> DeleteAsync(string id);
}
