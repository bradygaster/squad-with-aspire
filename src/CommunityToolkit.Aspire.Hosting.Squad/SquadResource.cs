using System.Text.RegularExpressions;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents a Squad AI-agent team as a first-class .NET Aspire resource.
/// </summary>
public sealed class SquadResource : Resource, IResourceWithConnectionString
{
    private static readonly Regex AgentTableRowRegex =
        new(@"^\s*\|\s*([^|\r\n]+?)\s*\|", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex AgentLineRegex =
        new(@"^\s*-\s+\*\*([^*\r\n]+?)\*\*", RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly List<string> _agents;

    public string TeamRoot { get; }

    public IReadOnlyList<string> Agents => _agents;

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"squad://resource/{Uri.EscapeDataString(Name)}?teamRoot={Uri.EscapeDataString(TeamRoot)}&agents={Uri.EscapeDataString(string.Join(",", _agents))}&protocol=maf-1.0&messagingApi=/api/messages");

    public SquadResource(string name, string teamRoot)
        : base(name)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamRoot);

        TeamRoot = teamRoot;
        _agents = DiscoverAgents(teamRoot);
    }

    internal static List<string> DiscoverAgents(string teamRoot)
    {
        var rosterPath = Path.Combine(teamRoot, ".squad", "team.md");

        if (!File.Exists(rosterPath))
        {
            return ["coordinator"];
        }

        var content = File.ReadAllText(rosterPath);
        var agents = AgentTableRowRegex.Matches(content)
            .Concat(AgentLineRegex.Matches(content))
            .Select(m => NormalizeAgentName(m.Groups[1].Value))
            .Where(agent => IsKnownAgent(teamRoot, agent))
            .Distinct()
            .ToList();

        return agents.Count > 0 ? agents : ["coordinator"];
    }

    private static bool IsKnownAgent(string teamRoot, string agentName)
    {
        if (agentName.Length == 0) return false;

        var agentsDirectory = Path.Combine(teamRoot, ".squad", "agents");
        if (!Directory.Exists(agentsDirectory)) return true;

        return File.Exists(Path.Combine(agentsDirectory, agentName, "charter.md"));
    }

    private static string NormalizeAgentName(string value)
    {
        var cleaned = value.Trim().ToLowerInvariant().Replace("'", string.Empty);
        return Regex.Replace(cleaned, @"[^a-z0-9]+", "-").Trim('-');
    }
}
