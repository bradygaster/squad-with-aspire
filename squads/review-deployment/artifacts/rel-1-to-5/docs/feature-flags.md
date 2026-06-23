# Feature Flags (REL-4)

**Owner:** review-deployment-squad (Drake) + application-development-squad (Hicks)
**Store:** Azure App Configuration (Feature Management)
**Library:** `Microsoft.FeatureManagement.AspNetCore`

## Why

We're trunk-based. Short-lived branches merge to `main` daily, but
LLM-planner prompt/model changes ship behind flags so we can:

- Roll out per-environment without re-deploying.
- Kill-switch a misbehaving model in < 30 seconds.
- A/B prompt variants against eval suites in production.

## Flag taxonomy

| Prefix          | Meaning                              | Owner squad                    |
| --------------- | ------------------------------------ | ------------------------------ |
| `llm.*`         | Model / prompt selection             | application-development        |
| `booking.*`     | Booking provider toggles & retries   | application-development        |
| `ux.*`          | UI / interaction experiments         | experience-design              |
| `safety.*`      | Content filters, PII redaction       | security-hardening             |
| `ops.*`         | Operational kill switches            | review-deployment              |

## Required flags (day 1)

- `llm.planner.model` (string) — model id used by `ILlmGateway` for the
  itinerary planner. Default: `gpt-4o-mini`.
- `llm.planner.systemPrompt.version` (string) — version key of the prompt
  used by the planner. Default: `v1`.
- `ops.deploy.killSwitch` (bool) — when `true`, returns 503 from
  `/api/plan` without invoking the LLM. Default: `false`.

## Conventions

- **One flag, one decision.** Don't reuse a flag for multiple call sites.
- **Default off in production until eval suite passes.** Owner squad
  attaches the eval run id in the flag description.
- **Sunset every flag.** Each flag has a `sunset` tag with an ISO date.
  Drake (release engineer) prunes expired flags monthly.
- **Never gate auth, payment, or PII handling behind a flag** without
  security-hardening-squad sign-off. Those paths must be on by default.

## Wiring sketch

```csharp
// Program.cs
builder.Configuration.AddAzureAppConfiguration(opt =>
{
    opt.Connect(new Uri(builder.Configuration["AppConfig:Endpoint"]!),
                new DefaultAzureCredential())
       .UseFeatureFlags(ff => ff.CacheExpirationInterval = TimeSpan.FromSeconds(30));
});
builder.Services.AddFeatureManagement();
```

```csharp
// LlmGateway.cs
public sealed class LlmGateway(IFeatureManager flags, IConfiguration cfg)
{
    public async Task<string> PlanAsync(string userPrompt, CancellationToken ct)
    {
        if (await flags.IsEnabledAsync("ops.deploy.killSwitch"))
            throw new ServiceUnavailableException("planner disabled by flag");

        var model = cfg["llm:planner:model"] ?? "gpt-4o-mini";
        // ... call provider ...
    }
}
```

## Rollback via flag

Flipping `ops.deploy.killSwitch=true` is the fastest mitigation — faster than
a Container Apps revision rollback. Use it first, then perform the proper
rollback per `docs/runbooks/rollback.md`.
