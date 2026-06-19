# Squad with Aspire

A multi-agent orchestration system built on .NET Aspire, where 7 AI agent squads collaborate through a shared messaging bus to build software together. Each squad is a specialized Copilot CLI agent with its own charter, tools, and domain expertise.

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                  Aspire AppHost                     │
│                                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌────────────┐  │
│  │  Squad Chat  │  │  Messaging  │  │  7 Squad   │  │
│  │  UI (React)  │──│  API (.NET) │──│  Agents    │  │
│  └─────────────┘  └──────┬──────┘  └────────────┘  │
│                          │                          │
│                   ┌──────┴──────┐                   │
│                   │   SQLite    │                   │
│                   │ Message Bus │                   │
│                   └─────────────┘                   │
└─────────────────────────────────────────────────────┘
```

**Messaging API** — ASP.NET Core service that hosts the SQLite-backed message bus, the coordinator service, and REST endpoints for inter-squad communication.

**Coordinator Service** — Background service that routes user messages to squads, dispatches to Copilot CLI agents, extracts knowledge from responses, and prevents infinite acknowledgment loops.

**Squad Agents** — Each squad runs as an ephemeral Copilot CLI session. Squads accumulate knowledge across conversations (persisted in a config store) so lessons carry forward without unbounded memory growth.

**Chat UI** — React + Vite frontend with @mention routing, dynamic squad colors, and a real-time message feed.

## The Squads

| Squad | Focus |
|-------|-------|
| **ideation-research-planning** | Product vision, competitive analysis, technical architecture |
| **experience-design** | UX/UI design, accessibility, design systems |
| **application-development** | Frontend, backend, API, and data development |
| **azure-infrastructure** | Cloud infrastructure, IaC, observability |
| **quality-testing** | Test strategy, automation, E2E coverage |
| **security-hardening** | Security review, threat modeling, hardening |
| **review-deployment** | CI/CD pipelines, code review, deployment |

Each squad has its own directory under `squads/` containing:
- `.squad/` — team charter, routing rules, ceremonies, decisions log
- `.github/agents/` — individual agent definitions (e.g., `ApiDeveloperAgent.agent.md`)
- `.copilot/skills/` — squad-specific skills and conventions

## How It Works

1. A user sends a message through the chat UI
2. The coordinator broadcasts it to all squads (or routes to a specific squad via `@squad-name`)
3. Each squad's Copilot CLI agent processes the message with full context of the conversation
4. Squads can message each other directly using the `squad_send_message` tool
5. Knowledge blocks (`<knowledge>...</knowledge>`) are extracted from responses and persisted for future sessions

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling)
- [Node.js 20+](https://nodejs.org)
- [GitHub Copilot CLI](https://docs.github.com/en/copilot)

## Running

```bash
cd src/AspireWithSquad.AppHost
dotnet run
```

Aspire will start all resources — the messaging API, all 7 squad agents, and the chat UI. Open the Aspire dashboard to see the full topology and traces.

## Project Structure

```
├── src/
│   ├── AspireWithSquad.AppHost/          # Aspire orchestrator
│   ├── AspireWithSquad.MessagingApi/     # Messaging bus + coordinator
│   ├── AspireWithSquad.MessagingApi.Tests/
│   ├── CommunityToolkit.Aspire.Hosting.Squad/  # Squad hosting extension
│   └── squad-chat-ui/                   # React chat frontend
├── squads/
│   ├── application-development/         # Each squad's charter + agents
│   ├── azure-infrastructure/
│   ├── experience-design/
│   ├── ideation-research-planning/
│   ├── quality-testing/
│   ├── review-deployment/
│   └── security-hardening/
└── SquadWithAspire.slnx
```

## Key Design Decisions

- **Ephemeral sessions** — Squad agents spin up fresh Copilot CLI sessions per message, avoiding unbounded memory growth from long-running conversations.
- **Knowledge persistence** — Squads emit `<knowledge>` blocks that are extracted, timestamped, and injected into future prompts so institutional knowledge survives across sessions.
- **Ack-loop prevention** — A content filter (`IsNonActionable`) detects acknowledgment-only messages and stops them from triggering infinite reply chains between squads.
- **Single squad registry** — `SquadRegistry` is the single source of truth for squad names, consumed by the coordinator, API endpoints, and tests.
- **OpenTelemetry tracing** — All messaging and agent dispatch operations are instrumented and export to the Aspire dashboard via OTLP.
