namespace Aspire.Hosting;

/// <summary>
/// Simple key-value configuration store for squad-level settings.
/// Backed by the same SQLite database as the message bus.
/// </summary>
public interface ISquadConfigStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default);
}
