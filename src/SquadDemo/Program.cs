using Microsoft.Agents.AI;
using Squad.Agents.AI;

var repoRoot = Environment.GetEnvironmentVariable("SQUAD_REPO_ROOT")
    ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));

var squadFolders = new[]
{
    "research-and-ideation-squad",
    "site-design-squad",
    "game-development-squad",
    "qa-squad",
};

foreach (var folder in squadFolders)
{
    var teamRoot = Path.Combine(repoRoot, folder);
    if (!Directory.Exists(Path.Combine(teamRoot, ".squad")))
    {
        Console.WriteLine($"⚠ Skipping {folder} — no .squad/ folder found.");
        Console.WriteLine();
        continue;
    }

    await using var agent = new SquadAgent(teamRoot, new SquadAgentOptions
    {
        AgentName = "Coordinator",
    });

    Console.WriteLine($"━━━ {folder} ━━━");
    Console.WriteLine($"  Team root: {teamRoot}");
    Console.WriteLine();

    var session = await agent.CreateSessionAsync();
    var response = await agent.RunAsync(
        "Team fan out and each of you describe your role in one sentence.", session);

    Console.WriteLine($"> {response.Text}");
    Console.WriteLine();
}
