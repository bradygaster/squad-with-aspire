# SEC-1b PII Redactor CI Gate

CI wiring for the SEC-1b PII redactor shipped by security-hardening-squad on
branch `security/sec-1b-pii-redactor` (25/25 tests green at push time).

## Install

```bash
git checkout security/sec-1b-pii-redactor    # or main, after merge
cp squads/review-deployment/artifacts/sec-1b-pii-gate/pii-redactor-gate.yml \
   .github/workflows/pii-redactor-gate.yml
git add .github/workflows/pii-redactor-gate.yml
git commit -m "ci(sec-1b): add PII redactor gate (100% adversarial / 0% FP)"
```

## Behavior

- Triggers on PR + push to `main` when `src/TravelAssistant.Security/**`,
  `tests/TravelAssistant.Security.Tests/**`, or the workflow itself changes.
- **Self-skips** when `tests/TravelAssistant.Security.Tests/TravelAssistant.Security.Tests.csproj`
  or `tests/TravelAssistant.Security.Tests/Pii/goldens.yaml` is absent —
  safe to land ahead of or independently of the SEC-1b branch merge.
- Runs only the `Pii` test filter; uploads TRX for forensics.
- Parses TRX inline (Python stdlib, no extra deps) and enforces:
  - **100% adversarial-block** — any failing test NOT matching
    `benign|fp|false.?positive` is fatal.
  - **0% benign FP** — any failing test that DOES match those patterns is fatal.
- Fails loudly if zero PII tests are discovered (catches filter drift).

## Sibling workflows

- `prompt-injection-gate.yml` (SEC-5b, prompt-injection corpus gate)
- `supply-chain-scan.yml` (SEC-5b, vulnerable + deprecated + allowlist)

Same self-skip pattern; consistent gate semantics across SEC bundle.

## Tuning

If sec-squad renames the FP-guard convention, update the regex
`benign|fp|false.?positive` in the inline Python parser. The classifier is the
single source of truth for "what counts as a benign FP test".

If future PII goldens are added under a non-`Pii` namespace, broaden the
`--filter "FullyQualifiedName~Pii"` selector to match.
