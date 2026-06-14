using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics;

namespace Aspire.Hosting;

public static class SquadMessagingServiceExtensions
{
    /// <summary>
    /// The ActivitySource name used by the Squad messaging bus.
    /// Add this to your OpenTelemetry TracerProvider to see squad messaging spans.
    /// </summary>
    public const string ActivitySourceName = "Squad.Messaging";

    /// <summary>
    /// The ActivitySource name used by the Squad config store.
    /// </summary>
    public const string ConfigActivitySourceName = "Squad.Config";

    public static IServiceCollection AddSquadMessaging(this IServiceCollection services, string? dbPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var resolvedPath = dbPath ?? Path.Combine(Directory.GetCurrentDirectory(), "squad-messages.db");
        services.TryAddSingleton<ISquadMessageBus>(new SqliteSquadMessageBus(resolvedPath));
        services.TryAddSingleton<ISquadConfigStore>(new SqliteSquadConfigStore(resolvedPath));
        return services;
    }
}
