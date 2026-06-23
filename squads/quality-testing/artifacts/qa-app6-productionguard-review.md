# QA Review — APP-6 ProductionGuard Checks

**Reviewing:** `docs/security/app-6/productionguard-checks.md` @ `26dbb45` (`origin/security/app-6-productionguard-checks`)
**Reviewer:** quality-testing-squad
**Date:** 2026-06-23
**Severity:** mixed (1 critical, 1 medium, 1 clarification)
**Status:** EMU blocks gh issue create — this markdown is the issue of record.

The doc is well-structured and the failure-mode framing is right. Three issues in the C# itself that will bite when app-dev pastes it in. Filing now so app-dev catches them in the wire-up PR.

---

## DEFECT-1 (CRITICAL) — Check 3 will silently no-op in every real app

`SensitivePropertiesEncryptedCheck.Run` does:

```csharp
var contexts = services.GetServices<DbContext>().ToList();
if (contexts.Count == 0)
    return GuardCheckResult.Pass(Id, "No DbContext registered — nothing to scan.");
```

**Problem:** `AddDbContext<AppDbContext>(...)` registers `AppDbContext` as scoped — NOT as the base `DbContext` type. `GetServices<DbContext>()` returns an empty enumerable for every real EF Core app. The check returns `Pass` and the entire `[DataClass(Sensitive)]` reflection guard is dead code. The exact failure mode this check is supposed to catch (new entity added without converter) ships to prod with a green light.

**Second problem:** `DbContext` is scoped by default. Resolving a scoped service from the root `IServiceProvider` throws `InvalidOperationException` in dev with `ValidateScopes=true`. Even after fixing #1, you have to `services.CreateScope()` first.

**Fix:** Accept `Type[] contextTypes` as a constructor arg (registered explicitly at wire-up time). Then:

```csharp
using var scope = services.CreateScope();
foreach (var ctxType in _contextTypes)
{
    var ctx = (DbContext)scope.ServiceProvider.GetRequiredService(ctxType);
    foreach (var entityType in ctx.Model.GetEntityTypes()) { ... }
}
```

The wire-up site already knows the context types — it's the same place that calls `AddDbContext<AppDbContext>`. Alternatively, expose a marker registration `services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>())` and document it as a precondition — but that's fragile (silent break if the next dev adds a second context and forgets the alias).

---

## DEFECT-2 (MEDIUM) — Check 2 has no timeout on Key Vault GET

`client.GetKey(cmkName)` has no `CancellationToken`. If KV is partially reachable (throttling, AAD token acquisition stall, transient network blip), boot hangs indefinitely on this synchronous call. Startup checks should be bounded.

**Fix:** Accept a `TimeSpan` budget (default ~10s), use `CancellationTokenSource(budget)`, pass token to `GetKey(cmkName, cancellationToken: cts.Token)`, and on `OperationCanceledException` return `Fail` with a clear "KV unreachable within {N}s" message. Useful ops signal — distinguishes "KV is down" from "CMK doesn't exist".

Also: the `50–200ms cold` estimate in Performance is optimistic. Cold containers with fresh MSI token acquisition can hit 1–2s. Doc should say "<2s typical, hard cap 10s".

---

## DEFECT-3 (CLARIFICATION) — `GuardCheckResult.Warn` factory not in the contract

Check 1 only demonstrates `Pass` and `Fail`. Check 2 uses `GuardCheckResult.Warn(...)` for the CMK-expiring-in-7d case. Is `Warn` an actual factory, or did it slip in from a different result type? If intentional, the doc should declare it alongside Pass/Fail. Related: confirm the deployment gate workflow handles `Warn` correctly (passes the gate but surfaces to humans?). Open Question #1 in the doc is the same concern — worth resolving before this lands.

---

## Test matrix QA will own (post-implementation)

When app-dev creates `src/TravelAssistant.ProductionGuard` + `tests/TravelAssistant.ProductionGuard.Tests/Checks/`, QA lands these 21 unit tests (7 scenarios × 3 checks per doc's matrix). Mocking plan:

| Check | Mock surface | Approach |
|---|---|---|
| Check 1 (`PiiCipherRegisteredCheck`) | `IServiceProvider` | `ServiceCollection().BuildServiceProvider()` — with/without `AddSingleton<IPiiCipher, FakePiiCipher>()` |
| Check 2 (`CmkNameResolvesCheck`) | `KeyClient` | Inject via constructor (refactor — doc instantiates inline). Use `Moq` on `KeyClient` returning `Response<KeyVaultKey>` for happy/403/404/disabled/expiring/cancelled |
| Check 3 (`SensitivePropertiesEncryptedCheck`) | `DbContext` | In-memory provider + handcrafted entity with `[DataClass(Sensitive)]` annotated property — one with `HasConversion(new EncryptedPiiConverter(...))`, one without |

Check 2's mockability needs a refactor: extract `IKeyClient` or accept `Func<Uri,KeyClient>` for injection. Inline `new KeyClient(...)` is untestable.

---

## Recommended commit sequence

1. **Security or app-dev:** Patch the doc with DEFECT-1, DEFECT-2, DEFECT-3 fixes (Check 3 takes `Type[] contextTypes`; Check 2 takes `TimeSpan budget` + `Func<Uri,KeyClient>`; declare `Warn` or remove it).
2. **App-dev:** Create `src/TravelAssistant.ProductionGuard` + `tests/TravelAssistant.ProductionGuard.Tests/Checks/` (empty test project, just csproj + `Usings.cs`).
3. **QA:** Land the 21-test matrix on `qa/app-6-productionguard-tests`.
4. **App-dev:** Wire `AddProductionGuardCheck<...>` in `Program.cs` and verify all 21 tests pass.

cc security-hardening-squad, application-development-squad
