using Microsoft.Agents.AI;
using Squad.Agents.AI;

// Path to a Squad-initialized folder. SQUAD_TEAM_ROOT env var wins; otherwise CWD.
var teamRoot = Environment.GetEnvironmentVariable("SQUAD_TEAM_ROOT")
    ?? Directory.GetCurrentDirectory();

await using var agent = new SquadAgent(teamRoot, new SquadAgentOptions
{
    AgentName = "Coordinator",
});

Console.WriteLine($"Squad coordinator ready (team root: {teamRoot}).");
Console.WriteLine();

// First prompt — basic chat, no file access required
var session = await agent.CreateSessionAsync();
var response = await agent.RunAsync("Team fan out and each of you describe your role in one sentence.", session);

Console.WriteLine($"> {response.Text}");
