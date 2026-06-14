using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, ".."));

// Each squad is its own siloed resource in the Aspire topology.
builder.AddSquad("research-and-ideation-squad",
    teamRoot: Path.Combine(repoRoot, "research-and-ideation-squad"));

builder.AddSquad("site-design-squad",
    teamRoot: Path.Combine(repoRoot, "site-design-squad"));

builder.AddSquad("game-development-squad",
    teamRoot: Path.Combine(repoRoot, "game-development-squad"));

builder.AddSquad("qa-squad",
    teamRoot: Path.Combine(repoRoot, "qa-squad"));

builder.Build().Run();
