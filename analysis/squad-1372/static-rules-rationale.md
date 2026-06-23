# Static-Analysis Rules: Rationale & Rollout

**Companion to**: `.semgrep/squad-spawn-rules.yml`
**Issue**: bradygaster/squad#1372 (preflight) + spawn-side injection follow-up.

## 1. Why semgrep + ESLint, not one or the other

| Layer | Strength | Weakness |
|---|---|---|
| ESLint plugin | Editor feedback, fast, integrates with `--fix` | Can't easily express "args array contains non-literal" cross-rule with the same fidelity as semgrep's `metavariable-pattern` |
| semgrep | Strong pattern language, CI-first, supports `pattern-inside` and metavariable constraints | No live editor feedback by default |

We ship semgrep as the **enforcing** layer in CI and document an ESLint companion rule for the editor experience. The semgrep ruleset is authoritative.

## 2. Rule-by-rule rationale

### `squad-no-shell-true-with-tainted-args` (ERROR)

The root cause of the spawn-side finding. Catches `execFile`/`spawn`/`spawnSync` (and their `Sync` variants) called with `{ shell: true }` or `{ shell: IS_WINDOWS }` when *any* element of the args array is a non-literal expression (variable, function call, member access, template literal with interpolation).

We intentionally **do not** also ban `shell:true` with all-literal args — there are legitimate uses (e.g., calling `cmd /c dir` for diagnostics in `doctor.ts`). The taint condition is what differentiates safe from unsafe.

**False-positive escape hatch**: a developer who has manually escaped a tainted arg and accepts the residual risk can suppress with `// nosemgrep: squad-no-shell-true-with-tainted-args` plus a justification comment. Security-hardening-squad reviews every suppression in PR.

### `squad-no-bare-copilot-execfile` (WARNING)

Stops regression to bare-name `copilot` resolution. Tied to `resolveCopilotCmd()` adoption. Downgraded from ERROR because legitimate test fixtures may still need to invoke `'copilot'` directly with mocks — flagging as WARNING gives reviewers a chance to confirm the call site is a test/fixture.

### `squad-no-string-split-as-spawn-args` (WARNING)

Catches the `copilotFlags.split(/\s+/)` anti-pattern. WARNING-level because some legitimate use cases (e.g., splitting a known-good `JSON.stringify(['--foo', '--bar'])`-equivalent string) exist. The auto-fix recommended in the message is to switch the operator interface to `string[]` from the start.

## 3. Rollout plan

1. **Week 1 (immediate)**: Land `.semgrep/squad-spawn-rules.yml` in `bradygaster/squad-with-aspire@main`. Add a `semgrep ci` step to a *new* `security-static.yml` GitHub Actions workflow that runs on PRs to `main`/`dev`. **Fail PR on ERROR-level findings**, comment on WARNING-level.
2. **Week 1**: Confirm rules trigger on the historical bug (loop.ts pre-fix) — sanity check that the test corpus catches the regression we just fixed.
3. **Week 2**: Run semgrep across `bradygaster/squad@dev` to enumerate every existing call site that needs migration. Publish results as a tracking issue (when EMU restrictions are lifted) or as `analysis/squad-1372/spawn-audit.md`.
4. **Week 2**: Add `@squad/eslint-plugin` with companion rule `no-shell-true-with-tainted-args` (re-uses TypeScript AST; mirrors the semgrep semantics). Ship as devDependency.
5. **Week 3**: Once spawn-side fix lands, **upgrade `squad-no-bare-copilot-execfile` from WARNING to ERROR**. Gate via a `severity-override` ENV in the CI workflow to allow phased rollout.

## 4. CI workflow stub (drop into `.github/workflows/security-static.yml`)

```yaml
name: security-static
on:
  pull_request:
    branches: [main, dev]
  push:
    branches: [main, dev]
permissions:
  contents: read
  pull-requests: write
jobs:
  semgrep:
    runs-on: ubuntu-latest
    container: returntocorp/semgrep:latest
    steps:
      - uses: actions/checkout@v4
      - name: Run semgrep
        run: semgrep --config .semgrep/squad-spawn-rules.yml --error --strict --json --output semgrep.json .
      - name: Comment findings on PR
        if: always() && github.event_name == 'pull_request'
        uses: actions/github-script@v7
        with:
          script: |
            const fs = require('fs');
            const r = JSON.parse(fs.readFileSync('semgrep.json', 'utf8'));
            const lines = (r.results || []).map(x => `- **${x.check_id}** \`${x.path}:${x.start.line}\` — ${x.extra.message.split('\n')[0]}`);
            if (lines.length) {
              await github.rest.issues.createComment({
                ...context.repo, issue_number: context.issue.number,
                body: `### Security static analysis findings\n\n${lines.join('\n')}\n\nSee \`analysis/squad-1372/static-rules-rationale.md\`.`
              });
            }
```

## 5. Suppression policy

- ERROR-level findings: suppression requires (a) `// nosemgrep:<rule-id>` comment, (b) justification in the same comment, (c) sec-squad sign-off in PR review.
- WARNING-level findings: suppression requires (a) and (b); sec-squad review optional.
- Repeated suppressions of the same rule across PRs → revisit the rule itself.

## 6. Test coverage of the rules themselves

Each rule should have a positive/negative pair in `.semgrep/__tests__/`:

```
.semgrep/__tests__/no-shell-true-with-tainted-args.test.yaml
.semgrep/__tests__/no-shell-true-with-tainted-args.fixture.ts
```

Standard semgrep test harness: `semgrep --test --config .semgrep/`. Add to CI workflow above.

— security-hardening-squad, static-analysis subagent
