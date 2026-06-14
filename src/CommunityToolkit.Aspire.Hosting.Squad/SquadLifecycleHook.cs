using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Logging;
using System.Security;

namespace Aspire.Hosting;

internal sealed class SquadLifecycleHook : IDistributedApplicationEventingSubscriber
{
    private readonly ILogger<SquadLifecycleHook> _logger;
    private readonly ResourceNotificationService _notifications;
    private readonly ISquadMessageBus? _messageBus;

    private const string StateSpawning = "Spawning";
    private const string StateActive = "Active";
    private const string StateStopped = "Finished";

    public SquadLifecycleHook(
        ILogger<SquadLifecycleHook> logger,
        ResourceNotificationService notifications,
        ISquadMessageBus? messageBus = null)
    {
        _logger = logger;
        _notifications = notifications;
        _messageBus = messageBus;
    }

    public Task SubscribeAsync(
        IDistributedApplicationEventing eventing,
        DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventing);

        eventing.Subscribe<BeforeStartEvent>(OnBeforeStartAsync);
        eventing.Subscribe<AfterResourcesCreatedEvent>(OnAfterResourcesCreatedAsync);
        eventing.Subscribe<ResourceStoppedEvent>(OnResourceStoppedAsync);

        return Task.CompletedTask;
    }

    private Task OnBeforeStartAsync(BeforeStartEvent @event, CancellationToken cancellationToken) =>
        BeforeStartAsync(@event.Model, cancellationToken);

    private Task OnAfterResourcesCreatedAsync(AfterResourcesCreatedEvent @event, CancellationToken cancellationToken) =>
        AfterResourcesCreatedAsync(@event.Model, cancellationToken);

    private Task OnResourceStoppedAsync(ResourceStoppedEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Resource is SquadResource squad)
        {
            return _notifications.PublishUpdateAsync(squad, s => s with
            {
                State = new ResourceStateSnapshot(StateStopped, KnownResourceStateStyles.Info),
            });
        }

        return Task.CompletedTask;
    }

    private async Task BeforeStartAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken)
    {
        var squads = appModel.Resources.OfType<SquadResource>().ToList();
        if (squads.Count == 0) return;

        _logger.LogInformation("Squad: {Count} squad team resource(s) discovered.", squads.Count);

        foreach (var squad in squads)
        {
            await _notifications.PublishUpdateAsync(squad, s => s with
            {
                State = new ResourceStateSnapshot(StateSpawning, KnownResourceStateStyles.Info),
                Properties = [..ReadTeamProperties(squad)],
            });

            _logger.LogInformation(
                "Squad '{Name}' is Spawning - {AgentCount} agent(s) discovered: {Agents}",
                squad.Name, squad.Agents.Count, string.Join(", ", squad.Agents));
        }
    }

    private async Task AfterResourcesCreatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken)
    {
        var squads = appModel.Resources.OfType<SquadResource>().ToList();
        if (squads.Count == 0) return;

        foreach (var squad in squads)
        {
            _logger.LogInformation("Squad '{Name}' is Active - {AgentCount} agent(s) ready.", squad.Name, squad.Agents.Count);

            await _notifications.PublishUpdateAsync(squad, s => s with
            {
                State = new ResourceStateSnapshot(StateActive, KnownResourceStateStyles.Success),
                Properties = [..ReadTeamProperties(squad)],
            });
        }
    }

    private ResourcePropertySnapshot[] ReadTeamProperties(SquadResource squad)
    {
        try
        {
            return SquadDashboardProperties.CreateWithLiveStats(squad, _messageBus);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger.LogWarning(
                ex,
                "Could not read Squad dashboard metadata for resource '{SquadName}' from '{TeamRoot}'.",
                squad.Name,
                squad.TeamRoot);
            return [];
        }
    }
}
