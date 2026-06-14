using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding Squad AI-agent team resources to an Aspire AppHost.
/// </summary>
public static class SquadBuilderExtensions
{
    /// <summary>
    /// Adds a Squad AI-agent team to the distributed application.
    /// </summary>
    public static IResourceBuilder<SquadResource> AddSquad(
        this IDistributedApplicationBuilder builder,
        string name,
        string? teamRoot = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var resolvedRoot = teamRoot ?? Directory.GetCurrentDirectory();
        var resource = new SquadResource(name, resolvedRoot);

        resource.Annotations.Add(new SquadTeamAnnotation(
            teamRoot: resolvedRoot,
            decisionsMdPath: Path.Combine(resolvedRoot, ".squad", "decisions.md"),
            inboxDir: Path.Combine(resolvedRoot, ".squad", "decisions", "inbox"),
            agentRosterFile: Path.Combine(resolvedRoot, ".squad", "team.md")));

        builder.Services.TryAddEventingSubscriber<SquadLifecycleHook>();

        var resourceBuilder = builder.AddResource(resource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "Squad",
                CreationTimeStamp = DateTime.UtcNow,
                State = new ResourceStateSnapshot("Configured", KnownResourceStateStyles.Info),
                Properties = [..SquadDashboardProperties.CreateStatic(resource)],
            });

        resourceBuilder.WithCommand(
            name: "refresh-agents",
            displayName: "Refresh Agents",
            executeCommand: ctx =>
            {
                var rosterFile = Path.Combine(resolvedRoot, ".squad", "team.md");
                var count = resource.Agents.Count;
                var exists = File.Exists(rosterFile);
                var message = exists
                    ? $"Team roster at '{rosterFile}' lists {count} agent(s): {string.Join(", ", resource.Agents)}."
                    : $"No team.md found at '{rosterFile}'; using default roster of {count} agent(s).";

                Console.WriteLine($"[Squad] {message}");
                return Task.FromResult(new ExecuteCommandResult { Success = true });
            },
            new CommandOptions
            {
                Description = "Re-reads .squad/team.md and reports the current agent roster.",
                IconName = "ArrowClockwise",
                IsHighlighted = true,
                UpdateState = _ => ResourceCommandState.Enabled,
            });

        resourceBuilder.WithCommand(
            name: "check-inbox",
            displayName: "Check Inbox",
            executeCommand: ctx =>
            {
                var inboxDir = Path.Combine(resolvedRoot, ".squad", "decisions", "inbox");
                if (!Directory.Exists(inboxDir))
                {
                    Console.WriteLine($"[Squad] Inbox directory does not exist: {inboxDir}");
                    return Task.FromResult(new ExecuteCommandResult { Success = true });
                }

                var pending = Directory.GetFiles(inboxDir, "*.md");
                var summary = pending.Length == 0
                    ? "Inbox is empty - no pending decisions."
                    : $"{pending.Length} pending item(s): {string.Join(", ", pending.Select(Path.GetFileName))}";

                Console.WriteLine($"[Squad] {summary}");
                return Task.FromResult(new ExecuteCommandResult { Success = true });
            },
            new CommandOptions
            {
                Description = "Counts pending .md files in .squad/decisions/inbox/.",
                IconName = "Mail",
                UpdateState = _ => ResourceCommandState.Enabled,
            });

        return resourceBuilder;
    }
}
