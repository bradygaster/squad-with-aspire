# Building a Multi-Agent Team with Microsoft Agent Framework + Squad

A 10-minute walkthrough for embedding a full Squad multi-agent team inside any .NET application powered by [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/) and the [GitHub Copilot SDK](https://docs.github.com/en/copilot/how-tos/copilot-sdk).

> **What's a Squad?** Squad is a small, file-system-driven multi-agent framework that lives in a `.squad/` folder next to your code. A *coordinator* agent reads the team roster, charters, and decisions from that folder and dispatches work to specialist sub-agents (a lead, a frontend dev, a tester, etc.). Until now Squad ran as its own CLI process. With **Squad.Agents.AI**, the whole coordinator+team becomes a single `AIAgent` you can compose into any MAF workflow — including a first-class Aspire resource (Step 10 at the bottom).

---

> ### ⚠️ Required workaround (temporary)
>
> Add a **direct `PackageReference` to `GitHub.Copilot.SDK`** alongside Squad.Agents.AI. Without it, the SDK's MSBuild targets that download `copilot.exe` into your `bin/` folder don't fire, and the app crashes on first run with:
>
> ```
> InvalidOperationException: Copilot runtime not found at
>   bin\Debug\net10.0\runtimes\win-x64\native\copilot.exe
> ```
>
> [microsoft/agent-framework#6457](https://github.com/microsoft/agent-framework/pull/6457) (merged 2026-06-10) ships a `buildTransitive` bridge in `Microsoft.Agents.AI.GitHub.Copilot` that makes this automatic, but it hasn't been cut into a MAF preview yet. Until that lands, the direct ref is the workaround — every step in this tutorial assumes you've added it.

---

## What you'll build

A .NET console app (Steps 1–9) and a .NET Aspire example (Step 10) that:

1. Loads a Squad team from a `.squad/` folder.
2. Exposes the coordinator as a `Microsoft.Agents.AI.AIAgent` that behaves identically to `copilot --agent squad` from a terminal.
3. Asks the coordinator to read the team roster and answer questions about it.
4. **Dispatches a question across multiple specialist agents** and synthesises their answers.
5. **Observes that dispatch live in the Aspire dashboard** — `squad.subagent {Name}` spans per spawn, with timeline annotations for every lifecycle event.
6. Streams the response token by token.
7. Drops two real Squad teams into an Aspire AppHost as side-by-side resources with one line of registration each.

---

## Prerequisites

- **.NET 8 or later** (.NET 10 recommended for newest MAF features).
- **GitHub Copilot CLI** installed globally: `npm install -g @github/copilot`, then `copilot login`.
- **A Squad-initialized folder.** If you don't already have one, run `npx @bradygaster/squad-cli init` in any folder — it'll scaffold `.squad/team.md`, `.github/agents/squad.agent.md` (the coordinator system prompt), per-agent charters, and a few decisions.
- A **GitHub Copilot subscription** (Individual, Business, or Enterprise) — the same auth your CLI is logged in with.

---

## Step 1 — Create the project

```powershell
mkdir SquadDemo
cd SquadDemo
dotnet new console --framework net10.0
dotnet add package Squad.Agents.AI --prerelease

# Workaround until microsoft/agent-framework#6457 ships in a MAF preview:
# add the SDK directly so its MSBuild targets download copilot.exe into
# bin/{cfg}/{tfm}/runtimes/{rid}/native/. Without this, the SDK fails on
# startup with "Copilot runtime not found at runtimes\win-x64\native\copilot.exe".
dotnet add package GitHub.Copilot.SDK --version 1.0.0
```

That `--prerelease` is important: Squad.Agents.AI ships preview NuGets while it stabilizes. **0.5.1 or later required** for the simplified observability + Aspire flow below.

---

## Step 2 — Wire up the agent

Replace `Program.cs`:

```csharp
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
var response = await agent.RunAsync("In one sentence: what is your role?", session);
Console.WriteLine($"> {response.Text}");
```

That's the entire setup. `SquadAgent` extends `Microsoft.Agents.AI.DelegatingAIAgent`, so anywhere MAF accepts an `AIAgent` you can pass a Squad team instead.

> **Why no `Instructions` override?** Squad.Agents.AI 0.5.0+ auto-injects `--agent squad` when `.github/agents/squad.agent.md` exists at the team root. That file IS the coordinator system prompt (eager execution, parallel fan-out, dispatch via the `task` tool). Setting `Instructions` would override it, which is almost never what you want when wrapping a Squad team. To opt out, set `opts.AgentFileName = null`.

---

## Step 3 — Run it

```powershell
dotnet run
```

You should see the coordinator introduce itself. If you get *"Copilot CLI not found"*, the GitHub Copilot CLI binary either isn't installed or isn't on your `PATH` — `npm install -g @github/copilot` fixes both.

---

## Step 4 — Talk to a real team

Point the demo at your real Squad team and ask it questions that require reading the team files:

```powershell
$env:SQUAD_TEAM_ROOT = "C:\path\to\your\squad-team"
dotnet run
```

Add a follow-up prompt to the program:

```csharp
var roster = await agent.RunAsync(
    "Read .squad/team.md and list every member with their role. Just the names and roles, one per line.",
    session);
Console.WriteLine($"> {roster.Text}");
```

The coordinator will use its `view` tool to actually open `.squad/team.md` and reply with the live roster. This is the part that distinguishes a real coordinator from a stub — the agent is reading the team's own files at runtime, not making them up.

---

## Step 5 — Dispatch across multiple specialists

This is the prompt that proves the coordinator is actually *delegating*, not just answering everything itself. The phrasing matters: a casual "Team, give me…" prompt will often make the coordinator role-play multiple voices in a single response without actually spawning anything, because `squad.agent.md` classifies short roster questions as *Direct Mode*. To force real `task`-tool dispatch (*Full Mode*) you need to be explicit:

```csharp
const string Prompt =
    "Use the task tool to dispatch a subagent to Picard. Send Picard this exact prompt: " +
    "\"In one sentence, what is the most important property of an agent framework architecture?\" " +
    "Then dispatch a second subagent to Data with this exact prompt: " +
    "\"In one sentence, what is the most important property of an agent framework implementation?\" " +
    "Wait for both to return, then output ONLY the two answers verbatim " +
    "(each on its own line, prefixed with the agent name), and nothing else.";

await foreach (var update in agent.RunStreamingAsync(Prompt, session))
{
    Console.Write(update.Text);
}
Console.WriteLine();
```

Real output (against a Star Trek–themed team):

```
Picard: Clear boundaries between agent context and shared state — so agents can be
composed, swapped, and reasoned about independently without hidden coupling.
Data: Reliable, observable control flow — you need to be able to see exactly what
the agent did, why, and reproduce it, or you can't debug or trust anything it produces.
```

The coordinator used its `task` tool to spawn each specialist in a separate sub-session, gathered their replies, and only then composed the final answer. That's the unique Squad value: multiple distinct voices, each owning their part of the answer, instead of a single LLM impersonating a team. Step 8 below shows how to *observe* that dispatch live.

---

## Step 6 — Stream the response

For chat UIs you usually want token-by-token output. MAF gives you `RunStreamingAsync` (already used above). The streaming surface is identical to any other MAF agent — you can drop the same loop into Blazor, an AspNetCore SSE endpoint, or an Aspire dashboard view.

---

## Step 7 — Use dependency injection

For real apps you want the agent in the DI container:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSquadAgent(options =>
{
    options.SquadFolderPath = builder.Configuration["Squad:TeamRoot"]
                              ?? Directory.GetCurrentDirectory();
});

using var app = builder.Build();

var agent = app.Services.GetRequiredService<SquadAgent>();
var session = await agent.CreateSessionAsync();
var response = await agent.RunAsync("Status report.", session);
Console.WriteLine(response.Text);
```

You can also use connection strings: set `ConnectionStrings:squad` to a path or `squad://localhost?teamRoot=...&cliArgs=--yolo` URI and `AddSquadAgent()` picks it up automatically.

For **multiple squads** (e.g., research + dev side-by-side), use the keyed-DI overload. Squad.Agents.AI 0.4.0+ resolves the Aspire-style direct connection-string name first, then falls back to the legacy `squad-{name}` prefix — so an Aspire AppHost that does `builder.AddSquad("research-squad", …)` just needs:

```csharp
builder.Services.AddKeyedSquadAgent("research-squad"); // resolves ConnectionStrings:research-squad
builder.Services.AddKeyedSquadAgent("dev-squad");      // resolves ConnectionStrings:dev-squad
```

…and the consumer can resolve either with `[FromKeyedServices("research-squad")] SquadAgent agent`. No manual `GetConnectionString()` + URI parsing needed.

---

## Step 8 — Observe the inner subagent chats

What you've seen so far hides one thing: *what's happening between the coordinator and its subagents in flight?* Squad.Agents.AI 0.4.0+ ships first-class OpenTelemetry support that's **on by default**: just register an `ActivitySource` and the spans flow.

```csharp
using OpenTelemetry.Trace;

// Two-line wiring — that's it. No callbacks required.
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(SquadAgentDiagnostics.ActivitySourceName));   // "Microsoft.Agents.AI.Squad"

builder.Services.AddSquadAgent(o => o.SquadFolderPath = "...");
```

Hit the agent with a Full Mode prompt (Step 5 style) and the dashboard's **Traces** view now shows one `squad.subagent {Name}` span per dispatch, with annotated lifecycle events on the timeline:

| Event | When |
|---|---|
| `squad.subagent.start` | The `task` tool spawned the subagent |
| `squad.subagent.message` | The subagent emitted an assistant message (tag: `squad.subagent.message_preview`) |
| `squad.subagent.completed` | The subagent finished and returned to the coordinator |
| `squad.subagent.failed` | The subagent errored — span status set to `Error` |

**Span tags you can filter on:**

| Tag | Value |
|---|---|
| `squad.subagent.name` | `Picard`, `Data` — the custom-agent name from `.squad/team.md` |
| `squad.subagent.display_name` | The pretty name |
| `squad.subagent.sdk_agent_id` | `toolu_vrtx_…` — correlates with the subagent's session id in CLI logs |
| `squad.subagent.reply_preview` | First 512 chars of the subagent's reply, attached when the span closes |

### Want a callback instead of (or in addition to) spans?

`OnSubagentTrace` is now a pure customization hook — completely independent of OTel. Useful for ILogger structured logs, custom metrics, dashboards, audit trails:

```csharp
builder.Services.AddOptions<SquadAgentOptions>()
    .Configure<ILoggerFactory>((opts, loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("Squad.Subagent");
        opts.OnSubagentTrace = trace =>
        {
            switch (trace.Kind)
            {
                case SquadAgentTraceEventKind.SubagentStarted:
                    logger.LogInformation(">> {SubagentName} started", trace.SubagentName);
                    break;
                case SquadAgentTraceEventKind.AssistantMessage when trace.SdkAgentId is not null:
                    logger.LogInformation("   {SubagentName}: {Content}", trace.SubagentName ?? trace.SdkAgentId, trace.Content);
                    break;
                case SquadAgentTraceEventKind.SubagentCompleted:
                    logger.LogInformation("<< {SubagentName} done", trace.SubagentName);
                    break;
            }
        };
    });
```

Both layers (OTel spans + callback) are active simultaneously by default. To opt out of the SDK's built-in spans (e.g., to avoid double-counting against another telemetry layer), set `opts.EmitSubagentActivities = false` — the callback continues to fire.

The full categorised event list is on [`SquadAgentTraceEventKind`](https://github.com/bradygaster/squad/blob/dev/src/Squad.Agents.AI/SquadAgentDiagnostics.cs): `SubagentSelected`, `SubagentStarted`, `SubagentCompleted`, `SubagentFailed`, `AssistantMessage`, `ToolStart`, `ToolComplete`, `SessionIdle`. Need something we didn't categorise? `trace.RawEvent` carries the original `GitHub.Copilot.SessionEvent` instance.

---

## Step 9 — Customise the session (advanced)

`SquadAgentOptions.ConfigureSession` exposes the underlying `GitHub.Copilot.SessionConfig` so you can opt into MCP servers, custom skills, model overrides, etc:

```csharp
builder.Services.AddSquadAgent(options =>
{
    options.SquadFolderPath = "...";
    options.ConfigureSession = session =>
    {
        session.Model = "gpt-5.2";
        session.ReasoningEffort = "high";
        // session.McpServers, session.SkillDirectories, etc.
    };
});
```

`ConfigureCopilotClient` is the same idea one level lower (for `CopilotClientOptions`). Both are optional — the defaults work out of the box.

---

## Step 10 — Run it in .NET Aspire

Everything above also works as a first-class Aspire resource via `CommunityToolkit.Aspire.Hosting.Squad` ([`CommunityToolkit/Aspire#1394`](https://github.com/CommunityToolkit/Aspire/pull/1394)). A complete end-to-end example with **two side-by-side Squad teams** ships in `examples/squad/` on that PR.

### AppHost — one line per squad

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var researchSquadRoot = Path.Combine(builder.AppHostDirectory, "research-squad");
var devSquadRoot      = Path.Combine(builder.AppHostDirectory, "dev-squad");

var researchSquad = builder.AddSquad("research-squad", teamRoot: researchSquadRoot);
var devSquad      = builder.AddSquad("dev-squad",      teamRoot: devSquadRoot);

builder.AddProject<Projects.SquadApi>("squad-api")
    .WithReference(researchSquad)
    .WithReference(devSquad);

builder.Build().Run();
```

Each `AddSquad(...)` becomes its own row in the Aspire dashboard, with the roster discovered from `.squad/team.md` and the connection string (`squad://resource/{name}?teamRoot=…&agents=…&protocol=maf-1.0`) injected into any project that does `.WithReference(squad)`.

### Consumer ApiApp — also one line per squad

```csharp
// Surface the per-subagent spans in the dashboard (default-on as of 0.4.0).
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(SquadAgentDiagnostics.ActivitySourceName));

// Squad.Agents.AI 0.4.0+ picks up ConnectionStrings:{name} directly (Aspire-style),
// 0.5.0+ auto-injects --agent squad so the wrapped session behaves identically
// to `copilot --agent squad` in a terminal. No callbacks, no manual parsing.
builder.Services.AddKeyedSquadAgent("research-squad");
builder.Services.AddKeyedSquadAgent("dev-squad");

app.MapPost("/ask", async (
    [AsParameters] SquadQuery q,
    AskRequest req,
    [FromKeyedServices("research-squad")] SquadAgent research,
    [FromKeyedServices("dev-squad")]      SquadAgent dev,
    CancellationToken ct) =>
{
    var agent = q.Squad == "research" ? research : dev;
    var session = await agent.CreateSessionAsync(ct);
    var response = await agent.RunAsync(req.Prompts[0], session, cancellationToken: ct);
    return Results.Ok(new { squad = q.Squad, reply = response.Text });
});
```

### What you see in the dashboard

Open the AppHost's dashboard (default `https://localhost:17277`) and hit `POST /ask?squad=dev` with a Full Mode prompt (the example's `GET /` endpoint returns ready-to-paste sample prompts):

- **Resources tab** — two `squad` rows (research-squad, dev-squad), each with the discovered roster as properties.
- **Traces tab** — one trace per request showing:
  - `POST /ask` HTTP root span
  - `squad.ask {squad}` wrapper span (emitted by the ApiApp)
  - N child `squad.subagent {Name}` spans, one per dispatched specialist, with `squad.subagent.start` / `message` / `completed` ActivityEvents annotated on the timeline.
- **Structured Logs tab** — per-subagent log lines (category `Squad.Subagent.{squad-name}`) showing start/done/message events with queryable structured fields.

The two teams in the example use deliberately different "casts" — research is The Matrix (Morpheus, Trinity, Oracle, Tank), dev is The Simpsons (Lisa, Marge, Frink, Comic Book Guy). Hitting the dispatch prompt against the dev squad produces replies that end in *"glavin!"* (pure Professor Frink) — proof that the coordinator is dispatching real specialists with their own charters, not impersonating them inline.

The full source is at [`examples/squad/`](https://github.com/CommunityToolkit/Aspire/pull/1394/files) — fork the repo, F5 the `Squad.slnx` solution at the root, and you're running both teams + the API + the dashboard locally in seconds.

---

## What just happened

Under the hood, the SDK's MSBuild targets downloaded the Copilot CLI binary into `bin/{config}/{tfm}/runtimes/{rid}/native/copilot.exe` at build time. At runtime, `SquadAgent` spins up that CLI as a child process with `--agent squad`, hands it the Squad team root, and exposes the resulting session as an `AIAgent`. Tool calls (file reads, shell commands, MCP) go through the same permission-and-tool gates as the interactive CLI.

This means everything Copilot CLI can do — file IO, git, web fetches, MCP tools — your Squad team can do, from inside your MAF app, with the same single subscription.

---

## Where to go next

- **Aspire integration:** [`CommunityToolkit/Aspire#1394`](https://github.com/CommunityToolkit/Aspire/pull/1394) (in review) and the full example at `examples/squad/`.
- **MAF workflows:** chain the Squad coordinator with other `AIAgent` instances in a `WorkflowBuilder` — Squad becomes one node in a bigger graph.
- **Squad CLI itself:** `npx @bradygaster/squad-cli` for the standalone experience, slash commands, and richer interactive features.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| *Copilot runtime not found at runtimes\\win-x64\\native\\copilot.exe* (on first run) | Add `<PackageReference Include="GitHub.Copilot.SDK" Version="1.0.0" />` directly to your csproj (or `dotnet add package GitHub.Copilot.SDK --version 1.0.0`). The SDK's MSBuild targets only auto-import for projects with a direct reference — without it, the binary never lands in your `bin/`. This will become unnecessary once [microsoft/agent-framework#6457](https://github.com/microsoft/agent-framework/pull/6457) ships in a MAF preview. |
| *Copilot CLI not found / login required* | `npm install -g @github/copilot`, then `copilot login`. The bundled binary in `bin/` is the same CLI; it uses your global auth. |
| Coordinator role-plays subagents in one reply instead of dispatching | On 0.5.0 or later, this should be rare — the SDK auto-injects `--agent squad` so `squad.agent.md` is the system prompt. If you're on 0.4.x or earlier, either upgrade or add `opts.CliArgs.Add("--agent"); opts.CliArgs.Add("squad");` manually. Also check that `.github/agents/squad.agent.md` actually exists at your team root. |
| `RemoteRpcException: Custom agent 'squad' not found` on the very first `RunAsync` | You're on `Squad.Agents.AI 0.5.0-preview.7` exactly — that one preview used `SessionConfig.Agent`, which looks up the SDK's in-memory `CustomAgents` registry, not on-disk `.github/agents/*.agent.md`. Bump to **0.5.1-preview.8 or later**. |
| Agent replies with "blocked by your organization's content exclusion policy" when reading files | You're on an old `Microsoft.Agents.AI.GitHub.Copilot` (< 1.10.0-rc1) which pinned `GitHub.Copilot.SDK 1.0.0-beta.2`. Upgrade Squad.Agents.AI to ≥ 0.2.0 (or pin MAF Copilot to ≥ 1.10.0-rc1 explicitly). |
| `--allow-all` shows up in arguments unexpectedly | Squad injects `--allow-all` by default so the agent can drive its tools. If you don't want that, set `options.CliArgs` to include `--allow-all-tools`, `--allow-all-paths`, `--allow-all-urls`, or the omnibus `--yolo` yourself — Squad detects any of these and skips the injection (case-insensitive). |

---

*Sample code and the full Squad.Agents.AI source live at [github.com/bradygaster/squad](https://github.com/bradygaster/squad/tree/dev/src/Squad.Agents.AI). The Aspire example lives at [CommunityToolkit/Aspire#1394](https://github.com/CommunityToolkit/Aspire/pull/1394). PRs welcome.*
