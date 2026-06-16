namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Carries Squad team metadata on a <see cref="SquadResource"/>.
/// </summary>
public sealed class SquadTeamAnnotation : IResourceAnnotation
{
    public string TeamRoot { get; }
    public string DecisionsMdPath { get; }
    public string InboxDir { get; }
    public string AgentRosterFile { get; }

    public SquadTeamAnnotation(string teamRoot, string decisionsMdPath, string inboxDir, string agentRosterFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamRoot);

        TeamRoot = teamRoot;
        DecisionsMdPath = decisionsMdPath;
        InboxDir = inboxDir;
        AgentRosterFile = agentRosterFile;
    }
}
